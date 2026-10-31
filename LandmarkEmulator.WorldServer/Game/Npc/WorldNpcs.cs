using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Npc
{
    /// <summary>
    /// World (non–Story Tools) NPCs: definitions, spawn, per-player flags, dialogue bubbles, optional achievement quests,
    /// patrol and proximity hail. See docs/npc-scripting.md and docs/playtests/npc-pt*.md.
    /// </summary>
    public static partial class WorldNpcs
    {
        private const int FactionAlly = 2;
        private const uint MoodHappy = 220;   // StoryTools mood ids (Happy)

        private sealed class Instance
        {
            public NpcDef Def;
            public ulong Guid;
            public uint Zone;
            public Vector3 Home;
            public List<Vector3> Route = new();
            public int RouteIndex;
            public DateTime NextDwell = DateTime.UtcNow;
            public bool Walking;
            public ulong TalkingTo;           // player guid while in conversation / question open
            public DateTime TalkUntil;
        }

        private sealed class PendingQuestion
        {
            public string NpcId;
            public ulong Creature;
            public DateTime Until;
            /// <summary>The player the question was put to: only their answer counts.</summary>
            public ulong AskedOf;
            public HashSet<ulong> Answered = new();
        }

        private static readonly Dictionary<string, Instance> byId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<ulong, string> byGuid = new();
        private static readonly Dictionary<ulong, PendingQuestion> questions = new();
        private static readonly object sync = new();
        private static ulong nextQuestion = 1; // never 0 (DisplayPropText question flag is questionId != 0)

        /// <summary>
        /// World-NPC question ids carry this bit, so they never equal a Story Tools question id (StoryTools.NewQuestion
        /// counts from 1 too): every PropPacketDialogResponse is offered here first, and a shared number took a player's
        /// story answer and gave them a quest they were never asked (trace/0695/phaseE4-logic.md F2).
        /// </summary>
        private const ulong QuestionIdBit = 1UL << 62;
        private static DateTime nextTick = DateTime.UtcNow;

        private sealed class PendingSpeak
        {
            public WorldSession Session;
            public string NpcId;
            public DateTime When;
        }

        private static readonly List<PendingSpeak> pendingSpeaks = new();

        /// <summary>True when this creature guid is a world NPC we own.</summary>
        public static bool Owns(ulong guid)
        {
            lock (sync)
                return byGuid.ContainsKey(guid);
        }

        public static void Init()
        {
            LoadDefinitions();
            RespawnAll();
        }

        /// <summary>Despawn and spawn every defined NPC (startup and <c>npc reload</c>).</summary>
        public static void RespawnAll()
        {
            List<ulong> old;
            lock (sync)
            {
                old = byGuid.Keys.ToList();
                byId.Clear();
                byGuid.Clear();
            }
            foreach (ulong g in old)
                Creatures.DespawnStory(g);

            uint homeZone = Terrain.TerrainStore.HomeContinent;
            foreach (NpcDef def in AllDefs())
            {
                uint zone = def.Zone != 0 ? def.Zone : homeZone;
                Vector3 home = def.HomePosition(zone);
                if (Terrain.TerrainHeights.HeightAt(home.X, home.Z, zone) is float ground)
                    home.Y = ground;
                ulong guid = Creatures.SpawnStory(zone, home, def.SpawnerName, 0, def.DisplayName);
                if (guid == 0)
                {
                    log.Warn($"World NPC {def.Id}: spawn failed.");
                    continue;
                }
                Creatures.StoryFaction(guid, FactionAlly);
                Creatures.StoryTactics(guid, Creatures.TacticsNothing);
                if (def.Yaw != 0f)
                    Creatures.StoryFocus(guid, home + new Vector3(MathF.Sin(def.Yaw), 0f, MathF.Cos(def.Yaw)));

                var inst = new Instance
                {
                    Def = def,
                    Guid = guid,
                    Zone = zone,
                    Home = home,
                    Route = def.WaypointPositions(zone).Select(p =>
                    {
                        if (Terrain.TerrainHeights.HeightAt(p.X, p.Z, zone) is float g)
                            p.Y = g;
                        return p;
                    }).ToList()
                };
                if (inst.Route.Count == 0)
                    inst.Route.Add(home);
                lock (sync)
                {
                    byId[def.Id] = inst;
                    byGuid[guid] = def.Id;
                }
                // Flag interactable for everyone already in view (StoryFaction ally + Owns).
                FlagInteractable(guid);
                // Scout's Errand needs Fiery Fungus nearby: random surface seeding is sparse (20% of regions) and
                // often empty around the Spire (claims / graveyards), so plant a small pocket for the playtest.
                if (def.Id.Equals("fungus_scout", StringComparison.OrdinalIgnoreCase))
                {
                    int n = Creatures.SeedNear(zone, home, "fungus", 5, 30f, 70f);
                    log.Info($"World NPC {def.Id}: seeded {n} Fiery Fungus nearby.");
                }
                log.Info($"World NPC {def.Id} ({def.DisplayName}) at {home} zone {zone} guid 0x{guid:X}.");
            }
        }

        private static void FlagInteractable(ulong guid)
        {
            var flagging = new Network.Message.Model.DynamicInteractionFlagging();
            flagging.Guids.Add(guid);
            Vector3? at = Creatures.StoryPosition(guid);
            if (at == null)
                return;
            uint zone = 0;
            lock (sync)
                if (byGuid.TryGetValue(guid, out string id) && byId.TryGetValue(id, out Instance inst))
                    zone = inst.Zone;
            foreach (WorldSession s in PlayerManager.InZone(zone))
                if (Vector3.Distance(s.Position, at.Value) < 200f)
                    s.EnqueueMessage(flagging);
        }

        /// <summary>InteractRequest: returns true when handled as a world NPC.</summary>
        public static bool Click(WorldSession session, ulong guid)
        {
            Instance inst;
            lock (sync)
            {
                if (!byGuid.TryGetValue(guid, out string id) || !byId.TryGetValue(id, out inst))
                    return false;
            }
            log.Info($"{session.CharacterName}: interact world NPC {inst.Def.Id} (0x{guid:X}) name={inst.Def.DisplayName}");
            PauseForTalk(inst, session);
            // Promote so the nameplate / look is full; dialogue itself does not wait on that (see Say).
            if (!Creatures.IsFullFor(session, guid))
                Creatures.SendFull(session, guid);
            ScheduleSpeak(session, inst, 0);
            return true;
        }

        private static void ScheduleSpeak(WorldSession session, Instance inst, int delayMs)
        {
            if (delayMs <= 0)
            {
                SpeakFor(session, inst);
                return;
            }
            lock (sync)
                pendingSpeaks.Add(new PendingSpeak
                {
                    Session = session,
                    NpcId = inst.Def.Id,
                    When = DateTime.UtcNow.AddMilliseconds(delayMs)
                });
        }

        /// <summary>PropPacketDialogResponse for a world-NPC question; returns true when consumed.</summary>
        public static bool DialogResponse(WorldSession session, ulong questionId, bool accepted)
        {
            PendingQuestion q;
            lock (sync)
            {
                if (!questions.TryGetValue(questionId, out q) || q.AskedOf != session.PlayerGuid)
                    return false;
                if (q.Until < DateTime.UtcNow || !q.Answered.Add(session.PlayerGuid))
                    return true;   // ours, but late or already answered
            }
            if (!TryGetDef(q.NpcId, out NpcDef def))
                return true;
            Instance inst;
            lock (sync)
                byId.TryGetValue(q.NpcId, out inst);
            if (inst == null)
                return true;

            SetFlag(session, def.Id, "met", 1);
            // The chain lock again (it was checked when the question was asked): no quest out of order.
            if (accepted && !string.IsNullOrEmpty(def.RequiresNpcTurnedIn) && !HasFlag(session.PlayerGuid, def.RequiresNpcTurnedIn, "turned_in"))
                accepted = false;
            if (accepted)
            {
                SetFlag(session, def.Id, "accepted", 1);
                if (def.QuestAchievementId != 0)
                {
                    Achievements.OfferNpcQuest(session, def.QuestAchievementId, def.QuestObjectiveId);
                    // Talk-only gifts (required ≤ 1): complete on accept.
                    if (def.QuestRequired <= 1)
                    {
                        Achievements.CompleteNpcQuest(session, def.QuestAchievementId);
                        SetFlag(session, def.Id, "turned_in", 1);
                    }
                }
                Say(session, inst, Line(def, "accept") ?? new LineDef
                {
                    Text = "Thank you! Dispatch those explode-y mushrooms for me — three will do. Come back when you're done.",
                    Mode = 5
                });
                Creatures.StoryPose(inst.Guid, MoodHappy, null, play: true);
            }
            else
            {
                Say(session, inst, Line(def, "decline") ?? new LineDef
                {
                    Text = "No harm done. Wave if you change your mind.",
                    Mode = 5
                });
            }
            return true;
        }

        private static void SpeakFor(WorldSession session, Instance inst)
        {
            NpcDef def = inst.Def;
            ulong ch = session.PlayerGuid;

            // Chain lock: another NPC must have turned_in first.
            if (!string.IsNullOrEmpty(def.RequiresNpcTurnedIn) && !HasFlag(ch, def.RequiresNpcTurnedIn, "turned_in"))
            {
                Say(session, inst, Line(def, "locked") ?? new LineDef
                {
                    Text = "I'm waiting on word from the Fungus Scout. Help them first, then we can talk.",
                    Mode = 5
                });
                return;
            }

            if (HasFlag(ch, def.Id, "turned_in"))
            {
                Say(session, inst, Line(def, "done") ?? new LineDef
                {
                    Text = "You've done enough mushroom hunting for one day. Safe travels!",
                    Mode = 5
                });
                return;
            }

            if (def.QuestAchievementId != 0 && HasFlag(ch, def.Id, "accepted"))
            {
                if (Achievements.NpcQuestReady(ch, def.QuestAchievementId, def.QuestObjectiveId))
                {
                    Achievements.CompleteNpcQuest(session, def.QuestAchievementId);
                    SetFlag(session, def.Id, "turned_in", 1);
                    Say(session, inst, Line(def, "turnin") ?? new LineDef
                    {
                        Text = "That's the last of them — well done! Here's a little thanks from the Spire.",
                        Mode = 5
                    });
                    Creatures.StoryPose(inst.Guid, MoodHappy, null, play: true);
                    return;
                }
                Say(session, inst, Line(def, "reminder") ?? new LineDef
                {
                    Text = "Still need three explode-y mushrooms down. The Journal will keep count.",
                    Mode = 5
                });
                return;
            }

            if (HasFlag(ch, def.Id, "met") && !HasFlag(ch, def.Id, "accepted"))
            {
                // Declined earlier: offer again as a question.
                OfferGreeting(session, inst, def, first: false);
                return;
            }

            if (HasFlag(ch, def.Id, "accepted") && def.QuestAchievementId == 0)
            {
                Say(session, inst, Line(def, "idle") ?? new LineDef
                {
                    Text = "Good to see you again.",
                    Mode = 5
                });
                return;
            }

            OfferGreeting(session, inst, def, first: true);
        }

        private static void OfferGreeting(WorldSession session, Instance inst, NpcDef def, bool first)
        {
            LineDef greet = Line(def, first ? "greet" : "greet_again")
                ?? Line(def, "greet")
                ?? new LineDef
                {
                    Text = first
                        ? "Hold a moment, Luminary. Will you help me watch for fungus? Those explode-y ones keep popping near the Spire."
                        : "Changed your mind? Will you help me watch for fungus?",
                    Mode = 5,
                    Question = true,
                    Seconds = 12
                };
            greet.Question = true;
            Say(session, inst, greet);
        }

        private static LineDef Line(NpcDef def, string key)
            => def.Lines != null && def.Lines.TryGetValue(key, out LineDef line) ? line : null;

        private static void Say(WorldSession session, Instance inst, LineDef line)
        {
            if (line == null || string.IsNullOrEmpty(line.Text))
                return;
            ulong questionId = 0;
            uint mode = line.Mode;
            if (line.Question)
            {
                // Client only draws Yes / No for bubble modes 4 / 5 (FUN_142f859b0: 1 < mode - 4 skips the bubble path).
                if (mode is not (4 or 5))
                    mode = 4;
                lock (sync)
                {
                    questionId = QuestionIdBit | nextQuestion++;
                    questions[questionId] = new PendingQuestion
                    {
                        NpcId = inst.Def.Id,
                        Creature = inst.Guid,
                        AskedOf = session.PlayerGuid,
                        Until = DateTime.UtcNow.AddSeconds(Math.Max(line.Seconds, 8) + 5)
                    };
                }
            }
            // Speaker must be a *client-side* full actor for NpcDialogResponse (L30). Server FullNpcs is set the
            // moment SendFull is queued — before LightweightToFullNpc is applied — so using the creature then
            // yields a bubble with no Yes/No. Story Tools falls back to a streamed prop; world NPCs use the
            // interactor for the question bubble only. Chat must not use that speaker (mode 5 would print
            // "[player] says: …"), so questions are bubble-only (mode 4) plus a separate NpcSay as the NPC.
            if (line.Question)
            {
                mode = 4;
                Props.DisplayTextTo(session, session.PlayerGuid, line.Text, mode, line.Seconds, questionId, creature: 0);
                Vector3 at = Creatures.StoryPosition(inst.Guid) ?? session.Position;
                string name = Creatures.StoryName(inst.Guid) ?? inst.Def.DisplayName;
                Chat.NpcSay(inst.Guid, name, inst.Zone, at, line.Text);
            }
            else if (Creatures.IsFullFor(session, inst.Guid))
            {
                Props.DisplayTextTo(session, inst.Guid, line.Text, mode, line.Seconds, 0, creature: inst.Guid);
            }
            else
            {
                // No full actor yet: chat as the NPC, skip DisplayPropText so we don't attribute to the player.
                Vector3 at = Creatures.StoryPosition(inst.Guid) ?? session.Position;
                string name = Creatures.StoryName(inst.Guid) ?? inst.Def.DisplayName;
                Chat.NpcSay(inst.Guid, name, inst.Zone, at, line.Text);
            }
            log.Info($"{session.CharacterName}: NPC {inst.Def.Id} text mode={mode} q={questionId} full={Creatures.IsFullFor(session, inst.Guid)}");
            Creatures.StoryFocus(inst.Guid, session.Position);
        }

        private static void PauseForTalk(Instance inst, WorldSession session)
        {
            lock (sync)
            {
                inst.TalkingTo = session.PlayerGuid;
                inst.TalkUntil = DateTime.UtcNow.AddSeconds(20);
                inst.Walking = false;
            }
            Creatures.StoryWalk(inst.Guid, Creatures.StoryPosition(inst.Guid) ?? inst.Home, 0);
            Creatures.StoryFocus(inst.Guid, session.Position);
        }

        public static void Update()
        {
            FlushPendingSpeaks();
            if (DateTime.UtcNow < nextTick)
                return;
            nextTick = DateTime.UtcNow.AddMilliseconds(500);
            List<Instance> list;
            lock (sync)
                list = byId.Values.ToList();
            foreach (Instance inst in list)
            {
                TickProximity(inst);
                TickPatrol(inst);
            }
            // Drop old questions.
            lock (sync)
            {
                foreach (ulong id in questions.Where(kv => kv.Value.Until < DateTime.UtcNow).Select(kv => kv.Key).ToList())
                    questions.Remove(id);
            }
        }

        private static void FlushPendingSpeaks()
        {
            List<PendingSpeak> due;
            lock (sync)
            {
                DateTime now = DateTime.UtcNow;
                due = pendingSpeaks.Where(p => p.When <= now).ToList();
                if (due.Count == 0)
                    return;
                pendingSpeaks.RemoveAll(p => p.When <= now);
            }
            foreach (PendingSpeak p in due)
            {
                Instance inst;
                lock (sync)
                    byId.TryGetValue(p.NpcId, out inst);
                if (inst != null && p.Session?.RezoneTarget == null)
                    SpeakFor(p.Session, inst);
            }
        }

        private static void TickProximity(Instance inst)
        {
            if (inst.Def.HailRange <= 0f)
                return;
            Vector3? at = Creatures.StoryPosition(inst.Guid);
            if (at == null)
                return;
            foreach (WorldSession s in PlayerManager.InZone(inst.Zone))
            {
                if (s.RezoneTarget != null)
                    continue;
                float dist = Vector3.Distance(s.Position, at.Value);
                if (dist > inst.Def.HailRange)
                    continue;
                if (HasFlag(s.PlayerGuid, inst.Def.Id, "hailed") || HasFlag(s.PlayerGuid, inst.Def.Id, "met"))
                    continue;
                // Chain-locked NPCs stay quiet until unlocked.
                if (!string.IsNullOrEmpty(inst.Def.RequiresNpcTurnedIn)
                    && !HasFlag(s.PlayerGuid, inst.Def.RequiresNpcTurnedIn, "turned_in"))
                    continue;
                SetFlag(s, inst.Def.Id, "hailed", 1);
                Say(s, inst, Line(inst.Def, "hail") ?? new LineDef
                {
                    Text = "Over here! A word, if you have a moment.",
                    Mode = 4,
                    Seconds = 5
                });
            }
        }

        private static void TickPatrol(Instance inst)
        {
            if (inst.Route.Count < 2)
                return;
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                if (inst.TalkingTo != 0 && now < inst.TalkUntil)
                    return;
                inst.TalkingTo = 0;
                if (now < inst.NextDwell)
                    return;
            }
            Vector3? pos = Creatures.StoryPosition(inst.Guid);
            if (pos == null)
                return;
            Vector3 target = inst.Route[inst.RouteIndex % inst.Route.Count];
            if (Vector3.Distance(pos.Value, target) < 1.5f)
            {
                inst.RouteIndex = (inst.RouteIndex + 1) % inst.Route.Count;
                inst.NextDwell = now.AddSeconds(Math.Max(1f, inst.Def.DwellSeconds));
                inst.Walking = false;
                Creatures.StoryFocus(inst.Guid, null);
                return;
            }
            if (!inst.Walking)
            {
                Creatures.StoryWalk(inst.Guid, target, 0);
                inst.Walking = true;
            }
        }

        /// <summary>Admin helpers: reload | goto &lt;id&gt; | talk &lt;id&gt; | reset | flag &lt;id&gt; &lt;key&gt; [value].</summary>
        public static string DevCommand(WorldSession session, string[] args)
        {
            if (args.Length == 0)
                return "usage: npc reload | npc goto <id> | npc talk <id> | npc reset | npc flag <id> <key> [value]";
            switch (args[0].ToLowerInvariant())
            {
                case "reload":
                    LoadDefinitions();
                    RespawnAll();
                    return $"reloaded {byId.Count} world NPC(s).";
                case "goto":
                {
                    if (args.Length < 2 || !byId.TryGetValue(args[1], out Instance inst))
                        return $"unknown NPC id (have: {string.Join(", ", byId.Keys)}).";
                    Vector3 at = (Creatures.StoryPosition(inst.Guid) ?? inst.Home) + new Vector3(2f, 0f, 2f);
                    // NPCs live on a fixed landscape (home continent by default). Teleport.To alone stays on the
                    // player's current land — so a Pioneer / other landscape visit never saw the Scout (PT1).
                    if (session.ZoneId != inst.Zone)
                        Teleport.ToZone(session, at, inst.Zone, snapToGround: true);
                    else
                        Teleport.To(session, at, snapToGround: true);
                    return $"teleported to {inst.Def.Id} (landscape {inst.Zone}).";
                }
                case "talk":
                {
                    if (args.Length < 2 || !byId.TryGetValue(args[1], out Instance talk))
                        return $"unknown NPC id (have: {string.Join(", ", byId.Keys)}).";
                    if (!Creatures.IsFullFor(session, talk.Guid))
                        Creatures.SendFull(session, talk.Guid);
                    SpeakFor(session, talk);
                    return $"spoke as {talk.Def.Id} ({talk.Def.DisplayName}).";
                }
                case "reset":
                    ClearFlags(session.PlayerGuid);
                    foreach (NpcDef def in AllDefs())
                        if (def.QuestAchievementId != 0)
                            Achievements.ResetNpcQuest(session, def.QuestAchievementId);
                    Achievements.SendInitial(session);
                    return "NPC flags and quest goals reset for you.";
                case "flag":
                {
                    if (args.Length < 3)
                        return "usage: npc flag <id> <key> [value]";
                    int value = args.Length > 3 && int.TryParse(args[3], out int v) ? v : 1;
                    SetFlag(session, args[1], args[2], value);
                    return $"flag {args[1]}.{args[2]}={value}.";
                }
                default:
                    return "usage: npc reload | npc goto <id> | npc talk <id> | npc reset | npc flag <id> <key> [value]";
            }
        }

        public static string Describe()
        {
            lock (sync)
                return string.Join("; ", byId.Select(kv =>
                {
                    Vector3? p = Creatures.StoryPosition(kv.Value.Guid);
                    return $"{kv.Key}@{p?.X:F0},{p?.Y:F0},{p?.Z:F0}";
                }));
        }
    }
}
