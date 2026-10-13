using LandmarkEmulator.Shared.Network.Cryptography;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Shared.Network.Message.Model;
using LandmarkEmulator.Shared.Network.Packets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LandmarkEmulator.Shared.Network
{
    public abstract class GameSession : NetworkSession, ISoeOutputTelemetry
    {
        /// <summary>SOE disconnect reason aligned with UnacknowledgedTimeout (Live sample uses 6).</summary>
        public const ushort DisconnectReasonUnacknowledgedTimeout = 6;

        public ProtocolVersion ProtocolVersion { get; private set; }
        public string EncryptionKey { get; protected set; }

        Thread outgoingPacketThread = null;

        private readonly ConcurrentQueue<ProtocolPacket> outgoingPriorityPackets = new();
        private readonly ConcurrentQueue<ProtocolPacket> outgoingDataPackets = new();
        private readonly ConcurrentQueue<ProtocolPacket> outOfOrderPackets = new();
        private readonly DataStreamInput inputStream;
        private readonly DataStreamOutput outputStream;

        /// <summary>SOE wire session id from the client's SessionRequest.</summary>
        public uint SoeSessionId { get; private set; }

        private byte serverCrcLength { get; set; } = 2;
        private uint clientUdpLength { get; set; }
        private uint serverUdpLength { get; set; } = 512u;
        private uint crcSeed { get; set; }
        private byte encryptionMethod { get; set; } = 1;
        private byte encryptionMethod2 { get; set; } = 0;

        /// <summary>
        /// SessionReply method 1 installs UdpCompressionHandler (LoginUdp only).
        /// Wire: opcode + flag + body + CRC. Method 0 (Gateway) has no flag.
        /// See AgentShare/SoeProtocol.md § Compression.
        /// </summary>
        private bool SoeCompressionFlag => encryptionMethod != 0;

        /// <summary>True when SessionReply negotiated method 1 (every non-session packet carries a flag byte).</summary>
        public bool UsesSoeCompression => SoeCompressionFlag;

        /// <summary>
        /// Offer the world gateway the compressing body transform (method 1) instead of live's plain copy (method 0).
        /// Off: the 0.589 client fails to enter the world with it (tested 2026-10-08).
        /// </summary>
        public static bool GatewayCompression { get; set; } = false; // works (codec fixed), but zone data is RC4 ciphertext so it never shrinks

        private bool hasAuthed = false;

        /// <summary>True while DataWhole/DataFragment packets are still waiting to hit the wire.</summary>
        public bool HasQueuedDataPackets => !outgoingDataPackets.IsEmpty;

        /// <summary>True when the output stream still has unacked cached sequences.</summary>
        public bool HasUnackedOutput => outputStream.MissingAcks;

        public ushort? OutputNextSequence => outputStream.NextSequence;

        public ushort? OutputLastAck => outputStream.LastAck;

        /// <summary>Highest reliable sequence written to UDP (terrain debug).</summary>
        public ushort? OutputHighestSent => outputStream.HighestSentSequence;

        public int OutputAckPingMs => outputStream.AckPingMs;

        public int OutputCurrentRtoMs => outputStream.CurrentRtoMs;

        internal SoeReliabilityStats ReliabilityStats { get; } = new();

        /// <summary>
        /// Fragments flushed and <see cref="DataStreamOutput.MissingAcks"/> clear.
        /// Can be true too early if LastAck points past a null slot while higher seqs are live —
        /// prefer <see cref="IsOutputAckedThrough"/> for terrain backpressure.
        /// </summary>
        public bool OutputCaughtUp => !HasQueuedDataPackets && outputStream.LastAck.HasValue && !outputStream.MissingAcks;

        /// <summary>
        /// True when the client has acked through <paramref name="sequence"/> inclusive
        /// and <see cref="DataStreamOutput.LastAck"/> is not ahead of <see cref="DataStreamOutput.NextSequence"/>.
        /// Contiguous frontier only — selective <c>0x11</c> Acks advance LastAck after leading holes clear.
        /// </summary>
        public bool IsOutputAckedThrough(ushort sequence) => outputStream.IsAckedThrough(sequence);

        /// <summary>Assigned but unacked reliable sequences (desync reports over-window).</summary>
        public int OutputUnackedCount => outputStream.UnackedCount;

        /// <summary>
        /// Force-resend still-cached reliable packets (terrain catch-up soft-deadline kick).
        /// </summary>
        protected void ResendUnackedOutput() => outputStream.ResendAllUnacked();

        /// <summary>Force an immediate SOE reliability stats line (terrain flood milestones).</summary>
        protected void ForceReliabilityReport() => ReliabilityStats.MaybeReport(this, log, force: true);

        /// <summary>Max reliable datagrams sent per <see cref="ProcessOutPackets"/> loop iteration.</summary>
        protected virtual int ReliableBurstPerLoop => 64;

        public GameSession(string cryptoKey, bool subPacketAcks = false)
        {
            EncryptionKey = cryptoKey;

            inputStream = new DataStreamInput(this, subPacketAcks);
            inputStream.OnData += (sequence, gamePacket) =>
            {
                OnGamePacket(sequence, gamePacket);
            };
            outputStream = new DataStreamOutput(this);
            outputStream.OnData += (sequence, gamePacket, outOfOrder) =>
            {
                if (!gamePacket.IsFragment)
                    EnqueueProtocolMessage(new DataWhole
                    {
                        Sequence = sequence,
                        Data = gamePacket.Data,
                    }, new PacketOptions
                    {
                        IsSubpacket = false,
                        Compression = SoeCompressionFlag,
                        WasOutOfOrder = outOfOrder,
                        Sequence = sequence
                    });
                else
                    EnqueueProtocolMessage(new DataFragment
                    {
                        Sequence = sequence,
                        Data = gamePacket.Data
                    }, new PacketOptions
                    {
                        IsSubpacket = false,
                        Compression = SoeCompressionFlag,
                        WasOutOfOrder = outOfOrder,
                        Sequence = sequence
                    });
            };
        }

        /// <summary>
        /// This is fired to alert the Class inheritor that a packet is here for it to handle.
        /// </summary>
        /// <remarks>GameSession only handles Protocol packets directly. AuthSession, WorldSession, and any others handle Game Packets.</remarks>
        protected virtual void OnGamePacket(ushort sequence, byte[] data)
        {
            // deliberately empty
        }

        /// <summary>
        /// This is fired between incoming packets being parsed and outgoing packets being sent, to allow for the Class inheritor to handle its packets.
        /// </summary>
        /// <remarks>This is called from within Update().</remarks>
        protected virtual void OnProcessPackets()
        {
            // deliberately empty
        }

        /// <summary>
        /// Handle receiving data from connected client. Packets are processed immediately.
        /// </summary>
        public override void OnData(byte[] data)
        {
            // Setup Outgoing Packet Thread
            if (outgoingPacketThread == null)
            {
                outgoingPacketThread = new Thread(new ThreadStart(ProcessOutPackets));
                outgoingPacketThread.IsBackground = true;
                outgoingPacketThread.Start();
            }

            Heartbeat.OnHeartbeat();

            var packet = new ProtocolPacket(data, new PacketOptions
            {
                IsSubpacket = false,
                Compression = SoeCompressionFlag
            });
            HandleProtocolPacket(packet);

            base.OnData(data);
        }

        /// <summary>
        /// This runs in its own thread so we can send packets quickly
        /// </summary>
        private void ProcessOutPackets()
        {
            // Burst reliable datagrams per loop so ~MB CellsUpdate fragment floods finish
            // inside the client 6s SCENE timer (1-pkt + Sleep(10)/50 was taking 13–16s).
            while (true)
            {
                // The thread ends with its session. Before this, a disconnected session with packets still queued
                // (CanProcessPackets false, so nothing dequeues) looped on Sleep(0) at a full core forever - one
                // more spinning thread per relog / logout, starving the client's terrain contouring on the same PC.
                if (Disconnected)
                    return;

                if (!CanProcessPackets
                    || (outgoingPriorityPackets.IsEmpty && outOfOrderPackets.Count == 0 && outgoingDataPackets.IsEmpty))
                {
                    Thread.Sleep(10);
                    continue;
                }

                if (CanProcessPackets && outgoingPriorityPackets.TryDequeue(out ProtocolPacket priorityPacket))
                {
                    if (!priorityPacket.Ignore)
                    {
                        SendPacket(priorityPacket);

                        if (priorityPacket.Opcode == ProtocolMessageOpcode.Disconnect)
                        {
                            OnDisconnect();
                            return;
                        }
                    }

                }

                if (CanProcessPackets && outOfOrderPackets.TryDequeue(out ProtocolPacket outOfOrderPacket))
                    SendPacket(outOfOrderPacket);

                if (outOfOrderPackets.Count == 0 && outgoingPriorityPackets.IsEmpty)
                {
                    // Sliding window: do not put more reliable data on the wire while too many
                    // sequences are still unacked (client discards past maxInstandingPackets).
                    int burst = 0;
                    while (burst < ReliableBurstPerLoop
                        && outputStream.CanSendMoreReliable
                        && CanProcessPackets
                        && outgoingDataPackets.TryDequeue(out ProtocolPacket outPacket))
                    {
                        SendPacket(outPacket);
                        burst++;
                    }

                    if (burst == 0 && !outgoingDataPackets.IsEmpty && !outputStream.CanSendMoreReliable)
                    {
                        ReliabilityStats.RecordSendWindowPause();
                        Thread.Sleep(1);
                    }
                }

                Thread.Sleep(0);
            }
        }

        /// <summary>
        /// Invoked each server tick with the delta since the previous tick occurred.
        /// </summary>
        public sealed override void Update(double lastTick)
        {
            if (hasAuthed && outputStream.ProcessRtoResends())
            {
                log.Warn($"Oldest unacked reliable packet exceeded {DataStreamOutput.OldestUnackedTimeoutMs}ms; disconnecting");
                SendDisconnect(DisconnectReasonUnacknowledgedTimeout);
                return;
            }

            if (hasAuthed)
                ReliabilityStats.MaybeReport(this, log);

            // We still process packets at regular interval so that things stay in sync.
            OnProcessPackets();

            base.Update(lastTick);
        }

        private void HandleProtocolPacket(ProtocolPacket packet)
        {
            IProtocol message = MessageManager.Instance.GetProtocolMessage(packet.Opcode);
            if (message == null)
            {
                log.Warn($"Received unknown protocol packet {packet.Opcode:X} : {BitConverter.ToString(packet.Data)}");
                return;
            }

            if (!hasAuthed && message is not SessionRequest)
            {
                log.Debug("Client sent data without session or session requests. Disconnecting.");
                OnDisconnect();
                return;
            }

            MessageHandlerDelegate handlerInfo = MessageManager.Instance.GetProtocolMessageHandler(packet.Opcode);
            if (handlerInfo == null)
            {
                log.Warn($"Received unhandled protocol packet {packet.Opcode}(0x{packet.Opcode:X}).");
                return;
            }

            log.Trace($"Received packet {packet.Opcode}(0x{packet.Opcode:X}) : {BitConverter.ToString(packet.Data)}");

            byte[] payload = packet.Data;
            if (MessageManager.Instance.GetOpcodeData(message, out (ProtocolMessageOpcode, bool) opcodeData)
                && opcodeData.Item2
                && hasAuthed
                && !packet.PacketOptions.IsSubpacket)
            {
                // Protected opcodes: strip trailing crcBytes and verify against session seed.
                if (!CrcProvider.TryVerifyAndStrip(BuildFullPacket(packet.Opcode, payload), crcSeed, serverCrcLength, out byte[] fullWithoutCrc))
                {
                    log.Warn($"CRC reject for {packet.Opcode}(0x{packet.Opcode:X})");
                    return;
                }
                // fullWithoutCrc is opcode (2) + body; reader expects body only.
                payload = fullWithoutCrc.AsSpan(2).ToArray();
            }

            var reader = new ProtocolPacketReader(payload);

            message.Read(reader, packet.PacketOptions);
            if (reader.BytesRemaining > 0 && message is not Ping)
                log.Warn($"Failed to read entire contents of packet {packet.Opcode} ({reader.BytesRemaining} bytes remaining)");

            try
            {
                handlerInfo.Invoke(this, message);
            }
            catch (InvalidPacketValueException exception)
            {
                log.Error(exception);
                OnDisconnect();
            }
            catch (Exception exception)
            {
                log.Error(exception);
            }
        }

        private static byte[] BuildFullPacket(ProtocolMessageOpcode opcode, byte[] body)
        {
            var full = new byte[2 + (body?.Length ?? 0)];
            full[0] = (byte)(((ushort)opcode >> 8) & 0xFF);
            full[1] = (byte)((ushort)opcode & 0xFF);
            if (body != null && body.Length > 0)
                Buffer.BlockCopy(body, 0, full, 2, body.Length);
            return full;
        }

        public void EnqueueProtocolMessage(IProtocol message, PacketOptions options)
        {
            if (!MessageManager.Instance.GetOpcodeData(message, out (ProtocolMessageOpcode, bool) opcodeData))
            {
                log.Warn("Failed to send message with no attribute!");
                return;
            }

            if (options.IsPriority)
            {
                outgoingPriorityPackets.Enqueue(new ProtocolPacket(opcodeData.Item1, message, opcodeData.Item2, options));
                return;
            }

            switch (opcodeData.Item1)
            {
                // AckAll coalesce (client ackDeduping). Single Ack (0x11) is the common
                // in-order path — do not drop prior singles when coalescing AckAll.
                case ProtocolMessageOpcode.AckAll:
                    foreach (var packet in outgoingPriorityPackets.ToList())
                        if (packet.Opcode == ProtocolMessageOpcode.AckAll)
                            packet.Ignore = true;

                    outgoingPriorityPackets.Enqueue(new ProtocolPacket(opcodeData.Item1, message, opcodeData.Item2, options));
                    break;
                case ProtocolMessageOpcode.Ack:
                    outgoingPriorityPackets.Enqueue(new ProtocolPacket(opcodeData.Item1, message, opcodeData.Item2, options));
                    break;
                case ProtocolMessageOpcode.MultiPacket:
                    outgoingPriorityPackets.Enqueue(new ProtocolPacket(opcodeData.Item1, message, opcodeData.Item2, options));
                    break;
                default:
                    if (options.WasOutOfOrder)
                        outOfOrderPackets.Enqueue(new ProtocolPacket(opcodeData.Item1, message, opcodeData.Item2, options));
                    else
                        outgoingDataPackets.Enqueue(new ProtocolPacket(opcodeData.Item1, message, opcodeData.Item2, options));
                    break;
            }
        }

        protected void SendPacket(ProtocolPacket packet)
        {
            List<byte> data = new();
            var writer = new ProtocolPacketWriter(data);

            writer.Write((ushort)packet.Opcode);
            writer.WriteBytes(packet.Data);

            byte[] newData = data.ToArray();
            // UseEncryption attribute means "append CRC" for protected post-session opcodes.
            if (packet.UseEncryption)
                newData = CrcProvider.AppendCRC(newData, crcSeed);

            if (newData == null || newData.Length == 0)
                return;

            log.Trace($"Sending packet {packet.Opcode}(0x{packet.Opcode:X}) : {BitConverter.ToString(packet.Data)}");

            SendRaw(newData);

            if (packet.Opcode is ProtocolMessageOpcode.Data or ProtocolMessageOpcode.DataFragment
                && packet.PacketOptions != null)
            {
                outputStream.NotifySent(packet.PacketOptions.Sequence);
            }
        }

        protected void PackAndSend(byte[] data)
        {
            // Send to Output Stream for Packing
            // Output Stream will emit the packed data to be added to outgoingPackets Queue to be sent.
            outputStream.PackData(data);
        }

        protected void ToggleEncryption(bool usingEncryption)
        {
            inputStream.SetEncryption(usingEncryption);
            outputStream.SetEncryption(usingEncryption);
        }

        [ProtocolMessageHandler(ProtocolMessageOpcode.SessionRequest)]
        public void HandleSessionRequest(SessionRequest request)
        {
            log.Info($"{request.SessionId}, protocolVer={request.ProtocolVersion}, {request.UdpLength}, {request.Protocol}");

            hasAuthed = true;

            SoeSessionId    = request.SessionId;
            clientUdpLength = request.UdpLength;

            if (BackendSessionId == Guid.Empty)
                AssignBackendSessionId(Guid.NewGuid());

            if (clientUdpLength != 512)
                throw new InvalidOperationException();

            if (request.Protocol == "LoginUdp_10")
                ProtocolVersion = ProtocolVersion.LoginUdp_10;
            if (request.Protocol == "LoginUdp_9")
                ProtocolVersion = ProtocolVersion.LoginUdp_9;
            if (request.Protocol == "ExternalGatewayApi_3")
            {
                ProtocolVersion = ProtocolVersion.ExternalGatewayApi_3;
                // Gateway: encrypt methods 0/0 (memcpy passthrough). Auth defaults 1/0 (Live).
                encryptionMethod  = GatewayCompression ? (byte)1 : (byte)0;
                encryptionMethod2 = 0;
            }

            byte[] seedBytes = RandomProvider.GetBytes(4u);
            crcSeed = BitConverter.ToUInt32(seedBytes, 0);
            if (crcSeed == 0)
                crcSeed = 1;

            // Overhead: opcode(2) + optional compression flag(1) + seq(2) + CRC(2).
            outputStream.SetFragmentSize(clientUdpLength - (SoeCompressionFlag ? 7u : 6u));
            EnqueueProtocolMessage(new SessionReply
            {
                SessionId          = SoeSessionId,
                CRCSeed            = crcSeed,
                CRCLength          = serverCrcLength,
                EncryptionMethod   = encryptionMethod,
                EncryptionMethod2  = encryptionMethod2,
                UdpLength          = serverUdpLength,
                Unknown0           = request.ProtocolVersion != 0 ? request.ProtocolVersion : 3u
            }, new PacketOptions
            {
                IsSubpacket = false,
                IsPriority  = true
            });
        }

        [ProtocolMessageHandler(ProtocolMessageOpcode.DataFragment)]
        public void HandleDataFragment(DataFragment dataFragment)
        {
            inputStream.ProcessDataFragment(dataFragment);
        }

        [ProtocolMessageHandler(ProtocolMessageOpcode.Data)]
        public void HandleDataFragment(DataWhole dataWhole)
        {
            inputStream.ProcessDataFragment(dataWhole);
        }

        [ProtocolMessageHandler(ProtocolMessageOpcode.MultiPacket)]
        public void HandleMultiPacket(MultiPacket multiPacket)
        {
            // Immediately Handle these packets, if we add back to the queue, we get out of order problems.
            foreach (ProtocolPacket packet in multiPacket.Packets)
                HandleProtocolPacket(packet);
        }

        [ProtocolMessageHandler(ProtocolMessageOpcode.Ping)]
        public void HandlePing(Ping ping)
        {
            // Reply only — client does not treat Ping as a resend signal (RTO tick handles that).
            EnqueueProtocolMessage(new Ping(), new PacketOptions
            {
                IsPriority = true
            });
        }

        [ProtocolMessageHandler(ProtocolMessageOpcode.Disconnect)]
        public void HandleDisconnect(Disconnect disconnect)
        {
            OnDisconnect();
        }

        /// <summary>
        /// Send any queued protocol packets immediately, including after a gateway ForceDisconnect
        /// has been packed but before <see cref="OnDisconnect"/> stops packet processing.
        /// </summary>
        protected void FlushOutgoingProtocolPackets()
        {
            while (outgoingPriorityPackets.TryDequeue(out ProtocolPacket priorityPacket))
            {
                if (!priorityPacket.Ignore)
                    SendPacket(priorityPacket);
            }

            while (outOfOrderPackets.TryDequeue(out ProtocolPacket outOfOrderPacket))
                SendPacket(outOfOrderPacket);

            while (outgoingDataPackets.TryDequeue(out ProtocolPacket dataPacket))
                SendPacket(dataPacket);
        }

        private void SendDisconnect(ushort reason)
        {
            if (Disconnected || !WasConnected)
            {
                OnDisconnect();
                return;
            }

            SendPacket(new ProtocolPacket(ProtocolMessageOpcode.Disconnect, new Disconnect
            {
                SessionId = SoeSessionId,
                Reason    = reason
            }, true, new PacketOptions
            {
                IsPriority  = true,
                Compression = SoeCompressionFlag
            }));
            base.OnDisconnect();
        }

        public override void OnDisconnect()
        {
            if (Disconnected)
                return;

            if (hasAuthed)
                ReliabilityStats.MaybeReport(this, log, force: true);

            if (WasConnected)
                SendPacket(new ProtocolPacket(ProtocolMessageOpcode.Disconnect, new Disconnect
                {
                    SessionId = SoeSessionId,
                    Reason    = 0
                }, true, new PacketOptions
                {
                    IsPriority  = true,
                    Compression = SoeCompressionFlag
                }));

            base.OnDisconnect();
        }

        [ProtocolMessageHandler(ProtocolMessageOpcode.Ack)]
        public void HandleAck(Ack ack)
        {
            ReliabilityStats.RecordClientAck();
            outputStream.HandleAck(ack.Sequence);
        }

        [ProtocolMessageHandler(ProtocolMessageOpcode.AckAll)]
        public void HandleAckAll(AckAll ackAll)
        {
            ReliabilityStats.RecordClientAckAll();
            outputStream.HandleAckAll(ackAll.Sequence);
        }
    }
}
