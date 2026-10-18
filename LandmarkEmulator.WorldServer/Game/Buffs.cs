using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Timed effects on players, shared by consumables and gear: stat buffs (added to the calculated stats the client
    /// shows and uses), healing over time, per-ability cooldowns and on/off toggles (gadgets). The server tick expires
    /// them; each change re-sends the calculated stats once (one small packet), and a buff wearing off tells the player.
    /// Effects live with the session (a relog clears them).
    /// </summary>
    public static class Buffs
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>One stat change: <see cref="Add"/> to the value, then <see cref="Percent"/> of it.</summary>
        private sealed record Buff(string Name, uint Stat, float Add, float Percent, DateTime Ends, uint Item, bool Announce = true);

        private sealed class State
        {
            public readonly Dictionary<string, Buff> Buffs = new();
            public readonly Dictionary<uint, DateTime> Cooldowns = new();
            public readonly HashSet<string> Toggles = new();
            public (float PerSecond, DateTime Ends, uint Item)? Healing;
        }

        private static readonly Dictionary<WorldSession, State> states = new();
        private static readonly object sync = new();

        private static State Of(WorldSession session)
        {
            if (!states.TryGetValue(session, out State state))
                states[session] = state = new State();
            return state;
        }

        /// <summary>
        /// Starts (or refreshes) a named stat buff: <paramref name="add"/> to stat <paramref name="stat"/> plus
        /// <paramref name="percent"/> % of it, for <paramref name="duration"/>. <paramref name="announce"/> off: no "has worn
        /// off" chat line at the end (short, often repeated buffs such as Cloud Burst's; the icon still goes).
        /// </summary>
        public static void AddStat(WorldSession session, string name, uint stat, float add, float percent, TimeSpan duration,
            uint item = 0, bool announce = true)
        {
            lock (sync)
                Of(session).Buffs[name] = new Buff(name, stat, add, percent, DateTime.UtcNow + duration, item, announce);
            session.SendCalculatedStats();
            ShowIcon(session, name, item, duration);
            log.Info($"{session.CharacterName}: {name} for {duration.TotalSeconds:0.#} s (stat {stat} +{add} +{percent}%).");
        }

        /// <summary>
        /// The client asks for its effects after every zone load (AbilityEffectUpdatesRequest): the active buffs and the
        /// healing over time are shown again with the time they have left.
        /// </summary>
        public static void Resend(WorldSession session)
        {
            Pets.ResendIcon(session);   // the permanent pet icon (spec-pets.md §5.5); kept outside the buff states
            List<(string Name, uint Item, TimeSpan Left)> active = new();
            lock (sync)
            {
                if (!states.TryGetValue(session, out State state))
                    return;
                DateTime now = DateTime.UtcNow;
                foreach (Buff b in state.Buffs.Values.Where(b => b.Ends > now))
                    active.Add((b.Name, b.Item, b.Ends - now));
                if (state.Healing is { } h && h.Ends > now)
                    active.Add((HealingName, h.Item, h.Ends - now));
            }
            foreach (var (name, item, left) in active)
                ShowIcon(session, name, item, left);
            Consumables.ResendGadgets(session);
        }

        /// <summary>Is the named stat buff running on the player?</summary>
        public static bool Has(WorldSession session, string name)
        {
            lock (sync)
                return states.TryGetValue(session, out State state) && state.Buffs.TryGetValue(name, out Buff b)
                    && b.Ends > DateTime.UtcNow;
        }

        /// <summary>A calculated stat after the player's buffs.</summary>
        public static float Apply(WorldSession session, uint stat, float value)
        {
            lock (sync)
            {
                if (!states.TryGetValue(session, out State state))
                    return value;
                foreach (Buff buff in state.Buffs.Values)
                    if (buff.Stat == stat)
                        value = (value + buff.Add) * (1f + buff.Percent / 100f);
            }
            return value;
        }

        /// <summary>Heals <paramref name="percentPerSecond"/> % of max health every second for the duration.</summary>
        public static void HealOverTime(WorldSession session, float percentPerSecond, TimeSpan duration, uint item = 0)
        {
            lock (sync)
                Of(session).Healing = (percentPerSecond, DateTime.UtcNow + duration, item);
            if (item != 0)
                ShowIcon(session, HealingName, item, duration);
        }

        private const string HealingName = "Healing";

        /// <summary>
        /// Buff icons (BuffsWidget, data source Character.AbilityEffects, built by FUN_142d87fe0 from the player's
        /// effect list at ClientPcData +0x75638). An effect is added by Abilities 0x27 sub 0x1C EffectInitialized
        /// (reader FUN_142d64620, handler FUN_142d7b4f0 then FUN_142f2b140) and removed by sub 0x0D EffectEnd (handler
        /// FUN_142d7aca0, the target's virtual +0x248, FUN_142f32a50), both keyed by {u64, u32, u32 effect id}; the
        /// effect id is also the icon's GUID, which the bar's cancel button sends back (sub 0x1E RequestCancelEffect,
        /// FUN_142d83940). Header (FUN_142d65900): u16 sub (again after the opcode), u8, u32.
        /// EffectInitialized body: u64 key, u32 key, u32 effect id, u32, u32, f32 duration (s; the icon's countdown),
        /// u64 caster, u32 name text, u32 description text, u32 image set, u32 good type (2 = always a buff), u32,
        /// u32 stack count, u32. EffectEnd body: u64 key, u32 key, u32 effect id, u32, u64 (when it is the player and
        /// the type below isn't 6 the client prints harvest results), u64 target (whose effect list), u32, f32, u32
        /// type (6 = plain end), then unless the type is 1 two u32. Icon, name and description are those of the item that started the buff.
        /// </summary>
        private static void ShowIcon(WorldSession session, string name, uint item, TimeSpan duration)
        {
            var definition = item == 0 ? null : Inventory.ItemDefinitionManager.Get(item);
            if (definition == null)
                return;
            ShowIcon(session, name, definition.NameId, definition.DescriptionId, definition.ImageSetId, duration, 1);
        }

        /// <summary>
        /// A combat state on the buff bar (Combat.Statuses: Frenzy, Flow …): the property's own name text, the affinity's
        /// buff icon, its time left and stack count.
        /// </summary>
        public static void ShowStatus(WorldSession session, string key, uint nameText, uint imageSet, TimeSpan duration, uint stacks)
            => ShowIcon(session, key, nameText, 0, imageSet, duration, stacks);

        public static void HideStatus(WorldSession session, string key) => HideIcon(session, key);

        private static void ShowIcon(WorldSession session, string name, uint nameId, uint descriptionId, uint imageSet,
            TimeSpan duration, uint stacks)
        {
            HideIcon(session, name);
            SendEffectStart(session, name);
            var output = new List<byte>();
            var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
            WriteHeader(w, 0x1C);
            w.Write(session.PlayerGuid);
            w.Write(0u);
            w.Write(EffectId(name));
            w.Write(0u);
            w.Write(0u);
            w.Write((float)duration.TotalSeconds);
            w.Write(session.PlayerGuid);
            w.Write(nameId);
            w.Write(descriptionId);
            w.Write(imageSet);
            w.Write(2u);
            w.Write(0u);
            w.Write(stacks);
            w.Write(0u);
            session.EnqueueMessage(Network.Message.ZoneMessageOpcode.AbilityEffectInitialized, output.ToArray());
        }

        /// <summary>
        /// EffectStart (sub 0x0C, reader FUN_142d64890, handler FUN_142d7b690): puts the effect on the player's
        /// character (virtual +0x240), which EffectEnd needs to find before it removes anything (FUN_142d7aca0 looks it
        /// up with +0x230 and only then calls +0x248, which also drops the buff icon). No animation (+0xb0 = 0), no
        /// ability (+0x48 = 0), identity rotation. Body: u64 key, u32 key, u32 effect id, 6 × u32, bool, u32,
        /// 4 × u32, u32, u64, u64 target, u32, 4 × u32, 4 × f32 rotation, u32 anim type, u64, u32, 4 × f32,
        /// 5 × u32, 4 × f32, 8 × u32, bool, u32. Sent before EffectInitialized: it clears a buff entry with the same key.
        /// </summary>
        private static void SendEffectStart(WorldSession session, string name, uint type = 0, float strength = 0f,
            float fade = 0f, float seconds = 0f, uint startMs = 0)
            => session.EnqueueMessage(Network.Message.ZoneMessageOpcode.AbilityEffectStart,
                CharacterEffectStart(session.PlayerGuid, name, type, 0f, strength, seconds, startMs, fade));

        /// <summary>
        /// The EffectStart body (after the opcode) for an effect on any character <paramref name="carrier"/> (+0x70 caster
        /// and +0x78 carrier; the key's u64 is the carrier too, so each character's effect has its own key), for the
        /// recipient's client to put on that character. Layout as <see cref="SendEffectStart"/>; 0.695 reader FUN_142e028b0
        /// (re/tools/pkt.py: the three 4 × u32 blocks are fixed-size, FUN_142c02830), handler FUN_142e19660 copies +0x38 →
        /// effect type +0x20, +0x110 → +0xa0, +0x114 → +0xa4, +0x118 → +0xa8, +0xdc → duration, +0xe0 → start [P].
        /// <paramref name="arc"/> (+0x110) and <paramref name="strength"/> (+0x114) are the type's two parameters: a Block
        /// effect's full arc in degrees and height in metres (FUN_14354dc10), DetectCave's strength (+0x114).
        /// </summary>
        public static byte[] CharacterEffectStart(ulong carrier, string name, uint type, float arc, float strength,
            float seconds, uint startMs, float fade = 0f)
        {
            var output = new List<byte>();
            var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
            WriteHeader(w, 0x0C);
            w.Write(carrier);
            w.Write(0u);
            w.Write(EffectId(name));
            w.Write(0u);                               // +0x34
            w.Write(type);                             // +0x38 effect type (name hash, e.g. DetectCave, Block)
            for (int i = 0; i < 4; i++) w.Write(0u);   // +0x3c..+0x48
            w.Write(false);                            // +0x4c
            w.Write(0u);                               // +0x50
            for (int i = 0; i < 4; i++) w.Write(0u);   // +0x60
            w.Write(0u);                               // +0x54
            w.Write(carrier);                          // +0x70
            w.Write(carrier);                          // +0x78 the character carrying it
            w.Write(0u);                               // +0x80
            for (int i = 0; i < 4; i++) w.Write(0u);   // second 4-value block
            w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f);   // +0xa0 rotation
            w.Write(0u);                               // +0xb0 animation type: none
            w.Write(0ul);                              // +0xb8
            w.Write(0u);                               // +0xc0
            for (int i = 0; i < 4; i++) w.Write(0f);   // +0xf0
            w.Write(arc);                              // +0x110 → effect +0xa0 (Block: full arc, degrees)
            w.Write(strength);                         // +0x114 → effect +0xa4 (Block: height, m; DetectCave: strength)
            w.Write(fade);                             // +0x118 → effect +0xa8
            w.Write(0u);                               // +0x11c
            w.Write(0u);                               // +0x120
            for (int i = 0; i < 4; i++) w.Write(0f);   // +0x100
            for (int i = 0; i < 6; i++) w.Write(0u);   // +0xd4, +0xc4, +0xd8, +0xc8, +0xcc, +0xd0
            w.Write(seconds);                          // +0xdc duration (s) → effect +0x68
            w.Write(startMs);                          // +0xe0 start (server clock ms) → effect +0x40
            w.Write(false);                            // +0xe4
            w.Write(0u);                               // +0xe8
            return output.ToArray();
        }

        /// <summary>
        /// A client-side ability effect with no buff icon: EffectStart with the effect's type (the hash of its type name,
        /// FUN_142de0ef0's table) and its parameters. The local character's effect-added hook (FUN_1432777f0) acts on
        /// some types itself — DetectCave (0x6eb699a5) hands {id, strength +0xa4, duration +0x68, fade +0xa8, end =
        /// start + duration × 1000} to CaveDetectionManager (FUN_14307de40: strength must be &gt; 0, the fade is capped
        /// at half the duration), which fades the voxel renderer's cave view (VoxelClientImpl) in and out. Ended with
        /// <see cref="EndClientEffect"/> (EffectEnd → FUN_14307df30 drops it by id).
        /// </summary>
        public static void StartClientEffect(WorldSession session, string name, uint type, float strength, float fade,
            TimeSpan duration)
        {
            SendEffectStart(session, name, type, strength, fade, (float)duration.TotalSeconds, (uint)ServerClock.Milliseconds);
        }

        public static void EndClientEffect(WorldSession session, string name) => HideIcon(session, name);

        private static void HideIcon(WorldSession session, string name)
            => session.EnqueueMessage(Network.Message.ZoneMessageOpcode.AbilityEffectEnd, CharacterEffectEnd(session.PlayerGuid, name));

        /// <summary>
        /// The EffectEnd body (after the opcode) taking effect <paramref name="name"/> off character
        /// <paramref name="carrier"/> (key {carrier, 0, id} as <see cref="CharacterEffectStart"/>; target +0x40 = carrier).
        /// 0.695 reader FUN_142e02440: u64, u32, u32, u32, u64 (+0x38), u64 target (+0x40), u32, f32, u32 type, u32, u32 [P].
        /// </summary>
        public static byte[] CharacterEffectEnd(ulong carrier, string name)
        {
            var output = new List<byte>();
            var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
            WriteHeader(w, 0x0D);
            w.Write(carrier);
            w.Write(0u);
            w.Write(EffectId(name));
            w.Write(0u);
            w.Write(0ul);
            w.Write(carrier);
            w.Write(0u);
            w.Write(0f);
            w.Write(6u);
            w.Write(0u);      // type != 1: two more u32 (+0x54 resource, +0x58)
            w.Write(0u);
            return output.ToArray();
        }

        private static void WriteHeader(LandmarkEmulator.Shared.Network.GamePacketWriter w, ushort sub)
        {
            w.Write(sub);
            w.Write((byte)0);
            w.Write(0u);
        }

        /// <summary>A buff's effect id: a stable positive number from its name (the icon's GUID on the client).</summary>
        public static uint EffectId(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
                hash = (hash ^ c) * 16777619;
            return (hash & 0x3FFFFFFF) | 0x10000;
        }

        /// <summary>
        /// The buff bar's cancel (Ui.CancelEffect: Abilities 0x1E RequestCancelEffect, the effect id last): ends that
        /// buff early. The pet icon dismisses the pet (<see cref="Pets.Cancel"/>).
        /// </summary>
        public static void Cancel(WorldSession session, uint effectId)
        {
            if (Pets.Cancel(session, effectId))
                return;
            string ended = null;
            lock (sync)
            {
                if (!states.TryGetValue(session, out State state))
                    return;
                if (state.Buffs.Values.FirstOrDefault(b => EffectId(b.Name) == effectId) is { } buff)
                {
                    state.Buffs.Remove(buff.Name);
                    ended = buff.Name;
                }
                else if (state.Healing != null && EffectId(HealingName) == effectId)
                {
                    state.Healing = null;
                    ended = HealingName;
                }
            }
            if (ended == null)
            {
                log.Debug($"{session.CharacterName} cancelled unknown effect {effectId}.");
                return;
            }
            HideIcon(session, ended);
            session.SendCalculatedStats();
            log.Info($"{session.CharacterName} cancelled {ended}.");
        }

        /// <summary>Is a heal over time running on the player?</summary>
        public static bool IsHealing(WorldSession session)
        {
            lock (sync)
                return states.TryGetValue(session, out State state) && state.Healing is { } h && h.Ends > DateTime.UtcNow;
        }

        /// <summary>Is an ability still cooling down? Starts the cooldown when it isn't (and returns false).</summary>
        public static bool OnCooldown(WorldSession session, uint ability, TimeSpan cooldown)
        {
            lock (sync)
            {
                State state = Of(session);
                if (state.Cooldowns.TryGetValue(ability, out DateTime ready) && ready > DateTime.UtcNow)
                    return true;
                state.Cooldowns[ability] = DateTime.UtcNow + cooldown;
                return false;
            }
        }

        /// <summary>Flips an on/off effect (a gadget); true when it is now on.</summary>
        public static bool Toggle(WorldSession session, string name)
        {
            lock (sync)
            {
                State state = Of(session);
                if (state.Toggles.Remove(name))
                    return false;
                state.Toggles.Add(name);
                return true;
            }
        }

        public static bool IsOn(WorldSession session, string name)
        {
            lock (sync)
                return states.TryGetValue(session, out State state) && state.Toggles.Contains(name);
        }

        /// <summary>Players with an effect switched on.</summary>
        public static List<WorldSession> WithToggle(string name)
        {
            lock (sync)
                return states.Where(s => s.Value.Toggles.Contains(name)).Select(s => s.Key).ToList();
        }

        private static DateTime nextTick = DateTime.UtcNow;

        /// <summary>Server tick (once a second): healing over time, expiring buffs, gadget effects.</summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextTick)
                return;
            nextTick = now.AddSeconds(1);
            var expired = new List<(WorldSession, Buff)>();
            var healing = new List<(WorldSession, float)>();
            var endedHeals = new List<WorldSession>();
            lock (sync)
            {
                foreach ((WorldSession session, State state) in states.ToList())
                {
                    if (session.Disconnected)
                    {
                        states.Remove(session);
                        continue;
                    }
                    foreach (Buff buff in state.Buffs.Values.Where(b => b.Ends <= now).ToList())
                    {
                        state.Buffs.Remove(buff.Name);
                        expired.Add((session, buff));
                    }
                    if (state.Healing is { } heal)
                    {
                        if (heal.Ends <= now)
                        {
                            state.Healing = null;
                            if (heal.Item != 0)
                                endedHeals.Add(session);
                        }
                        else
                            healing.Add((session, heal.PerSecond));
                    }
                }
            }
            foreach ((WorldSession session, float percent) in healing)
                Health.Heal(session, (uint)Math.Ceiling(Health.Max(session) * percent / 100f));
            foreach (WorldSession session in expired.Select(e => e.Item1).Distinct())
                session.SendCalculatedStats();
            foreach ((WorldSession session, Buff buff) in expired)
            {
                HideIcon(session, buff.Name);
                if (buff.Announce)
                    Chat.System(session, $"{buff.Name} has worn off.");
            }
            foreach (WorldSession session in endedHeals)
                HideIcon(session, HealingName);
            Consumables.Update();
        }
    }
}
