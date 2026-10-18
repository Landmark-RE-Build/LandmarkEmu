using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Character stats (the client's <c>List&lt;CharacterStat&gt;</c>, a different id space from the calculated stats):
    /// the movement tuning live sent every player (OPEN-LEADS L162).
    /// <para>
    /// <b>What they are [P].</b> The client reads them by name through Resources/StringHashToValue.txt ("StatId.&lt;name&gt;^id^",
    /// looked up by FUN_142ea2e20, then the character's stat getter vtable +0x558): 3 BackpedalSpeedModifier,
    /// 4 CrouchSpeedModifier, 5 SprintSpeedModifier, 7 StrafeSpeedModifier, 21 SprintAccelerationTime,
    /// 22 SprintDecelerationTime, 23 WaistTwistLimit, 24 ForwardAccelerationTime, 25 BackAccelerationTime,
    /// 26 StrafeAccelerationTime, 27 ForwardDecelerationTime, 28 BackDecelerationTime, 29 StrafeDecelerationTime,
    /// 38 JumpHeight … So the ids CharacterStatDefinitions flags SEND_TO_REMOTE_CLIENT (21, 22, 24-29) are the eight
    /// movement acceleration / deceleration times (the earlier guess from CalculatedStatDefinitions, glide / fly /
    /// double jump, was wrong).
    /// </para>
    /// <para>
    /// <b>Who reads them.</b> The local player's movement (FUN_143329ed0: the speed ramps; backpedal FUN_1433260e0, strafe
    /// FUN_143326d20) and, for every character, the animation: ProxiedCharacter vtable +0x588 = FUN_142eaf290 copies the
    /// eight times into the actor's animation variables (hashes 0xbbac9e67 …), so start / stop blends follow them. A remote
    /// character gets it after PlayerUpdate UpdateStat 0x53 (FUN_142cdfa60 case 0x52: each stat vtable +0x580, then +0x588)
    /// and after LightweightToFullPc (the nested UpdateStat). Without stats the getter returns its default, 0 for the
    /// times (FUN_142ea3520: absent → default), so other players' start / stop blends were instant [P read; I how it looks].
    /// </para>
    /// <para>
    /// <b>Values [P capture].</b> The live login capture's ClientUpdate UpdateStat (once in MiscHandler, commented out): 37
    /// stats, among them the eight times 0.2 / 0.15 s, backpedal 0.65, strafe 0.75, crouch 0.25, sprint 1.0, waist twist
    /// 48. The SendSelf capture has the same 47 ids all 0 (the real values came in that UpdateStat). They are per-game
    /// constants as far as the one capture shows [I], so other players get them once with the character
    /// (LightweightToFullPc, no extra packet) and never again. MaxHp (1, 1800 in the capture) is left out: our health is
    /// the resources' (Combat/GearStats) [I].
    /// </para>
    /// Server setting LiveCharacterStats (Docker LIVE_CHARACTER_STATS, default true = live); dev <c>charstats on|off</c>.
    /// </summary>
    public static class CharacterStats
    {
        /// <summary>One CharacterStat (reader FUN_142c18c40): u32 id, i8 kind (0 int, 1 float), value, base [P].</summary>
        public readonly record struct Stat(uint Id, float Value);

        private static bool? enabled;

        /// <summary>The server setting LiveCharacterStats unless the dev command switched it.</summary>
        public static bool Enabled
        {
            get => enabled ?? LandmarkEmulator.Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.LiveCharacterStats ?? true;
            set => enabled = value;
        }

        /// <summary>The live capture's values (MaxHp left out; the buffs are 0 there and stay unsent).</summary>
        private static readonly Stat[] live =
        {
            new(3, 0.65f), new(4, 0.25f), new(5, 1f), new(6, 1f), new(7, 0.75f), new(8, 0.1f), new(9, 0.2f), new(10, 0.1f),
            new(11, 3000f), new(12, 2f), new(13, 250f), new(14, 1f),
            new(21, 0.2f), new(22, 0.15f), new(23, 48f), new(24, 0.2f), new(25, 0.2f), new(26, 0.2f), new(27, 0.15f),
            new(28, 0.15f), new(29, 0.15f),
            new(34, 1f), new(37, 1f), new(38, 0f), new(39, 0f), new(40, 1f), new(42, 0f), new(43, 0f), new(44, 0f), new(45, 0f),
            new(46, 0f), new(47, 0f),
        };

        /// <summary>Stats the capture sent as integers (kind 0): BreathPenaltyDuration 11, BreathRechargeDelay 13 (ms).</summary>
        private static readonly HashSet<uint> integers = new() { 11, 13 };

        private static HashSet<uint> remote;

        /// <summary>
        /// The ids CharacterStatDefinitions.txt flags SEND_TO_REMOTE_CLIENT (5th column; 21, 22, 24-29 in 0.695) [P]; the
        /// client's own reader of the flag (FUN_143c67450) is never called, so this is the server's rule.
        /// </summary>
        public static HashSet<uint> RemoteIds
        {
            get
            {
                if (remote != null)
                    return remote;
                var ids = new HashSet<uint>();
                foreach (string[] r in GameDataFiles.Rows("CharacterStatDefinitions.txt"))
                    if (r.Length > 4 && r[4].Trim() == "1" && uint.TryParse(r[0], out uint id))
                        ids.Add(id);
                if (ids.Count == 0)
                    ids.UnionWith(new uint[] { 21, 22, 24, 25, 26, 27, 28, 29 });
                return remote = ids;
            }
        }

        /// <summary>The player's own stats (ClientUpdate UpdateStat at login); empty when off.</summary>
        public static List<Stat> Own() => Enabled ? live.ToList() : new List<Stat>();

        /// <summary>The stats other players' clients get with this character (the nested PlayerUpdate UpdateStat).</summary>
        public static List<Stat> ForOthers() => Enabled ? live.Where(s => RemoteIds.Contains(s.Id)).ToList() : new List<Stat>();

        /// <summary><c>u32 count</c>, then per stat <c>u32 id, i8 kind, u32 value, u32 base 0</c> (FUN_142c3cef0 / FUN_142c18c40) [P].</summary>
        public static void WriteList(GamePacketWriter writer, IReadOnlyCollection<Stat> stats)
        {
            writer.Write((uint)stats.Count);
            foreach (Stat s in stats)
            {
                writer.Write(s.Id);
                if (integers.Contains(s.Id))
                {
                    writer.Write((byte)0);
                    writer.Write((uint)s.Value);
                    writer.Write(0u);
                }
                else
                {
                    writer.Write((byte)1);
                    writer.Write(s.Value);
                    writer.Write(0f);
                }
            }
        }

        /// <summary>Sends the player's own stats (after the client is ready, with the calculated stats).</summary>
        public static void SendOwn(WorldSession session)
        {
            if (!Enabled)
                return;
            var message = new Network.Message.Model.ClientUpdateUpdateStat();
            message.Stats.AddRange(Own());
            session.EnqueueMessage(message);
        }
    }
}
