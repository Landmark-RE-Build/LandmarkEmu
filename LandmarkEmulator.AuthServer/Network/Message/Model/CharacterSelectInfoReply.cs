using LandmarkEmulator.Shared.Game.Entity.Static;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Shared.Network.Message.Model.Shared;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.AuthServer.Network.Message.Model
{
    /// <summary>A character payload is a byte array on the wire: its bytes, written once, give both length and content.</summary>
    internal static class Payload
    {
        public static byte[] Bytes(Action<GamePacketWriter> write)
        {
            var bytes = new List<byte>();
            write(new GamePacketWriter(bytes));
            return bytes.ToArray();
        }

        public static int Measure(Action<GamePacketWriter> write) => Bytes(write).Length;
    }

    [AuthMessage(AuthMessageOpcode.CharacterSelectInfoReply, ProtocolVersion.LoginUdp_10)]
    public class CharacterSelectInfoReply : IWritable, IReadable
    {
        public class Character : IWritable, IReadable
        {
            public class CharacterPayload : IWritable, IReadable, ISize
            {
                /// <summary>
                /// One worn piece (client <c>CharacterAttachmentData</c>, 0x198 bytes; the payload list FUN_1430ae5d0 of records
                /// FUN_142c185c0) [P]: string +0x00, +0x60, +0xc0, +0x120, u32 +0x180, +0x184, +0x188, +0x18c, bool +0x190.
                /// Applied by FUN_142ebd620 (a per-slot record keyed by +0x188) → the actor's attach (vtable +0x340,
                /// FUN_142ebd740 → FUN_143b95320(model, +0x60, +0xc0, +0x120, +0x180, +0x184, +0x188, ..., +0x190)). The
                /// meanings of the strings and of +0x180 / +0x184 follow the ProxiedCharacter attachment code
                /// ("SetTintAndDecalAlias: tintAlias, decalAlias"; trace/0695/agent-storyhide.md) [P]; +0x18c, when not 0, is
                /// added to the character's id set at +0x380 (FUN_142e90e10) and +0x190 is passed through to the attach,
                /// meanings not traced further (OPEN-LEADS).
                /// </summary>
                public class CharacterAttachment : IWritable, IReadable, ISize
                {
                    public string ModelName { get; set; } = "";
                    /// <summary>+0x60.</summary>
                    public string TextureAlias { get; set; } = "";
                    /// <summary>+0xc0 (the item's TINT_ALIAS, ItemAppearances).</summary>
                    public string TintAlias { get; set; } = "";
                    /// <summary>+0x120.</summary>
                    public string DecalAlias { get; set; } = "";
                    /// <summary>+0x180.</summary>
                    public uint TintId { get; set; }
                    /// <summary>+0x184.</summary>
                    public uint CompositeEffectId { get; set; }
                    /// <summary>+0x188: the equipment slot (the per-slot key).</summary>
                    public AttachmentSlot Slot { get; set; }
                    /// <summary>+0x18c: 0 = nothing; else added to the character's id set (FUN_142e90e10).</summary>
                    public uint Value18C { get; set; }
                    /// <summary>+0x190: passed through to the actor attach (FUN_143b95320 last argument).</summary>
                    public bool Flag190 { get; set; }

                    public uint GetSize()
                    {
                        return (uint)Payload.Measure(Write);
                    }

                    public void Read(GamePacketReader reader)
                    {
                        ModelName = reader.ReadString();
                        TextureAlias = reader.ReadString();
                        TintAlias = reader.ReadString();
                        DecalAlias = reader.ReadString();
                        TintId = reader.ReadUInt();
                        CompositeEffectId = reader.ReadUInt();
                        Slot = (AttachmentSlot)reader.ReadUInt();
                        Value18C = reader.ReadUInt();
                        Flag190 = reader.ReadBool();
                        
                    }

                    public void Write(GamePacketWriter writer)
                    {
                        writer.Write(ModelName);
                        writer.Write(TextureAlias);
                        writer.Write(TintAlias);
                        writer.Write(DecalAlias);
                        writer.Write(TintId);
                        writer.Write(CompositeEffectId);
                        writer.Write((uint)Slot);
                        writer.Write(Value18C);
                        writer.Write(Flag190);
                    }
                }

                /// <summary>
                /// One entry of the character's friends list (client <c>Friends::Friend</c>, in a
                /// <c>SoeUtil::HashList&lt;Friends::Friend,20,-2&gt;</c> at payload +0x100; payload constructor FUN_1430afa40).
                /// Reader FUN_1430ae3a0 [P]: u32 n, then per friend u32 <see cref="Key"/> (the hash list's key, FUN_1430b31f0
                /// stores it at node +0x120; FUN_1430b34b0 files the node in bucket key % 20 and never compares it) and the
                /// record FUN_1430ad9e0 (0.589 FUN_143007650, same layout): string +0x00 (StringFixed&lt;32&gt;), string +0x40
                /// (StringFixed&lt;40&gt;), string +0x88 (StringFixed&lt;64&gt;), u64 +0xe8, bool +0xf0, string +0xf8
                /// (IString).
                /// <para>
                /// What the client does with it [P]: the character list handler (FUN_1430b4d80) keeps each character's list
                /// (store +0x210, FUN_1430b71b0); for the selected character FUN_1430c43e0 counts, per world in the server
                /// list, the friends whose <see cref="Online"/> is set and whose <see cref="Status"/> XML (parsed by
                /// FUN_1430b85f0: element "Status", attributes StatusId, MiniGameType, MiniGameNameId, ZoneId, WorldId)
                /// names that world, into the world row's +0x54; FUN_1430c3840 publishes it as the column
                /// <c>FriendsOnline</c> of the server list data source (GameData.Login.Servers). The 0.589 / 0.695
                /// ServerSelect UI (ServerSelectListItemRenderer.as) declares <c>_friendsString</c> but never fills it, so
                /// nothing on screen shows the count. The three strings and the u64 are only copied, never read.
                /// </para>
                /// </summary>
                public class Friend : IWritable, IReadable, ISize
                {
                    /// <summary>The hash list key (any value; the server numbers the entries 0, 1, 2...).</summary>
                    public uint Key { get; set; }
                    /// <summary>+0x00, StringFixed&lt;32&gt; [I: the friend's name; not read by the client].</summary>
                    public string Name { get; set; } = "";
                    /// <summary>+0x40, StringFixed&lt;40&gt; [I: not read by the client; sent empty].</summary>
                    public string Text40 { get; set; } = "";
                    /// <summary>+0x88, StringFixed&lt;64&gt; [I: not read by the client; sent empty].</summary>
                    public string Text64 { get; set; } = "";
                    /// <summary>
                    /// +0xe8, u64; its default is a static sentinel like the store's "no character" guid (DAT_14544ff18 /
                    /// DAT_1454500a8) [I: the friend's character guid; not read by the client].
                    /// </summary>
                    public ulong CharacterGuid { get; set; }
                    /// <summary>+0xf0: online; only online friends are counted [P FUN_1430c43e0].</summary>
                    public bool Online { get; set; }
                    /// <summary>+0xf8: the presence XML, see <see cref="StatusXml"/> [P FUN_1430b85f0].</summary>
                    public string Status { get; set; } = "";

                    /// <summary>
                    /// The Status XML the client parses (FUN_1430b85f0, attribute mode: the SerializeXml reader's flag +2 is
                    /// 1 from its constructor FUN_142b401d0). Only WorldId is used (compared with each world's ServerId);
                    /// StatusId / MiniGameType / MiniGameNameId / ZoneId are read but nothing uses them [I: StatusId,
                    /// MiniGameType, MiniGameNameId sent 0].
                    /// </summary>
                    public static string StatusXml(uint worldId, uint zoneId, uint statusId = 0, uint miniGameType = 0, uint miniGameNameId = 0)
                        => $"<Status StatusId=\"{statusId}\" MiniGameType=\"{miniGameType}\" MiniGameNameId=\"{miniGameNameId}\" "
                           + $"ZoneId=\"{zoneId}\" WorldId=\"{worldId}\"/>";

                    public uint GetSize() => (uint)Payload.Measure(Write);

                    public void Read(GamePacketReader reader)
                    {
                        Key = reader.ReadUInt();
                        Name = reader.ReadString();
                        Text40 = reader.ReadString();
                        Text64 = reader.ReadString();
                        CharacterGuid = reader.ReadULong();
                        Online = reader.ReadBool();
                        Status = reader.ReadString();
                    }

                    public void Write(GamePacketWriter writer)
                    {
                        writer.Write(Key);
                        writer.Write(Name ?? "");
                        writer.Write(Text40 ?? "");
                        writer.Write(Text64 ?? "");
                        writer.Write(CharacterGuid);
                        writer.Write(Online);
                        writer.Write(Status ?? "");
                    }
                }

                // The payload (reader FUN_1430ad7c0, store FUN_1430b4d80 → FUN_1430ba5c0) [P]. The handler copies some fields into
                // the character's summary (stack record, disassembly 1430b4ebe-1430b4eec): +0x18 → summary +0x20, ModelId
                // +0x24 → +0x3c, Gender +0x28 → +0x40, Race +0x2c → +0x44, ProfileId +0x30 → +0x48, +0x34 → +0x4c, +0x58 →
                // +0x50, ServerId +0x1d0 → +0x30. The data source row GameData.Login.Characters (FUN_1430bb290) takes Guid,
                // Name, ServerId, ServerName, ModelId, Gender, Race, ProfileId from it; nothing reads summary +0x20 / +0x4c /
                // +0x50, and +0x1c, +0x20 and the int list +0x38 are not copied at all: the "Unused" fields below.
                public string Name { get; set; } = "Player";
                /// <summary>+0x18, read as a signed char; copied, never read [P].</summary>
                public byte Unused18 { get; set; }
                /// <summary>+0x1c; never read [P] (sent 1, as before).</summary>
                public uint Unused1C { get; set; } = 1;
                /// <summary>+0x20; never read [P] (this field was called HeadId before; the head comes from the model).</summary>
                public uint Unused20 { get; set; }
                public uint ModelId { get; set; }
                public uint Gender { get; set; }
                public uint Race { get; set; }
                public uint ProfileId { get; set; }
                /// <summary>+0x34; copied, never read [P].</summary>
                public uint Unused34 { get; set; }
                /// <summary>+0x58; copied, never read [P].</summary>
                public ulong Unused58 { get; set; }
                /// <summary>+0x38, an int list (FUN_142c34c30); never read [P].</summary>
                public List<uint> Unused38 { get; set; } = new();
                public CharacterModelInfo Model { get; set; } = new();
                public List<CharacterAttachment> CharacterAttachments { get; set; } = new();
                public List<Friend> Friends { get; set; } = new();
                /// <summary>
                /// +0x1d0: the world the character plays on, the data source column ServerId [P FUN_1430bb290];
                /// CharacterSelection.lua (SelectCharacterByIndex, OnServersListUpdate) selects that world for it, 0 = the
                /// best world (GetBestServer). The login server sends the character's last world when it is in the server list.
                /// </summary>
                public ulong ServerId { get; set; }

                /// <summary>
                /// The payload is the entity's ApplicationData byte array (u32 length + bytes; Login::EntityDetails reader
                /// FUN_143a544a0), which the client parses on its own (FUN_1430ad7c0 over exactly those bytes, from
                /// FUN_1430b4d80): Read takes the length and parses within it, Write measures what it writes.
                /// </summary>
                public void Read(GamePacketReader reader)
                {
                    uint size = reader.ReadUInt();
                    var payload = new GamePacketReader(reader.ReadBytes(size));
                    ReadBody(payload);
                    if (payload.BytesRemaining != 0)
                        throw new System.IO.InvalidDataException($"Character payload: {payload.BytesRemaining} of {size} bytes not read.");
                }

                private void ReadBody(GamePacketReader reader)
                {
                    Name = reader.ReadString();
                    Unused18 = reader.ReadByte();
                    Unused1C = reader.ReadUInt();
                    Unused20 = reader.ReadUInt();
                    ModelId = reader.ReadUInt();
                    Gender = reader.ReadUInt();
                    Race = reader.ReadUInt();
                    ProfileId = reader.ReadUInt();
                    Unused34 = reader.ReadUInt();
                    Unused58 = reader.ReadULong();

                    var arr142Count = reader.ReadUInt();
                    for (int i = 0; i < arr142Count; i++)
                        Unused38.Add(reader.ReadUInt());

                    Model.Read(reader);

                    var attachmentCount = reader.ReadUInt();
                    for (int i = 0; i < attachmentCount; i++)
                    {
                        var attachment = new CharacterAttachment();
                        attachment.Read(reader);
                        CharacterAttachments.Add(attachment);
                    }

                    // Friends (FUN_1430ae3a0), then the u64 at +0x1d0.
                    uint friendCount = reader.ReadUInt();
                    // The smallest Friend is 29 bytes: u32 key, three empty strings (u32 each), u64 guid, bool online,
                    // empty status (u32). The bound was 33, so three or more friends with short names were refused
                    // (review 2026-10-18b #15; this reader serves the capture parser and the tests).
                    if (friendCount > reader.BytesRemaining / 29u)
                        throw new System.IO.InvalidDataException($"{friendCount} friends refused.");
                    for (uint i = 0; i < friendCount; i++)
                    {
                        var friend = new Friend();
                        friend.Read(reader);
                        Friends.Add(friend);
                    }

                    ServerId = reader.ReadULong();
                }

                public void Write(GamePacketWriter writer)
                {
                    byte[] body = Payload.Bytes(WriteBody);
                    writer.Write((uint)body.Length);
                    writer.WriteBytes(body);
                }

                private void WriteBody(GamePacketWriter writer)
                {
                    writer.Write(Name);
                    writer.Write(Unused18);
                    writer.Write(Unused1C);
                    writer.Write(Unused20);
                    writer.Write(ModelId);
                    writer.Write(Gender);
                    writer.Write(Race);
                    writer.Write(ProfileId);
                    writer.Write(Unused34);
                    writer.Write(Unused58);

                    writer.Write((uint)Unused38.Count);
                    Unused38.ForEach(x => writer.Write(x));

                    Model.Write(writer);

                    writer.Write((uint)CharacterAttachments.Count);
                    CharacterAttachments.ForEach(x => x.Write(writer));

                    writer.Write((uint)Friends.Count);
                    Friends.ForEach(x => x.Write(writer));

                    writer.Write(ServerId);
                }

                /// <summary>The payload's length in bytes (what Write puts before it).</summary>
                public uint GetSize() => (uint)Payload.Measure(WriteBody);
            }

            public ulong CharacterId { get; set; }
            public ulong LastServerId { get; set; }
            public double LastLogin { get; set; }
            public uint Status { get; set; }
            public CharacterPayload CharacterData { get; set; } = new();

            public void Write(GamePacketWriter writer)
            {
                writer.Write(CharacterId);
                writer.Write(LastServerId);
                writer.Write(LastLogin);
                writer.Write(Status);
                CharacterData.Write(writer);
            }

            public void Read(GamePacketReader reader)
            {
                CharacterId = reader.ReadULong();
                LastServerId = reader.ReadULong();
                LastLogin = reader.ReadDouble();
                Status = reader.ReadUInt();
                CharacterData.Read(reader);
            }
        }
        
        public uint Status { get; set; }
        public bool CanBypassServerLock { get; set; }
        public List<Character> Characters { get; set; } = new();

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Status);
            writer.Write(CanBypassServerLock);

            writer.Write((uint)Characters.Count);
            Characters.ForEach(x => x.Write(writer));
        }

        public void Read(GamePacketReader reader)
        {
            Status = reader.ReadUInt();
            CanBypassServerLock = reader.ReadBool();

            var charCount = reader.ReadUInt();
            for (int i = 0; i < charCount; i++)
            {
                var character = new Character();
                character.Read(reader);
                Characters.Add(character);
            }
        }
    }

    [AuthMessage(AuthMessageOpcode.CharacterSelectInfoReply, ProtocolVersion.LoginUdp_9)]
    public class CharacterSelectInfoReply9 : IWritable
    {
        public class Character : IWritable
        {
            public class CharacterPayload : IWritable, ISize
            {
                /// <summary>As the protocol 10 record without the last bool [I: no LoginUdp_9 reader in our decompiles].</summary>
                public class CharacterAttachment : IWritable, ISize
                {
                    public string ModelName { get; set; } = "";
                    /// <summary>+0x60.</summary>
                    public string TextureAlias { get; set; } = "";
                    /// <summary>+0xc0 (the item's TINT_ALIAS, ItemAppearances).</summary>
                    public string TintAlias { get; set; } = "";
                    /// <summary>+0x120.</summary>
                    public string DecalAlias { get; set; } = "";
                    /// <summary>+0x180.</summary>
                    public uint TintId { get; set; }
                    /// <summary>+0x184.</summary>
                    public uint CompositeEffectId { get; set; }
                    /// <summary>+0x188: the equipment slot (the per-slot key).</summary>
                    public AttachmentSlot Slot { get; set; }
                    /// <summary>+0x18c: 0 = nothing; else added to the character's id set (FUN_142e90e10).</summary>
                    public uint Value18C { get; set; }

                    public uint GetSize()
                    {
                        return (uint)Payload.Measure(Write);
                    }

                    public void Write(GamePacketWriter writer)
                    {
                        writer.Write(ModelName);
                        writer.Write(TextureAlias);
                        writer.Write(TintAlias);
                        writer.Write(DecalAlias);
                        writer.Write(TintId);
                        writer.Write(CompositeEffectId);
                        writer.Write((uint)Slot);
                        writer.Write(Value18C);
                    }
                }

                // Named after the protocol 10 payload, which this one is with one u32 fewer (no +0x34) and the customisations
                // and skin tint in place of the model block [I: no LoginUdp_9 reader in our decompiles].
                public string Name { get; set; } = "Kirmmin";
                public byte Unused18 { get; set; }
                public uint Unused1C { get; set; }
                public uint Unused20 { get; set; }
                public uint ModelId { get; set; }
                public Gender Gender { get; set; }
                public uint Race { get; set; }
                public uint ProfileId { get; set; }
                public ulong Unused58 { get; set; }
                public List<uint> Unused38 { get; set; } = new();
                public List<(uint, uint, uint)> Customizations { get; set; } = new();
                public uint SkinTint { get; set; }
                public List<CharacterAttachment> CharacterAttachments { get; set; } = new();
                /// <summary>The friends list, the same record as protocol 10 [I: no LoginUdp_9 reader in our decompiles].</summary>
                public List<CharacterSelectInfoReply.Character.CharacterPayload.Friend> Friends { get; set; } = new();
                /// <summary>As protocol 10's ServerId [I].</summary>
                public ulong ServerId { get; set; }

                public void Write(GamePacketWriter writer)
                {
                    byte[] body = Payload.Bytes(WriteBody);
                    writer.Write((uint)body.Length);
                    writer.WriteBytes(body);
                }

                private void WriteBody(GamePacketWriter writer)
                {
                    writer.Write(Name);
                    writer.Write(Unused18);
                    writer.Write(Unused1C);
                    writer.Write(Unused20);
                    writer.Write(ModelId);
                    writer.Write((uint)Gender);
                    writer.Write(Race);
                    writer.Write(ProfileId);
                    writer.Write(Unused58);

                    writer.Write((uint)Unused38.Count);
                    Unused38.ForEach(x => writer.Write(x));

                    writer.Write((uint)Customizations.Count);
                    foreach (var v in Customizations)
                    {
                        writer.Write(v.Item1);
                        writer.Write(v.Item2);
                        writer.Write(v.Item3);
                    }

                    writer.Write(SkinTint);

                    writer.Write((uint)CharacterAttachments.Count);
                    CharacterAttachments.ForEach(x => x.Write(writer));

                    writer.Write((uint)Friends.Count);
                    Friends.ForEach(x => x.Write(writer));

                    writer.Write(ServerId);
                }

                /// <summary>The payload's length in bytes (what Write puts before it).</summary>
                public uint GetSize() => (uint)Payload.Measure(WriteBody);
            }

            public ulong CharacterId { get; set; }
            public ulong LastServerId { get; set; }
            public double LastLogin { get; set; }
            public uint Status { get; set; }
            public CharacterPayload CharacterData { get; set; } = new();

            public void Write(GamePacketWriter writer)
            {
                writer.Write(CharacterId);
                writer.Write(LastServerId);
                writer.Write(LastLogin);
                writer.Write(Status);
                CharacterData.Write(writer);
            }
        }

        public uint Status { get; set; }
        public bool CanBypassServerLock { get; set; }
        public List<Character> Characters { get; set; } = new();

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Status);
            writer.Write(CanBypassServerLock);

            writer.Write((uint)Characters.Count);
            Characters.ForEach(x => x.Write(writer));
        }
    }
}
