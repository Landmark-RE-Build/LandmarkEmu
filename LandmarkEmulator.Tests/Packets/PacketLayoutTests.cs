using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Combat;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// Exact byte layouts of packet bodies against the [P] layouts in the code comments and docs/protocol.md (the client's
    /// readers). Each expected layout is built field by field from the reader's description, not from the writer.
    /// </summary>
    public class PacketLayoutTests
    {
        [Fact]
        public void RemovePlayer_Version0ThenGuid()
            => Assert.Equal(new Layout().I16(0).U64(0x1122334455667788).ToArray(),
                Wire.Write(new PlayerUpdateRemovePlayer { Guid = 0x1122334455667788 }));

        /// <summary>docs/protocol.md 4.1: reply entries {u64 guid, u32 type, 4 × u32, u32, f32, f32 range, u8}.</summary>
        [Fact]
        public void InteractionInfoReply_Entries()
        {
            var reply = new InteractionInfoReply();
            reply.Entries.Add(new InteractionInfoReply.Entry { Guid = 7, Type = InteractionInfoReply.Interactable, CursorInReach = 10,
                CursorOutOfReach = 11, Range = 5f, Outline = true });
            Assert.Equal(new Layout().U32(1).U64(7).U32(3).U32(10).U32(11).U32(10).U32(11).U32(0).F32(-1f).F32(5f).Bool(true).ToArray(),
                Wire.Write(reply));
        }

        [Fact]
        public void DynamicInteractionFlagging_BoolThenGuidList()
        {
            var flagging = new DynamicInteractionFlagging();
            flagging.Guids.AddRange(new ulong[] { 1, 2 });
            Assert.Equal(new Layout().Bool(true).U32(2).U64(1).U64(2).ToArray(), Wire.Write(flagging));
        }

        /// <summary>docs/protocol.md 4.4: u64 guid, list of {u32 stat, f32 value}.</summary>
        [Fact]
        public void UpdateCalculatedStats_GuidThenStatList()
        {
            var stats = new ClientUpdateUpdateCalculatedStats { Guid = 9 };
            stats.Stats.Add((13, 2.5f));
            Assert.Equal(new Layout().U64(9).U32(1).U32(13).F32(2.5f).ToArray(), Wire.Write(stats));
        }

        /// <summary>docs/protocol.md 3: Synchronization is 6 × u64 + bool.</summary>
        [Fact]
        public void Synchronization_Is49Bytes()
        {
            var sync = new Synchronization { ClientSendTime = 1, Time2 = 2, ClientTime = 3, ServerTime = 4, ServerTime2 = 5, Time3 = 6, IsReply = true };
            Assert.Equal(new Layout().U64(1).U64(2).U64(3).U64(4).U64(5).U64(6).Bool(true).ToArray(), Wire.Write(sync));
        }

        /// <summary>docs/protocol.md 3: GameTimeSync {8-byte timestamp, f32 cycle speed, bool}.</summary>
        [Fact]
        public void GameTimeSync_Is13Bytes()
            => Assert.Equal(new Layout().F64(1234.5).F32(2f).Bool(false).ToArray(),
                Wire.Write(new GameTimeSync { Time = 1234.5, CycleSpeed = 2f }));

        [Fact]
        public void ActionBarSlot_BarSlotTypeValue()
            => Assert.Equal(new Layout().U32(1).U32(2).U32(3).U64(4).ToArray(),
                Wire.Write(new ActionManagerUpdateSlot { BarId = 1, SlotId = 2, Type = 3, Value = 4 }));

        [Fact]
        public void CharacterSelectSessionResponse_BoolThenString()
            => Assert.Equal(new Layout().Bool(true).Str("abc").ToArray(),
                Wire.Write(new CharacterSelectSessionResponse { Success = true, SessionId = "abc" }));

        /// <summary>TerrainCell is a u32-sub family: the enum carries 2 sub bytes, the body the other 2 (docs 1.3).</summary>
        [Fact]
        public void TerrainCellUpToDateCells_FinishesTheSubThenCellList()
        {
            var cells = new TerrainCellUpToDateCells();
            cells.CellIds.Add(0xAABB);
            Assert.Equal(new Layout().U16(0).U32(1).U64(0xAABB).ToArray(), Wire.Write(cells));
        }

        /// <summary>
        /// PlayerUpdate 0x14 SetDisposition: reader FUN_142c4f960 → FUN_142c2b5a0: u64 guid, u32 disposition, nothing else
        /// (18 bytes on the wire with the 6-byte header).
        /// </summary>
        [Fact]
        public void SetDisposition_GuidThenValue()
        {
            byte[] body = Wire.Write(new PlayerUpdateSetDisposition { Guid = 5, Disposition = NpcDisposition.Neutral });
            Assert.Equal(new Layout().U64(5).U32(1).ToArray(), body);
            Assert.Equal(18, body.Length + LandmarkEmulator.WorldServer.Network.WorldSession
                .GetOpcodeBytes(LandmarkEmulator.WorldServer.Network.Message.ZoneMessageOpcode.PlayerUpdateSetDispositionWire).Length);
        }

        /// <summary>PlayerUpdate 0x35 MemberStatus: u64 guid, bool member, consumed exactly (FUN_142c25ad0).</summary>
        [Fact]
        public void MemberStatus_GuidThenBool()
            => Assert.Equal(new Layout().U64(0x10).Bool(true).ToArray(), Membership.MemberStatus(0x10, true));

        /// <summary>PlayerUpdate 0x51 UpdateCharacterStateDelta: u64 guid, u64 source 0, u64 set, u64 clear, u32 time = 36 bytes.</summary>
        [Fact]
        public void CharacterStateDelta_Is36Bytes()
        {
            byte[] body = Private.Call<byte[]>(typeof(BlockStance), "DeltaBody", 0x99ul, BlockStance.BlockingBit, 0ul, 1234u);
            Assert.Equal(new Layout().U64(0x99).U64(0).U64(1ul << 45).U64(0).U32(1234).ToArray(), body);
        }

        /// <summary>
        /// PlayerUpdate 0x4C SafeEject as the 0.695 client sends it: header FUN_142c13350 (u16 0x26, u32 0x4C), then one byte
        /// (packet +0x18 = 1, FUN_142ebdda0 / FUN_142e85070); the runtime body of 2026-10-18 was <c>01</c>.
        /// </summary>
        [Fact]
        public void SafeEject_HeaderThenOneByte()
        {
            byte[] packet = Wire.Hex("26 00 4C 00 00 00 01");
            (LandmarkEmulator.WorldServer.Network.Message.ZoneMessageOpcode? opcode, int length)
                = LandmarkEmulator.WorldServer.Network.WorldSession.GetOpcode(packet);
            Assert.Equal(LandmarkEmulator.WorldServer.Network.Message.ZoneMessageOpcode.PlayerUpdateSafeEjectWire, opcode);
            Assert.Equal(6, length);
            var (msg, left) = Wire.Read<PlayerUpdateSafeEjectRequest>(packet[length..]);
            Assert.Equal((byte)1, msg.Flag);
            Assert.Equal(0u, left);
        }

        /// <summary>The launch's end: UpdateCharacterStateDelta for the player, set 0, clear bit 26, time 0 (applies at once).</summary>
        [Fact]
        public void SafeEjectClear_DeltaClearsBit26()
            => Assert.Equal(new Layout().U64(0x99).U64(0).U64(0).U64(1ul << 26).U32(0).ToArray(), SafeEject.ClearBody(0x99));

        /// <summary>A launch ends only on the ground: after being seen in the air, or after the grace on the pad.</summary>
        [Fact]
        public void SafeEject_EndsOnlyOnTheGround()
        {
            Assert.False(SafeEject.ShouldEnd(airborne: true, sawAirborne: true, TimeSpan.FromSeconds(10)));
            Assert.False(SafeEject.ShouldEnd(airborne: false, sawAirborne: false, TimeSpan.FromSeconds(0.2)));
            Assert.True(SafeEject.ShouldEnd(airborne: false, sawAirborne: true, TimeSpan.FromSeconds(0.2)));
            Assert.True(SafeEject.ShouldEnd(airborne: false, sawAirborne: false, SafeEject.GroundedGrace));
        }

        /// <summary>
        /// docs/protocol.md 4.1 PlayCompositeEffect 0x0B: u8 target type 1 TargetCharacterGuid {vec4 offset, u64 guid},
        /// u64 instance, u32 effect, u32 delay ms, u32 duration ms, bool, bool, bool.
        /// </summary>
        [Fact]
        public void PlayCompositeEffect_OnACharacter()
        {
            byte[] body = Private.Call<byte[]>(typeof(HitEffects), "Build", 0x77ul, 508u, 1500u);
            Assert.Equal(new Layout().U8(1).Vec4(0, 0, 0, 0).U64(0x77).U64(0).U32(508).U32(0).U32(1500).Bool(true).Bool(true).Bool(false).ToArray(), body);
        }

        /// <summary>
        /// Inventory 0x12 ItemTooltipReply, 0.695 reader FUN_142f17070 → item record FUN_142f17a30 [P]: u8 sub, u8, bool,
        /// bool, then the record: 5 × u32 (+0x00..+0x10), four u32 lists (+0x18 / +0x30 / +0x48 / +0x60), the blob (+0x180),
        /// 3 × u32 (+0xf4..+0xfc), u64 guid +0x100, u32 +0x114, u32 +0x118, the stat list (+0xa8), u64 +0x108, u32 +0x110,
        /// **u64 +0x120 FraudTimer** (→ ItemTooltipInfo +0x178, FUN_143763a20; Lua "FraudTimer", FUN_143768120), u8 +0x128,
        /// str +0x130, … The FraudTimer is Unix seconds (the tooltip's os_difftime(FraudTimer, os_time()), L184).
        /// </summary>
        [Fact]
        public void ItemTooltipReply_FraudTimerIsTheU64AfterTheStats()
        {
            var definition = new LandmarkEmulator.WorldServer.Game.Inventory.ItemDefinitionManager.ItemDefinition
            {
                Id = 987654, NameId = 11, DescriptionId = 12, ImageSetId = 13, RawData = new byte[] { 0xAA, 0xBB }
            };
            const ulong fraud = 1_800_000_000ul;
            byte[] body = InventoryTooltipReply.Build(false, true, definition, 0x1122334455667788ul, fraud);
            Layout Head(bool byDefinition) => new Layout().U8(0x12).U8(0).Bool(byDefinition).Bool(true)
                .U32(11).U32(12).U32(13).U32(0).U32(987654)
                .U32(0).U32(0).U32(0).U32(0)                          // categories, 2nd list, 3rd list, keywords
                .U32(3).U8(1).Raw(0xAA, 0xBB)                          // blob: "has definition" + definition
                .U32(0).U32(0).U32(0).U64(byDefinition ? 0ul : 0x1122334455667788ul).U32(0).U32(0)
                .U32(0)                                                 // stats
                .U64(0).U32(0);                                         // +0x108, +0x110
            byte[] Tail(Layout head, ulong timer) => head
                .U64(timer)                                             // +0x120 FraudTimer
                .U8(0).Str("").U32(0).U32(0).U32(0).U8(0).Bool(false).Str("").Str("").U32(0)
                .U32(0)                                                 // related items
                .U32(0).ToArray();                                      // tooltip flags
            Assert.Equal(Tail(Head(false), fraud), body);
            // A definition tooltip has no instance, so never a timer.
            Assert.Equal(Tail(Head(true), 0ul), InventoryTooltipReply.Build(true, true, definition, 0, fraud));
        }

        /// <summary>
        /// 187's caster info carries the friend teleport cooldown in u32 / f32 +0x70 (body offset 21, 0.695 reader
        /// FUN_142e016b0 → record +0x14c, FUN_142e187a0), which Ui.TeleportToFriend checks itself (L182); 0 when off. The
        /// rest of the body is the captured one: +0xa0 = 187 (the timer key, offset 79), bool +0xa4 = 1 (start the timer on
        /// CastSuccess, offset 83). 185 keeps the capture's 60 s.
        /// </summary>
        [Fact]
        public void FriendTeleportCasterInfo_CarriesTheCooldown()
        {
            using (new Support.FakeWorld(c => c.FriendTeleportCooldownSeconds = 1800))
            {
                byte[] body = LandmarkEmulator.WorldServer.Network.Handlers.MiscHandler.CapturedCasterInfo(187);
                Assert.Equal(187u, BitConverter.ToUInt32(body, 0));
                Assert.Equal(1800f, BitConverter.ToSingle(body, LandmarkEmulator.WorldServer.Network.Handlers.MiscHandler.CasterInfoCooldownAt));
                Assert.Equal(178, body.Length);
                Assert.Equal(187u, BitConverter.ToUInt32(body, 79));
                Assert.Equal((byte)1, body[83]);
                // Spire / My Site follow their own setting (L188), not the friend one: 0 here.
                Assert.Equal(0f, BitConverter.ToSingle(LandmarkEmulator.WorldServer.Network.Handlers.MiscHandler.CapturedCasterInfo(185), 21));
            }
            using (new Support.FakeWorld())
            {
                Assert.Equal(0f, BitConverter.ToSingle(LandmarkEmulator.WorldServer.Network.Handlers.MiscHandler.CapturedCasterInfo(187), 21));
                // No Spire / My Site cooldown by default (live 2016; the old 60 s was the Cozy Campfire capture's).
                Assert.Equal(0f, BitConverter.ToSingle(LandmarkEmulator.WorldServer.Network.Handlers.MiscHandler.CapturedCasterInfo(185), 21));
                Assert.Equal(0f, BitConverter.ToSingle(LandmarkEmulator.WorldServer.Network.Handlers.MiscHandler.CapturedCasterInfo(186), 21));
            }
            using (new Support.FakeWorld(c => c.TravelCooldownSeconds = 600))
            {
                Assert.Equal(600f, BitConverter.ToSingle(LandmarkEmulator.WorldServer.Network.Handlers.MiscHandler.CapturedCasterInfo(185), 21));
                Assert.Equal(600f, BitConverter.ToSingle(LandmarkEmulator.WorldServer.Network.Handlers.MiscHandler.CapturedCasterInfo(186), 21));
                Assert.Equal(0f, BitConverter.ToSingle(LandmarkEmulator.WorldServer.Network.Handlers.MiscHandler.CapturedCasterInfo(187), 21));
            }
        }

        /// <summary>
        /// Moving platforms (L186): 1000 ms updates on a leg whose two stops have the same turn (q or −q), 500 ms on a
        /// turning leg (the client extrapolates position with no limit, rotation only from angular velocity, which is 0).
        /// </summary>
        [Fact]
        public void PlatformSendInterval_StraightLegsAreSentHalfAsOften()
        {
            Quaternion a = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
            Assert.Equal(TimeSpan.FromMilliseconds(1000), Props.PlatformSendIntervalBetween(a, a));
            Assert.Equal(TimeSpan.FromMilliseconds(1000), Props.PlatformSendIntervalBetween(a, Quaternion.Negate(a)));
            Assert.Equal(TimeSpan.FromMilliseconds(1000), Props.PlatformSendIntervalBetween(Quaternion.Identity, Quaternion.Identity));
            Assert.Equal(TimeSpan.FromMilliseconds(500), Props.PlatformSendIntervalBetween(a, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.72f)));
            Assert.Equal(TimeSpan.FromMilliseconds(500), Props.PlatformSendIntervalBetween(Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2)));
        }

        /// <summary>
        /// L179: each mover's queued bodies go out oldest first by the u32 time after the type byte, whatever order they
        /// were queued in; movers keep their first-queued order; a u32 wrap inside a tick still sorts right.
        /// </summary>
        [Fact]
        public void MovementFanout_InTimeOrder_SortsEachMoversBodiesByTime()
        {
            static byte[] Body(byte type, uint time) => new Layout().U8(type).U32(time).U8(1).ToArray();
            var pending = new Dictionary<(uint, byte), byte[]>
            {
                [(7, 2)] = Body(2, 1030),          // a position-only body queued first…
                [(9, 0)] = Body(0, 500),
                [(7, 0)] = Body(0, 1000),          // …then the older full one of the same mover
                [(8, 2)] = Body(2, 5),             // after the wrap
                [(8, 0)] = Body(0, uint.MaxValue - 10),
            };
            var order = LandmarkEmulator.WorldServer.Network.MovementFanout.InTimeOrder(pending, m => m)
                .Select(i => (i.Key.Item1, i.Key.Item2)).ToArray();
            Assert.Equal(new (uint, byte)[] { (7, 0), (7, 2), (9, 0), (8, 0), (8, 2) }, order);
            Assert.Equal(1030u, LandmarkEmulator.WorldServer.Network.MovementFanout.TimeOf(Body(2, 1030)));
            Assert.Equal(0u, LandmarkEmulator.WorldServer.Network.MovementFanout.TimeOf(new byte[] { 2, 1 }));
        }

        /// <summary>FraudTimer = Unix seconds of TradeableAfter while it is ahead; 0 when tradeable (no "Tradable in" line).</summary>
        [Fact]
        public void ItemTooltipReply_FraudTimerOf()
        {
            var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
            DateTime after = now.AddHours(12);
            Assert.Equal((ulong)new DateTimeOffset(after).ToUnixTimeSeconds(), InventoryTooltipReply.FraudTimerOf(after, now));
            Assert.Equal(1_792_368_000ul, InventoryTooltipReply.FraudTimerOf(after, now));
            Assert.Equal(0ul, InventoryTooltipReply.FraudTimerOf(now.AddSeconds(-1), now));
            Assert.Equal(0ul, InventoryTooltipReply.FraudTimerOf(now, now));
            Assert.Equal(0ul, InventoryTooltipReply.FraudTimerOf(null, now));
        }

        /// <summary>
        /// Abilities EffectStart (0x27/0x0C), 0.695 reader FUN_142e028b0 [P]: the header (u16 sub, u8, u32), then u64 key
        /// +0x20, u32 +0x28, u32 id +0x30, u32 +0x34, u32 type +0x38, 4 × u32, bool, u32, 4 × u32, u32, u64 +0x70, u64 +0x78,
        /// u32, 4 × u32, 4 × f32 +0xa0, u32, u64, u32, 4 × f32 +0xf0, f32 arc +0x110, f32 +0x114, f32 +0x118, u32, u32,
        /// 4 × f32 +0x100, 6 × u32, f32 duration +0xdc, u32 start +0xe0, bool, u32.
        /// </summary>
        [Fact]
        public void EffectStart_FollowsTheReader()
        {
            uint id = Buffs.EffectId("block-stance");
            byte[] body = Buffs.CharacterEffectStart(0xABCDul, "block-stance", BlockStance.BlockEffectType, 157f, 4f, 2.5f, 9000u, 0.25f);
            var expected = new Layout().U16(0x0C).U8(0).U32(0)
                .U64(0xABCD).U32(0).U32(id).U32(0).U32(BlockStance.BlockEffectType)
                .U32(0).U32(0).U32(0).U32(0).Bool(false).U32(0)
                .U32(0).U32(0).U32(0).U32(0).U32(0)
                .U64(0xABCD).U64(0xABCD).U32(0)
                .U32(0).U32(0).U32(0).U32(0)
                .Vec4(0, 0, 0, 1)
                .U32(0).U64(0).U32(0)
                .Vec4(0, 0, 0, 0)
                .F32(157f).F32(4f).F32(0.25f).U32(0).U32(0)
                .Vec4(0, 0, 0, 0)
                .U32(0).U32(0).U32(0).U32(0).U32(0).U32(0)
                .F32(2.5f).U32(9000).Bool(false).U32(0);
            Assert.Equal(expected.ToArray(), body);
        }

        /// <summary>EffectEnd (0x27/0x0D), reader FUN_142e02440: u64, u32, u32, u32, u64, u64 target, u32, f32, u32 type, u32, u32.</summary>
        [Fact]
        public void EffectEnd_FollowsTheReader()
        {
            uint id = Buffs.EffectId("x");
            Assert.Equal(new Layout().U16(0x0D).U8(0).U32(0).U64(5).U32(0).U32(id).U32(0).U64(0).U64(5).U32(0).F32(0f).U32(6).U32(0).U32(0).ToArray(),
                Buffs.CharacterEffectEnd(5, "x"));
        }

        /// <summary>CommandPacketSetChatBubbleColor: u16 sub, u32 fg, u32 bg, u32 size, u64 guid.</summary>
        [Fact]
        public void ChatBubbleColor_Layout()
        {
            byte[] body = Private.Call<byte[]>(typeof(ChatBubbleColor), "Build", 0x31ul, 0xFF0000u, 0x00FF00u, 2u);
            ushort sub = Convert.ToUInt16(Private.Field(typeof(ChatBubbleColor), "Sub"));
            Assert.Equal(new Layout().U16(sub).U32(0xFF0000).U32(0x00FF00).U32(2).U64(0x31).ToArray(), body);
        }

        /// <summary>
        /// docs/protocol.md 4.3: Position 0x7F is the 2-bit transient id, the movement body, then 4 floats (scale; the
        /// client defaults them to 1).
        /// </summary>
        [Fact]
        public void PlayerUpdatePosition_IdBodyAndFourFloats()
        {
            byte[] movement = { 0, 1, 2, 3 };
            Assert.Equal(new Layout().Packed(300).Raw(movement).F32(1f).F32(1f).F32(1f).F32(1f).ToArray(),
                Wire.Write(new PlayerUpdatePosition { TransientId = 300, Movement = movement }));
        }

        /// <summary>The Sweep targeter element: u32 id, type, origin, follow, f32 delay (+0x14) before u32 max targets (+0x10)… [P].</summary>
        [Fact]
        public void ClientExecutedTargeter_DelayBeforeMaxTargets()
        {
            var t = new ClientExecutedTargeter { Id = 1, Type = ClientExecutedTargeter.Sweep, Origin = 1, Follow = 2, Delay = 0.2f, MaxTargets = 3,
                Duration = 0.5f, StartAngle = -60f, EndAngle = 60f, RehitSeconds = 1.5f, Radius = 5f };
            Assert.Equal(new Layout().U32(1).U32(ClientExecutedTargeter.Sweep).U32(1).U32(2).F32(0.2f).U32(3).F32(0.5f).F32(-60f).F32(60f)
                .F32(1.5f).F32(5f).U32(0).ToArray(), Wire.Write(w => t.Write(w)));
        }

        /// <summary>ItemUpdate: u64, u32 count, u32, u32, u64, u32 slot, u32 container, 2 × (5 × u32 + 2 empty lists), u32, f32 wear last.</summary>
        [Fact]
        public void ItemUpdate_EndsWithTheWear()
        {
            byte[] body = Wire.Write(new ClientUpdateItemUpdate { Guid = 3, Count = 4, Slot = 5, Container = 6, Wear = 7.5f });
            var expected = new Layout().U64(3).U32(4).U32(0).U32(0).U64(0).U32(5).U32(6);
            for (int i = 0; i < 2; i++)
                expected.U32(0).U32(0).U32(0).U32(0).U32(0).U32(0).U32(0);
            expected.U32(0).F32(7.5f);
            Assert.Equal(expected.ToArray(), body);
        }

        // ---- Client → server readers: a [P] body is read whole -----------------------------------------------------------

        /// <summary>FindTarget 0x27/0x24: u64 character, u64 cast guid, u32 cast hash, u32 targeter id, 16 zero bytes.</summary>
        [Fact]
        public void FindTarget_ReadsTheClientsBody()
        {
            byte[] body = new Layout().U64(0x11).U64(0x22).U32(0x33).U32(1).Raw(new byte[16]).ToArray();
            var (msg, left) = Wire.Read<AbilityClientTargeterFindTarget>(body);
            Assert.Equal((0x11ul, 0x22ul, 0x33u, 1u), (msg.Target, msg.CastGuid, msg.CastHash, msg.TargeterId));
            Assert.Equal(16, msg.Tail.Length);
            Assert.Equal(0u, left);
        }

        [Fact]
        public void TargeterEnd_ReadsTheClientsBody()
        {
            var (msg, left) = Wire.Read<AbilityClientTargeterEnd>(new Layout().U64(0x22).U32(0x33).U32(1).ToArray());
            Assert.Equal((0x22ul, 0x33u, 1u), (msg.CastGuid, msg.CastHash, msg.TargeterId));
            Assert.Equal(0u, left);
        }

        /// <summary>HeroicMovement 0x105: {u8 sub, u64 entity}, then the sub's body.</summary>
        [Fact]
        public void HeroicMovement_HeaderThenBody()
        {
            var (msg, left) = Wire.Read<HeroicMovementRequest>(new Layout().U8(3).U64(0x44).U8(0x40).F32(0.5f).ToArray());
            Assert.Equal((byte)3, msg.SubOpcode);
            Assert.Equal(0x44ul, msg.Entity);
            Assert.Equal(5, msg.Body.Length);
            Assert.Equal(0u, left);
        }

        /// <summary>PacketClientInGamePurchase 0xD0: u32, u32, u32 [P] (writer FUN_1435ebe10).</summary>
        [Fact]
        public void ClientInGamePurchase_ThreeU32()
        {
            var (msg, left) = Wire.Read<ClientInGamePurchase>(new Layout().U32(1).U32(2).U32(0).ToArray());
            Assert.Equal((1u, 2u, 0u), (msg.First, msg.Second, msg.Third));
            Assert.Equal(0u, left);
        }

        public static IEnumerable<object[]> SubWidths => new[]
        {
            // docs/protocol.md 1.3: the sub-opcode width of each family's client packets.
            new object[] { typeof(ClaimsRequest), 1 }, new object[] { typeof(PropRequest), 1 }, new object[] { typeof(TemplateRequest), 1 },
            new object[] { typeof(ConstructionRequest), 1 }, new object[] { typeof(LootPinataRequest), 1 }, new object[] { typeof(GroupRequest), 1 },
            new object[] { typeof(UiRequest), 1 }, new object[] { typeof(FriendRequest), 1 },
            new object[] { typeof(InventoryRequest), 2 }, new object[] { typeof(GuildRequest), 2 }, new object[] { typeof(ChatRequest), 2 },
            new object[] { typeof(RecipeRequest), 4 }, new object[] { typeof(MailRequest), 4 }, new object[] { typeof(ChestRequest), 4 },
            new object[] { typeof(InstancedGameRequest), 4 }, new object[] { typeof(WarpNetworkRequest), 4 },
            new object[] { typeof(TeleportRequest), 1 }, new object[] { typeof(CommandRequest), 2 },
        };

        /// <summary>A family's request model reads the sub with the family's width and keeps the rest as the body.</summary>
        [Theory]
        [MemberData(nameof(SubWidths))]
        public void FamilyRequests_ReadTheirSubWidth(Type model, int width)
        {
            byte[] packet = { 0x05, 0x00, 0x00, 0x00, 0xAA, 0xBB };
            var request = (LandmarkEmulator.Shared.Network.Message.IReadable)Activator.CreateInstance(model);
            var reader = new LandmarkEmulator.Shared.Network.GamePacketReader(packet);
            request.Read(reader);
            Assert.Equal(5u, Convert.ToUInt32(model.GetProperty("SubOpcode")!.GetValue(request)));
            Assert.Equal(packet.Length - width, ((byte[])model.GetProperty("Body")!.GetValue(request)).Length);
        }
    }
}
