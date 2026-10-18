using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.GatewayServer;
using LandmarkEmulator.Internal;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// A game client that dies without logging out (crash, killed process, Alt+F4, power or network loss; user report
    /// 2026-10-18: the character stood in the world for ~3 minutes): its connection is dropped after
    /// CLIENT_TIMEOUT_SECONDS of silence and the character leaves the world; a new login of the same character drops the
    /// old session at once; the separate gateway tells the zone; a zone hold doesn't count as client silence.
    /// </summary>
    public class ClientTimeoutTests
    {
        /// <summary>A clock the test moves by hand (seconds).</summary>
        private sealed class FakeClock
        {
            public double Now;
            public void Advance(double seconds) => Now += seconds;
        }

        private sealed class ProbeSession : NetworkSession
        {
            public int Disconnects;
            public ProbeSession(SocketHeartbeat heartbeat) => Heartbeat = heartbeat;
            public override void OnDisconnect()
            {
                if (Disconnected)
                    return;
                Disconnects++;
                base.OnDisconnect();
            }
        }

        [Fact]
        public void Heartbeat_FlatlinesAfterTheTimeout_AndAnyPacketResetsIt()
        {
            var clock = new FakeClock();
            var beat = new SocketHeartbeat(() => clock.Now, 60);
            clock.Advance(59);
            Assert.False(beat.Flatline);
            beat.OnHeartbeat();
            clock.Advance(59);
            Assert.False(beat.Flatline, "a packet restarts the count");
            clock.Advance(1);
            Assert.True(beat.Flatline);
            Assert.Equal(60d, beat.SilentSeconds);
        }

        [Fact]
        public void Heartbeat_TimeoutBelowTheClientKeepAlive_IsRaised_AndZeroIsNever()
        {
            // The 0.695 client's SOE keep-alive comes every 29 s (keepAliveDelay, FUN_143a0a980): less would drop healthy
            // idle clients.
            Assert.Equal(30d, SocketHeartbeat.Sanitise(5));
            Assert.Equal(60d, SocketHeartbeat.Sanitise(60));
            Assert.Equal(0d, SocketHeartbeat.Sanitise(0));
            Assert.Equal(0d, SocketHeartbeat.Sanitise(-3));
            var clock = new FakeClock();
            var never = new SocketHeartbeat(() => clock.Now, 0);
            clock.Advance(100000);
            Assert.False(never.Flatline);
            Assert.Equal(60d, SocketHeartbeat.DefaultClientTimeoutSeconds);
            Assert.True(SocketHeartbeat.DefaultClientTimeoutSeconds >= 2 * SocketHeartbeat.ClientKeepAliveSeconds,
                "one lost keep-alive must not drop a client");
        }

        [Fact]
        public void Heartbeat_FollowsTheProcessDefault_WhenItChanges()
        {
            double before = SocketHeartbeat.DefaultTimeoutSeconds;
            try
            {
                var clock = new FakeClock();
                SocketHeartbeat.DefaultTimeoutSeconds = 60;
                var beat = new SocketHeartbeat(() => clock.Now);
                clock.Advance(45);
                Assert.False(beat.Flatline);
                SocketHeartbeat.DefaultTimeoutSeconds = 40;   // the settings page: counts at once
                Assert.True(beat.Flatline);
            }
            finally
            {
                SocketHeartbeat.DefaultTimeoutSeconds = before;
            }
        }

        [Fact]
        public void Heartbeat_IsWallClock_NotTheSumOfSlowTicks()
        {
            // A slow server tick (say 70 s) must not count as client silence if the client sent something meanwhile.
            var clock = new FakeClock();
            var session = new ProbeSession(new SocketHeartbeat(() => clock.Now, 60));
            clock.Advance(50);
            session.Heartbeat.OnHeartbeat();   // receive thread, during the long tick
            clock.Advance(20);
            session.Update(70);
            Assert.False(session.Disconnected);
        }

        [Fact]
        public void SilentSession_IsTimedOutAndDisconnected_Once()
        {
            var clock = new FakeClock();
            var session = new ProbeSession(new SocketHeartbeat(() => clock.Now, 60));
            for (int i = 0; i < 59; i++)
            {
                clock.Advance(1);
                session.Update(1);
            }
            Assert.False(session.Disconnected);
            clock.Advance(1);
            session.Update(1);
            Assert.True(session.Disconnected);
            Assert.True(session.TimedOut);
            session.Update(1);
            Assert.Equal(1, session.Disconnects);
        }

        private sealed class ProbeGameSession : GameSession
        {
            public int Disconnects;
            public ProbeGameSession() : base("F70IaxuU8C/w7FPXY1ibXw==")
            {
            }

            public override void OnDisconnect()
            {
                if (Disconnected)
                    return;
                Disconnects++;
                base.OnDisconnect();
            }
        }

        [Fact]
        public void UnacknowledgedTimeout_GoesThroughTheOwnersDisconnect()
        {
            // Before 2026-10-18 the 120 s unacked rule called NetworkSession.OnDisconnect directly: GatewayClient's
            // override never ran and the zone never heard the connection had closed.
            var session = new ProbeGameSession();
            typeof(GameSession).GetMethod("SendDisconnect", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(session, new object[] { GameSession.DisconnectReasonUnacknowledgedTimeout });
            Assert.True(session.Disconnected);
            Assert.Equal(1, session.Disconnects);
        }

        /// <summary>A social service that does nothing (the local one reads friends from the database, absent in tests).</summary>
        public class NoSocial : DispatchProxy
        {
            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                Type r = targetMethod.ReturnType;
                if (r == typeof(void))
                    return null;
                if (r == typeof(System.Threading.Tasks.Task))
                    return System.Threading.Tasks.Task.CompletedTask;
                return r.IsValueType ? Activator.CreateInstance(r) : null;
            }
        }

        private static IDisposable WithoutSocial()
        {
            PropertyInfo p = typeof(LandmarkEmulator.WorldServer.Game.Social.SocialServices).GetProperty("Service")!;
            object before = p.GetValue(null);
            p.GetSetMethod(true)!.Invoke(null, new object[] { DispatchProxy.Create<LandmarkEmulator.WorldServer.Game.Social.ISocialService, NoSocial>() });
            return new Restore(() => p.GetSetMethod(true)!.Invoke(null, new[] { before }));
        }

        private sealed class Restore : IDisposable
        {
            private readonly Action undo;
            public Restore(Action undo) => this.undo = undo;
            public void Dispose() => undo();
        }

        private static List<WorldSession> Players()
            => (List<WorldSession>)typeof(PlayerManager).GetField("players", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

        [Fact]
        public void NewLogin_DropsTheOlderSessionOfTheCharacter_AtOnce()
        {
            using var world = new FakeWorld();
            using var social = WithoutSocial();
            WorldSession watcher = world.Session(0x7300_0001, "Watcher");
            WorldSession ghost = world.Session(0x7300_0002, "Ghost");
            WorldSession fresh = world.Session(0x7300_0002, "Ghost");
            WorldSession other = world.Session(0x7300_0003, "Bystander");
            Players().Add(watcher);
            Players().Add(ghost);
            Players().Add(other);
            foreach (WorldSession s in new[] { watcher, ghost, fresh, other })
                ZoneSessions.Add(s);
            FakeWorld.Outgoing(watcher);
            FakeWorld.Outgoing(ghost);
            try
            {
                try { PlayerManager.ReplaceOlderSessions(fresh); } catch (Exception e) { Console.WriteLine("DBG " + e); throw; }

                Assert.False(PlayerManager.Snapshot().Contains(ghost), "the dead session left the world");
                Assert.True(PlayerManager.Snapshot().Contains(other), "other characters stay");
                Assert.True(ghost.Replaced);
                Assert.False(fresh.Replaced);
                Assert.True(FakeWorld.Outgoing(watcher).Any(p => p.Opcode == ZoneMessageOpcode.PlayerUpdateRemovePlayerWire),
                    "the others got the despawn");
                Assert.True(FakeWorld.Outgoing(ghost).Any(p => p.Opcode == ZoneMessageOpcode.ClientKickedFromServer),
                    "a still-connected old client is told why");
                // Its client (if it was still alive and loading) can't come back in and kick the new login.
                PlayerManager.Enter(ghost);
                Assert.False(PlayerManager.Snapshot().Contains(ghost));
            }
            finally
            {
                foreach (WorldSession s in new[] { watcher, ghost, fresh, other })
                    Players().Remove(s);
                foreach (WorldSession s in new[] { watcher, ghost, fresh, other })
                    ZoneSessions.Remove(s);
            }
        }

        [Fact]
        public void NewLogin_AlsoDropsAnOlderSessionStillLoading()
        {
            using var world = new FakeWorld();
            using var social = WithoutSocial();
            WorldSession loading = world.Session(0x7300_0010, "Loader");
            WorldSession fresh = world.Session(0x7300_0010, "Loader");
            ZoneSessions.Add(loading);
            ZoneSessions.Add(fresh);
            try
            {
                try { PlayerManager.ReplaceOlderSessions(fresh); } catch (Exception e) { Console.WriteLine("DBG " + e); throw; }
                Assert.True(loading.Replaced);
                Assert.False(fresh.Replaced);
            }
            finally
            {
                ZoneSessions.Remove(loading);
                ZoneSessions.Remove(fresh);
            }
        }

        // ---- the separate gateway ------------------------------------------------------------------------------------

        private static void SetGatewayConfig(GatewayServerConfiguration config)
            => typeof(ConfigurationManager<GatewayServerConfiguration>).GetProperty("Config")!
                .GetSetMethod(true)!.Invoke(ConfigurationManager<GatewayServerConfiguration>.Instance, new object[] { config });

        private static void Set(object target, string name, object value)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
                if (t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    is PropertyInfo p && p.GetSetMethod(true) != null)
                {
                    p.GetSetMethod(true)!.Invoke(target, new[] { value });
                    return;
                }
            throw new MissingMemberException(target.GetType().Name, name);
        }

        private static T WaitFor<T>(BlockingCollection<IInternalMessage> inbox, Func<T, bool> match = null) where T : class
        {
            DateTime until = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < until)
                if (inbox.TryTake(out IInternalMessage m, 200) && m is T t && (match == null || match(t)))
                    return t;
            return null;
        }

        [Fact]
        public void Gateway_SilentClient_TellsTheZone_Timeout_AndAZoneHoldIsNotClientSilence()
        {
            GatewayServerConfiguration previous = ConfigurationManager<GatewayServerConfiguration>.Instance.Config;
            SetGatewayConfig(new GatewayServerConfiguration());
            const uint world = 0x7E57;
            ZoneBackEnds.Start(IPAddress.Loopback, 0, world);
            var inbox = new BlockingCollection<IInternalMessage>();
            InternalLink zone = null;
            GatewayClient alive = null;
            try
            {
                var listener = (InternalListener)typeof(ZoneBackEnds).GetField("listener", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
                var tcp = new TcpClient();
                tcp.Connect(IPAddress.Loopback, listener.Port);
                zone = new InternalLink(tcp);
                zone.Received += (_, m) => inbox.Add(m);
                zone.Start();
                zone.Send(new RegisterBackEnd
                {
                    Kind = BackEndKind.Zone, WorldId = world, ZoneIds = Array.Empty<uint>(), ChannelMask = 0x0F, Name = "test zone"
                });
                DateTime until = DateTime.UtcNow.AddSeconds(10);
                while (ZoneBackEnds.Table.ZoneFor(world, 5) == null && DateTime.UtcNow < until)
                    Thread.Sleep(20);
                Assert.NotNull(ZoneBackEnds.Table.ZoneFor(world, 5));

                // Two clients of the world: one goes silent, the other keeps sending.
                var clock = new FakeClock();
                GatewayClient Login(ulong guid, string name)
                {
                    var client = new GatewayClient();
                    Set(client, "Heartbeat", new SocketHeartbeat(() => clock.Now, 60));
                    Set(client, "Account", new AccountModel { Id = (uint)guid, Username = name });
                    client.OnCharacterLogin(new CharacterModel { Id = guid, Name = name, ZoneId = 5 });
                    return client;
                }
                GatewayClient dead = Login(0x7300_0101, "Crashed");
                alive = Login(0x7300_0102, "Waiting");
                AttachConnection a1 = WaitFor<AttachConnection>(inbox, a => a.ConnectionId == dead.ConnectionId);
                Assert.NotNull(a1);

                // The zone goes away (a zone restart): the gateway holds its clients. The live client keeps sending
                // (its KeepAlive every second, its SOE keep-alive every 29 s), so it is never timed out by the hold.
                zone.Close();
                until = DateTime.UtcNow.AddSeconds(10);
                while (!alive.Held && DateTime.UtcNow < until)
                {
                    alive.Update(0.01);
                    Thread.Sleep(20);
                }
                Assert.True(alive.Held, "held while the zone is away");
                for (int i = 0; i < 100; i++)
                {
                    clock.Advance(1);
                    alive.Heartbeat.OnHeartbeat();
                    alive.Update(1);
                    dead.Update(1);
                }
                Assert.False(alive.Disconnected, "zone-side silence is not client silence");
                Assert.True(dead.Disconnected, "the silent client was dropped");
                Assert.True(dead.TimedOut);
                Assert.False(alive.TimedOut);
            }
            finally
            {
                alive?.OnDisconnect();
                zone?.Close();
                ZoneBackEnds.Stop();
                SetGatewayConfig(previous);
            }
        }

        [Fact]
        public void Gateway_SilentClient_ConnectionClosedSaysTimeout()
        {
            GatewayServerConfiguration previous = ConfigurationManager<GatewayServerConfiguration>.Instance.Config;
            SetGatewayConfig(new GatewayServerConfiguration());
            const uint world = 0x7E58;
            ZoneBackEnds.Start(IPAddress.Loopback, 0, world);
            var inbox = new BlockingCollection<IInternalMessage>();
            InternalLink zone = null;
            try
            {
                var listener = (InternalListener)typeof(ZoneBackEnds).GetField("listener", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
                var tcp = new TcpClient();
                tcp.Connect(IPAddress.Loopback, listener.Port);
                zone = new InternalLink(tcp);
                zone.Received += (_, m) => inbox.Add(m);
                zone.Start();
                zone.Send(new RegisterBackEnd
                {
                    Kind = BackEndKind.Zone, WorldId = world, ZoneIds = Array.Empty<uint>(), ChannelMask = 0x0F, Name = "test zone"
                });
                DateTime until = DateTime.UtcNow.AddSeconds(10);
                while (ZoneBackEnds.Table.ZoneFor(world, 5) == null && DateTime.UtcNow < until)
                    Thread.Sleep(20);

                var clock = new FakeClock();
                var client = new GatewayClient();
                Set(client, "Heartbeat", new SocketHeartbeat(() => clock.Now, 60));
                Set(client, "Account", new AccountModel { Id = 77, Username = "Crashed" });
                client.OnCharacterLogin(new CharacterModel { Id = 0x7300_0201, Name = "Crashed", ZoneId = 5 });
                Assert.NotNull(WaitFor<AttachConnection>(inbox, a => a.ConnectionId == client.ConnectionId));

                clock.Advance(59);
                client.Update(59);
                Assert.False(client.Disconnected);
                clock.Advance(1);
                client.Update(1);
                Assert.True(client.Disconnected);
                ConnectionClosed closed = WaitFor<ConnectionClosed>(inbox, c => c.ConnectionId == client.ConnectionId);
                Assert.NotNull(closed);
                Assert.Equal(CloseReason.Timeout, closed.Reason);
            }
            finally
            {
                zone?.Close();
                ZoneBackEnds.Stop();
                SetGatewayConfig(previous);
            }
        }
    }
}
