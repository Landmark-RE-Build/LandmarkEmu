using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Emotes while holding a tool or weapon: the held item is put away for the emote and brought back afterwards
    /// (setting <c>EmotesPutAwayHeldItem</c> / Docker <c>EMOTES_PUT_AWAY_HELD_ITEM</c>, <b>off by default = live</b>: after
    /// tracing it (trace/0695/emotes-client.md) the user chose live, 2026-10-21 — the client plays the tool's put-away clip
    /// first, and handing the tool back plays the take-out clip, which ends an emote still running).
    /// <para>
    /// Why the emote doesn't show otherwise [P, 0.695 client]: a held item's actor (every <c>Wield_*.adr</c> with a
    /// model in a hand, e.g. Wield_Pick_* {WeaponIdle 1, Class 1001, Weapon 1}, Wield_Shield_* {WeaponIdle 1, Class 1016,
    /// Weapon 4}; buckets and quivers carry none) sets the holder's WeaponIdle to 1, and in PlayerAnimationNetwork64.mrn
    /// (Assets_153.pack) the switch <c>StandPoses|Idle_Stand_2</c> (node 10129, control WeaponIdle) then picks child 1
    /// <c>Weapon1</c> — 220 nodes of weapon / tool idles (Pick, Axe, Sickle, Sword, Gun, ...) and no emote. Every emote
    /// clip (Emotes_SingleShot, the looping Emotes, the moods' Emotion_Switch) and every stance (Stances|StanceSwitch) is
    /// under child 0 <c>Stand</c>. So with anything in hand the selection arrives and nothing plays (docs/protocol.md
    /// "No sit or stance while a tool or weapon is held"). The client itself never puts the item away for an emote, so
    /// live presumably behaved the same [I].
    /// </para>
    /// <para>
    /// What this does: before an emote word, /emoteclip n, /playdead or the client's own /sit, the main-hand and off-hand
    /// items with a model go to the bag (as Tab does for build mode, <see cref="BuildModeHolster"/>), and the emote is
    /// played <see cref="PutAwayLead"/> later (taking the item off — the hotbar's own TransferItem, the same move — made
    /// /sit show again in game, 2026-10-18; the client's AttachmentProcessor.log shows the model removed within the same
    /// second, 2026-10-21). They come back:
    /// <list type="bullet">
    /// <item>when the player walks <see cref="MoveAway"/> from where they emoted (an emote or stance is over by then:
    /// walking ends a stance, <see cref="Emotes.Moved"/>);</item>
    /// <item>for the walk stances (skip, drunk, sneak, kept while walking) only at /stand;</item>
    /// <item>at /stand or /stop (the end of /talking) in any case.</item>
    /// </list>
    /// Moods are not affected: a mood is a lasting idle (Emote_Emotion) that shows whenever the hands are empty. An item
    /// comes back only if it is still in the bag and its hand is empty (the player may have taken something else from
    /// the hotbar meanwhile), and not while the player is in build mode (Tab keeps hands empty; the next move after it
    /// brings the item back).
    /// </para>
    /// </summary>
    public static class EmoteHolster
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>EquipmentSlotDefinitions: the main hand and the off hand (0.695).</summary>
        private const uint MainHand = 2, OffHand = 11;
        private static readonly uint[] hands = { MainHand, OffHand };

        /// <summary>
        /// How long after putting the item away the emote is played. Measured 2026-10-21 on the 0.695 client
        /// (re/test/emote_trace.js on the player's game, dev command emotelead, screenshots): a selection the client gets
        /// up to ~1.1 s after its EquipmentUnset is taken (FUN_142dfc010 returns true) but never shows — not even a
        /// stance, which is a lasting variable — and a second one inside that time is refused and dropped from the
        /// client's retry queue (FUN_142e977b0, ~70 ms of retries); cheer after 0.8 / 0.9 / 1.0 / 1.1 s: not shown,
        /// after 1.2 s (twice) / 1.6 / 2.0 s: shown. 1.5 s leaves room for the two packets arriving closer together than
        /// sent. What the client does in that second (OPEN-LEADS L201, trace/0695/phaseE2-combat.md) [P network, I
        /// Morpheme semantics]: the state machine <c>StandPoses</c> (node 10128) leaves <c>Idle_Stand_2</c> for
        /// <c>WeaponSwitchCombo</c> (the put-away clip) on the WeaponEnd request (message 75) with Class &lt; 0.5,
        /// WeaponIdle &lt; 0.5 and the Weapon1 idle active (transition 15157, a 0.6 s blend), and comes back as soon as
        /// WeaponIdle &lt; 0.5 holds there (15153, 0.5 s): 1.1 s of transitions in which no emote state is active, so an
        /// emote request is consumed and lost. Both durations are seconds of animation time, not frames, so the window is
        /// the same on every PC (± a frame). Nothing in it reaches the server, so the lead stays a fixed time.
        /// </summary>
        internal static TimeSpan PutAwayLead = TimeSpan.FromMilliseconds(1500);

        /// <summary>How far from where they emoted a player walks before the item comes back, metres (as a stance ends).</summary>
        internal const float MoveAway = 0.5f;

        private sealed class PutAway
        {
            public Vector3 At;
            public bool UntilStand;
            public readonly List<(uint Hand, ulong Item)> Items = new();
        }

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, PutAway> putAway = new();

        private static bool Enabled
            => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.EmotesPutAwayHeldItem == true;

        /// <summary>
        /// Puts the player's held items away for an emote. <paramref name="untilStand"/>: true for a walk stance (the
        /// items come back only at /stand), false for another stance, null for an emote that leaves the stance as it is
        /// (a wave during /skip keeps the items away until /stand). Returns true when something was put away: the
        /// caller plays the emote <see cref="PutAwayLead"/> later (<see cref="Then"/>).
        /// </summary>
        public static bool PutAwayFor(WorldSession session, bool? untilStand = null)
        {
            if (!Enabled || BuildModeHolster.InBuildMode(session))
                return false;
            Dictionary<uint, CharacterItemModel> visible = session.VisibleEquipment();
            var moved = new List<(uint Hand, ulong Item)>();
            foreach (uint hand in hands)
            {
                if (!visible.TryGetValue(hand, out CharacterItemModel item))
                    continue;
                Network.Handlers.InventoryHandler.Unequip(session, hand);
                // A full bag refuses the move: the item stays in hand.
                if (session.Inventory.Get(item.Id)?.Container == Inventory.Inventory.BagContainer)
                    moved.Add((hand, item.Id));
            }
            lock (sync)
            {
                putAway.TryGetValue(session, out PutAway record);
                if (moved.Count == 0 && record == null)
                    return false;
                if (record == null)
                    putAway[session] = record = new PutAway();
                // A new emote restarts the walk-away distance; a walk stance keeps the items away until /stand.
                record.At = session.Position;
                record.UntilStand = untilStand ?? record.UntilStand;
                // The player took something into that hand again since the last emote (hotbar): only the newest one
                // comes back (2026-10-21: two came back, and the second replaced the first).
                record.Items.RemoveAll(e => moved.Exists(m => m.Hand == e.Hand));
                record.Items.AddRange(moved);
            }
            if (moved.Count == 0)
                return false;
            log.Debug($"{session.CharacterName}: put away {moved.Count} held item(s) for an emote.");
            return true;
        }

        /// <summary>Runs an emote now, or <see cref="PutAwayLead"/> later when items were just put away for it.</summary>
        public static void Then(WorldSession session, bool delayed, Action play)
        {
            if (!delayed)
            {
                play();
                return;
            }
            // On the world tick (Abilities' timers), not a pool thread: the emote's goal progress can give items and
            // save achievements, which the tick also touches (trace/0695/phaseE4-logic.md F5).
            Abilities.Later(PutAwayLead, () =>
            {
                if (session.Disconnected)
                    return;
                try
                {
                    play();
                }
                catch (Exception e)
                {
                    log.Error(e, $"{session.CharacterName}: delayed emote failed.");
                }
            });
        }

        /// <summary>Movement from the client: the items come back once the player walks away (not in a walk stance).</summary>
        public static void Moved(WorldSession session, Vector3 position)
        {
            // Every movement packet of every player comes here: no lock while nobody has anything put away.
            if (putAway.Count == 0)
                return;
            PutAway record;
            lock (sync)
            {
                if (!putAway.TryGetValue(session, out record) || record.UntilStand || Vector3.Distance(record.At, position) <= MoveAway)
                    return;
                if (BuildModeHolster.InBuildMode(session))
                    return;
                putAway.Remove(session);
            }
            BringBack(session, record);
        }

        /// <summary>/stand or /stop: the items come back.</summary>
        public static void Stood(WorldSession session)
        {
            PutAway record;
            lock (sync)
                if (BuildModeHolster.InBuildMode(session) || !putAway.Remove(session, out record))
                    return;
            BringBack(session, record);
        }

        private static void BringBack(WorldSession session, PutAway record)
        {
            if (session.Disconnected)
                return;
            Dictionary<uint, CharacterItemModel> visible = session.VisibleEquipment();
            foreach ((uint hand, ulong guid) in record.Items)
            {
                CharacterItemModel item = session.Inventory.Get(guid);
                if (item == null || item.Container != Inventory.Inventory.BagContainer || visible.ContainsKey(hand))
                    continue;
                Network.Handlers.InventoryHandler.Equip(session, guid, hand);
            }
            log.Debug($"{session.CharacterName}: emote over, holding {record.Items.Count} item(s) again.");
        }

        /// <summary>Is anything of the player's put away for an emote? (tests)</summary>
        internal static bool HasPutAway(WorldSession session)
        {
            lock (sync)
                return putAway.ContainsKey(session);
        }

        /// <summary>Left the world: the items stay in the bag (as for build mode).</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                putAway.Remove(session);
        }
    }
}
