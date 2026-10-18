using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The client-executed movement effects of Shift's sprint and the boot abilities (OPEN-LEADS L160): the
    /// <c>List&lt;ClientExecutedEffectData&gt;</c> of their Abilities CasterInfoReply, and how their casts are answered
    /// (channels, charges, Energy, cooldowns; OPEN-LEADS L168).
    /// <para>
    /// <b>Why the server must send them [P, 0.695 decompile].</b> The movement itself is the client's, but the client only
    /// starts it from these effects:
    /// <list type="bullet">
    /// <item>the reply reader FUN_142e016b0 reads the list into reply +0xf8 (record +0xe0) with FUN_142e04680; each element
    /// (FUN_142e03a00) is 14 × 4 bytes;</item>
    /// <item>when the cast starts (FUN_142e0f810; for a charged cast at FinishChargedCast, FUN_142e14780) FUN_142e255e0 builds
    /// one ClientExecutedEffect per element through the ObjectFactory keyed by the type hash at +4, and runs it;</item>
    /// <item>only those effects put the local player into the states: the sprint flag (entity +0x145b bit 0x80) and the
    /// speed multiplier +0x4cd0 are set only by ClientExecutedEffectSprint / ChanneledSprint (FUN_14354bbc0 /
    /// FUN_14354b8a0); the controller's own sprint path (FUN_143008ee0 / FUN_143010260) is gated by the client's
    /// IsAdminClient (PlayClient vtable +0xB0 = FUN_142b21000, "return 0" in retail). Gliding (entity +0x1450 bit 0,
    /// FUN_142ec1bf0(…, 1)) is set only by the Glide effects (FUN_14354ba90 / FUN_14354b830); the boosted jump and the
    /// double jump (jump height +0x1824, forward launch +0x1868 / +0x186c, then FUN_142ec9cc0) only by the Jump /
    /// DoubleJump effects (FUN_14354bb30 / FUN_14354b920).</item>
    /// </list>
    /// With the list empty (as before 2026-10-18) Shift's sprint (326), Nimbus Boots' double jump, Breeze Boots' glide,
    /// the leaps and Neon Blitzing Boots' charge were cast and answered but nothing moved.
    /// </para>
    /// <para>
    /// <b>Element [P layout, FUN_142e03a00 / FUN_14354b670 and the seven effects' init functions]:</b> +0x00 u32 (not read by
    /// the effects; 0), +0x04 u32 effect type hash, +0x08 u32 (a condition id the ability manager's GiveTime FUN_142e174f0
    /// re-checks; 0), +0x0c..+0x2c nine f32, +0x30 / +0x34 u32 (not read; 0). The f32 hold <b>AbilityParameterDefinitions ids
    /// as floats</b> (each is read as a float and cast to int, then evaluated by the ParameterCalculator FUN_1435079e0 on the
    /// caster: Constant → PARAM_1; ChargedStatMultiplier → stat(PARAM_1) × PARAM_2 + PARAM_3 × charge + PARAM_4) and
    /// durations in seconds:
    /// <list type="bullet">
    /// <item>Sprint / ChanneledSprint / Fly: a 3-phase curve {start id, end id, seconds} at +0x0c / +0x18 / +0x24, linear
    /// per phase (FUN_14354b670, evaluated by FUN_14354afe0); a phase under 1 ms is skipped. Sprint's value is the speed
    /// multiplier (+0x4cd0, × the SprintSpeedModifier character stat, FUN_143329ed0) and ends with the curve; the channelled
    /// form (update FUN_14354b340) keeps the last value while the channel lasts and puts +0x4cd0 back to 1 when it ends
    /// (FUN_14354b0f0).</item>
    /// <item>Jump / DoubleJump: +0x0c the jump height (entity +0x1824, the controller's multiplier of Movement.JumpHeight
    /// 1.5; its default is 3.0, FUN_142e68240 / ClientAreaPhysicsProperty), +0x10 the forward launch in metres covered while
    /// rising (+0x1868: the controller update FUN_142e69e50 turns it into a horizontal speed of distance / rise time) or
    /// 271 for none (DAT_1452667f0 = 271; row 271 is the
    /// Constant 666 "unused" marker). Both are evaluated with the cast's charge (ability manager +0x2c4).</item>
    /// <item>Glide: +0x0c the per-update factor on the vertical speed while gliding (entity +0x1864 → controller +0x184,
    /// FUN_142e6dfe0: v.y ×= factor when ≥ 0), +0x10 how long (seconds; 0 = until landing, FUN_14354b4f0). ChanneledGlide
    /// (init FUN_14354bcc0) reads only the factor and lasts until landing or the channel's end (update FUN_14354b2e0).</item>
    /// </list>
    /// Every effect is scheduled with no delay when the cast is sent (FUN_143b08f50(…, 0, 0, 0) in all eight init
    /// functions 14354bc80 … 14354c1f0), so the movement never waits for the server's answer or a cast time.
    /// The cast gate FUN_142e0f4f0 refuses a cast unless each effect's precondition holds (in the air for a double jump or a
    /// glide, able to sprint …).
    /// </para>
    /// <para>
    /// <b>Channels [P, 0.695].</b> Sprint (326), Immediate Delivery (328) and Endless Breeze (542) are held keys
    /// (InputActionGroupEntries TRIGGER_ON 2) and their 0.695 texts begin "Channel…". Their replies carry the channelled kind
    /// <see cref="ChannelledKind"/> 0x216b5dcb:
    /// <list type="number">
    /// <item><b>Press.</b> The cast start FUN_142e0f810 sets the ability manager's cast mode +0x2b0 = 2 (3 when the reply's
    /// bool at record +0xad is set; we send 2) and starts the effects at once; FUN_142e255e0 swaps Sprint → ChanneledSprint
    /// and Glide → ChanneledGlide in modes 2-3 and keeps the channelled one at +0x2b8. The input manager
    /// (ClientInputActionManager::UpdateCastState FUN_1434793b0) puts its own state +0x18d4 = 3 for kind 0x216b5dcb.</item>
    /// <item><b>Hold.</b> Nothing from the server is needed; a CastSuccess would end it (FUN_142e18340 resets the mode and ends
    /// +0x2b8), so none is sent. The server confirms with CastContinue (sub 8, FUN_142e17b90), which never resets the cast and
    /// restarts the effects if none is running.</item>
    /// <item><b>Release.</b> The input manager's GiveTime FUN_1434783f0 (state 3) sees the entry's key up ("GiveTime: Channel:
    /// end ability") and calls CancelCast FUN_142e0f5a0(manager, 0): it ends +0x2b8 and, only because one was running, clears
    /// the current ability +0x2a0 and the casting time +0x290; then it sends <b>CancelCast 0x27/0x0F {bool 0}</b> (no ability,
    /// no cast guid).</item>
    /// <item><b>The stuck case.</b> A channelled effect can end on its own first: ChanneledGlide on landing (FUN_14354b2e0),
    /// ChanneledSprint when the player can no longer sprint (FUN_14354b340), and GiveTime FUN_142e174f0 then clears +0x2b8.
    /// The release's CancelCast then leaves +0x2a0 and +0x290 set, and the can-cast check FUN_142e0f080 refuses every cast
    /// (errors 15 / 2: no tool swings, no weapons) until the server answers. So every release is answered with a
    /// CastSuccess for that cast (Force off, so it acts only while the ability is still the current one). The answer
    /// waits <see cref="ReleaseAnswerDelay"/> and is skipped when another cast arrives meanwhile (a stuck client cannot
    /// cast): CastSuccess and CastFail match the current cast by ability id only, so an early answer would end a fresh
    /// press of the same key [P handler, I timing].</item>
    /// <item><b>Server end.</b> Out of Energy, death or a zone change: a CastSuccess (Force off) ends the client's channel; its
    /// input manager then finds the mode back at 0 and sends nothing (FUN_142e0f5a0 returns early with no current cast).</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Costs and cooldowns [P 14 Oct 2014 dev post, I that 2016 kept them].</b> Heroic Movement post
    /// (design/forum/all/2014-10-14-49225): Cloud Burst 50 Energy, 5 s; Charged Leap 100, 5 s; Explosive Leap 80, 10 s;
    /// Electric Charge 75, 10 s, 0.5 s cast; Immediate Delivery 40 Energy a second, 10 s; Infinite Glide (Breeze) 0 / s,
    /// 0 s. Shift's sprint has none (the 2014 sprint was folded into the base run speed) [I]. The reply's cost fields make
    /// the client refuse a cast it cannot pay itself (FUN_142e0f080 error 3) before any effect starts; the cooldown is the
    /// reply's +0x70 (record +0x14c) on the ability's own cooldown group +0xa0 (record +0x7c), whose timer the client starts
    /// at the CastSuccess when +0xa4 is set (FUN_142fd1cb0; FUN_142e187a0 copies the fields) [P]. The server pays the Energy
    /// (<see cref="WeaponAttacks.TrySpendEnergy"/>, sent to the client) and keeps the cooldowns too, refusing with CastFail.
    /// </para>
    /// <para>
    /// <b>Values.</b> Which rows live used is server data no client table names [I]; the shapes follow the same post (Electric
    /// Charge "jumps you forward and then increases your movement by 25% for 3 seconds", Immediate Delivery +37.5 %, Charged
    /// Leap 0-1.25 s / Explosive Leap 0-1.5 s of charge "the longer you charge, the greater distance", Cloud Burst a double
    /// jump with +10 % damage for 5 s, Breeze Boots a glide). Jump heights use 3.0 (row 205), the client's own default,
    /// because the client keeps the last jump height for later jumps (no effect restores it).
    /// </para>
    /// Server setting MovementEffects (Docker MOVEMENT_EFFECTS, default true = live: the effects are sent and the casts are
    /// answered as above); dev <c>movementeffects on|off</c> (new caster-info replies only; the client keeps the ones it has
    /// until relog). Off answers every movement cast at once with no effects, costs or cooldowns, as before 2026-10-18.
    /// </summary>
    public static class MovementEffects
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static bool? enabled;

        /// <summary>
        /// Whether caster-info replies carry the movement effects: the server setting MovementEffects unless the dev
        /// command switched it.
        /// </summary>
        public static bool Enabled
        {
            get => enabled ?? LandmarkEmulator.Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.MovementEffects ?? true;
            set => enabled = value;
        }

        // Effect type hashes (factory registration part_0070 ~25208, creators FUN_142dfd020 …) [P].
        public const uint Glide = 0x9055911d, ChanneledGlide = 0x1c3c381c, Jump = 0x80d81da0, DoubleJump = 0x7f2bc64f,
            Sprint = 0x7faad620, ChanneledSprint = 0xf1fae31f, Fly = 0xea3d9ce0;

        /// <summary>
        /// The channelled cast kind (CasterInfoReply +0x1c → record +4): FUN_142e0f810 accepts it as castable and sets the
        /// channel mode; FUN_1434793b0 gives it input state 3 (ended when the key is let go) [P].
        /// </summary>
        public const uint ChannelledKind = 0x216b5dcb;

        /// <summary>The "no second parameter" marker of Jump / DoubleJump (+0x10 compared with (float)271) [P].</summary>
        public const float None = 271f;

        // AbilityParameterDefinitions rows used (gameData695) [P values; I choice].
        private const float One = 202f;          // Constant 1
        private const float Zero = 3f;           // Constant 0
        private const float Quarter = 36f;       // Constant 1.25
        private const float SevenTwelfths = 242f; // Constant 1.417 (Immediate Delivery's +37.5 %: the nearest row)
        private const float Three = 205f;        // Constant 3 (the client's default jump height)
        private const float Five = 9f;           // Constant 5
        private const float Ten = 25f;           // Constant 10
        private const float ChargedFifteenToTwentyFive = 313f; // ChargedStatMultiplier: 10 × charge + 15
        private const float GlideFactor = 714f;  // Constant 0.9

        /// <summary>One ClientExecutedEffectData element (14 × 4 bytes).</summary>
        public sealed class Effect
        {
            public uint Type;
            /// <summary>+0x0c..+0x2c: parameter ids (as floats) and durations.</summary>
            public readonly float[] Values = new float[9];

            public Effect(uint type, params float[] values)
            {
                Type = type;
                Array.Copy(values, Values, Math.Min(values.Length, 9));
            }

            public void Write(LandmarkEmulator.Shared.Network.GamePacketWriter writer)
            {
                writer.Write(0u);
                writer.Write(Type);
                writer.Write(0u);
                foreach (float v in Values)
                    writer.Write(v);
                writer.Write(0u);
                writer.Write(0u);
            }
        }

        /// <summary>
        /// A channelled sprint: up to <paramref name="peak"/> in 0.25 s, then held at it (the last two phases are 0 s, so the
        /// channelled form keeps the peak, FUN_14354b340) until the channel ends, when the client puts the speed back to ×1.
        /// The type is Sprint; the client swaps it for ChanneledSprint in a channelled cast (FUN_142e255e0) [P].
        /// </summary>
        private static Effect ChannelSprint(float peak) => new(Sprint, One, peak, 0.25f, peak, peak, 0f, peak, peak, 0f);

        /// <summary>Charged leaps: the longest charge (seconds, 14 Oct 2014 post) [P dev post, I that 2016 kept it].</summary>
        private static readonly Dictionary<uint, float> maxCharge = new() { [321] = 1.25f, [322] = 1.5f };

        public const uint CloudBurst = 320, ExplosiveLeap = 322;

        private static readonly Dictionary<uint, Effect[]> effects = new()
        {
            // Shift / Adventurer's Sprint Boots: Adventurer's Sprint (+25 %) [I], while Shift is held.
            [Abilities.HeroicMovementAbility] = new[] { ChannelSprint(Quarter) },
            // Nimbus Boots: Cloud Burst, a double jump.
            [CloudBurst] = new[] { new Effect(DoubleJump, Three, None) },
            // Boots of Bounding: Charged Leap, forward 15-25 m while rising by charge (about twice that in all).
            [321] = new[] { new Effect(Jump, Three, ChargedFifteenToTwentyFive) },
            // Leaper's Launchers: Explosive Leap, forward 10 m while rising.
            [ExplosiveLeap] = new[] { new Effect(Jump, Three, Ten) },
            // Neon Blitzing Boots: Electric Charge, a jump forward then +25 % for 3 s.
            [327] = new[] { new Effect(Jump, Three, Five), new Effect(Sprint, Quarter, Quarter, 3f, One, One, 0f, One, One, 0f) },
            // Messenger's Sandals: Immediate Delivery (+37.5 %; nearest row +41.7 %) while the key is held.
            [328] = new[] { ChannelSprint(SevenTwelfths) },
            // Breeze Boots: Endless Breeze, a glide while the key is held, until landing.
            [542] = new[] { new Effect(Glide, GlideFactor, Zero) },
        };

        /// <summary>
        /// A movement ability's cost and timing: Energy (a second for a channel), cooldown and cast time (seconds) [P 14 Oct
        /// 2014 dev post; I that 2016 kept them; 326 free I].
        /// </summary>
        public sealed record Rule(uint Energy, float Cooldown, float CastTime, bool Channelled);

        private static readonly Dictionary<uint, Rule> rules = new()
        {
            [Abilities.HeroicMovementAbility] = new(0, 0f, 0f, true),   // Adventurer's Sprint
            [CloudBurst] = new(50, 5f, 0f, false),                      // Cloud Burst
            [321] = new(100, 5f, 0f, false),                            // Charged Leap
            [ExplosiveLeap] = new(80, 10f, 0f, false),                  // Explosive Leap
            [327] = new(75, 10f, 0.5f, false),                          // Electric Charge
            [328] = new(40, 10f, 0f, true),                             // Immediate Delivery, 40 a second
            [542] = new(0, 0f, 0f, true),                               // Endless Breeze (Infinite Glide)
        };

        /// <summary>The movement effects for this ability's caster-info reply (empty when off or none).</summary>
        public static IReadOnlyList<Effect> For(uint ability)
            => Enabled && effects.TryGetValue(ability, out Effect[] list) ? list : Array.Empty<Effect>();

        /// <summary>
        /// Adds the ability's effects to its caster-info reply: a channel gets the channelled kind, a leap becomes a charged
        /// cast, and each gets its Energy cost, cooldown (on its own cooldown group) and cast time.
        /// </summary>
        public static void Describe(AbilityCasterInfoReply reply)
        {
            uint id = reply.AbilityId;
            reply.ExecutedEffects.AddRange(For(id));
            if (!Enabled)
                return;
            if (maxCharge.TryGetValue(id, out float full))
            {
                reply.KindHash = Combat.WeaponCombat.ChargedKind;
                reply.MaxCharge = full;
                reply.MinCharge = 0f;
            }
            if (!rules.TryGetValue(id, out Rule rule))
                return;
            if (rule.Channelled)
                reply.KindHash = ChannelledKind;
            if (rule.Energy > 0)
            {
                // Energy (resource type 2): the client refuses the cast itself below this (can-cast error 3).
                reply.CostResource = CharacterResources.EnergyType;
                reply.CostAmount = rule.Energy;
                reply.IgnoreCostPool = false;
            }
            if (rule.Cooldown > 0)
            {
                // +0x70 → record +0x14c the cooldown; +0xa0 → record +0x7c the cooldown group: the ability's own, so other
                // group-0 casts (harvest swings) don't restart it.
                reply.U70 = rule.Cooldown;
                reply.Ua0 = id;
            }
            if (rule.CastTime > 0)
                reply.CastTime = rule.CastTime;
        }

        /// <summary>Is this a charged leap (answered at FinishChargedCast)?</summary>
        public static bool IsCharged(uint ability) => Enabled && maxCharge.ContainsKey(ability);

        private static readonly object sync = new();

        // ---- Timers on the world tick ----------------------------------------------------------------------------------

        /// <summary>
        /// Work due later (the charge timeout, the channel ticks, a release's answer), run by <see cref="Update"/> on the world
        /// thread. Until 2026-10-18 these were Task.Delay continuations on pool threads: the channel tick read session state
        /// the world thread changes (Combat.Gear.Has walks the inventory while gathering adds to it: "Collection was
        /// modified"), and an exception there was swallowed by the faulted task, so the channel stopped ticking and never
        /// ended server-side (review #6).
        /// </summary>
        private static readonly List<(DateTime Due, Action Do)> timers = new();

        private static void Later(TimeSpan delay, Action action)
        {
            lock (timers)
                timers.Add((DateTime.UtcNow + delay, action));
        }

        /// <summary>World tick (HostedService): runs the timers that are due.</summary>
        public static void Update()
        {
            List<Action> due;
            DateTime now = DateTime.UtcNow;
            lock (timers)
            {
                if (timers.Count == 0)
                    return;
                due = timers.Where(t => t.Due <= now).Select(t => t.Do).ToList();
                if (due.Count == 0)
                    return;
                timers.RemoveAll(t => t.Due <= now);
            }
            foreach (Action action in due)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    log.Error(e, "Movement effect timer failed.");
                }
            }
        }

        /// <summary>
        /// Answers a movement cast (Shift's 326 or a worn boot's ability): Energy and cooldown first (CastFail when either
        /// refuses), then a channel, a charged leap or an instant answer. False when the movement effects are off or the
        /// ability has no rule here: the caller answers it as before.
        /// </summary>
        public static bool HandleCast(WorldSession p, AbilityCastAttempt attempt)
        {
            uint id = attempt.AbilityId;
            if (!Enabled || !rules.TryGetValue(id, out Rule rule))
                return false;
            if (CoolingDown(p, id))
            {
                log.Info($"{p.CharacterName} movement ability {id}: still cooling down (CastFail).");
                Abilities.Refuse(p, attempt);
                return true;
            }
            if (rule.Energy > 0 && !WeaponAttacks.TrySpendEnergy(p, rule.Energy))
            {
                log.Info($"{p.CharacterName} movement ability {id}: not enough Energy for {rule.Energy} (CastFail).");
                Abilities.Refuse(p, attempt);
                return true;
            }
            if (rule.Channelled)
            {
                BeginChannel(p, attempt, rule);
                return true;
            }
            if (BeginCharge(p, attempt))
                return true;
            StartCooldown(p, id, rule);
            Abilities.Answer(p, attempt);
            if (id == CloudBurst)
                // "temporarily gain increased damage" (565069/565070) [P text]: +10 % for 5 s [P 2014 post, I 2016].
                Buffs.AddStat(p, CloudBurstBuff, Combat.GearStats.DamageStatId, 0f, CloudBurstDamagePercent,
                    TimeSpan.FromSeconds(CloudBurstSeconds), NimbusBoots, announce: false);
            return true;
        }

        // ---- Cloud Burst's damage buff ---------------------------------------------------------------------------------

        /// <summary>Cloud Burst: +10 % damage for 5 s (14 Oct 2014 post) [P 2014, I 2016]; the buff shows Nimbus Boots' icon.</summary>
        public const string CloudBurstBuff = "Cloud Burst";
        public const float CloudBurstDamagePercent = 10f, CloudBurstSeconds = 5f;
        private const uint NimbusBoots = 1120;

        /// <summary>
        /// The factor movement abilities put on the player's weapon damage now: ×1.1 while Cloud Burst lasts. (Attacks with
        /// their own parameter row get it through the buffed Damage stat instead, WeaponCombat.RowDamage.)
        /// </summary>
        public static float DamageFactor(WorldSession p)
            => Buffs.Has(p, CloudBurstBuff) ? 1f + CloudBurstDamagePercent / 100f : 1f;

        // ---- Cooldowns -------------------------------------------------------------------------------------------------

        /// <summary>
        /// The server's cooldowns run this much shorter than the client's, which starts its own when the CastSuccess arrives
        /// (one trip later than the server sends it) [I].
        /// </summary>
        private static readonly TimeSpan CooldownSlack = TimeSpan.FromSeconds(0.3);

        private static readonly Dictionary<(WorldSession, uint), DateTime> readyAt = new();

        private static bool CoolingDown(WorldSession p, uint ability)
        {
            lock (sync)
                return readyAt.TryGetValue((p, ability), out DateTime ready) && ready > DateTime.UtcNow;
        }

        /// <summary>Starts an ability's cooldown: at the answer for an instant cast, at the release for a leap or a channel.</summary>
        private static void StartCooldown(WorldSession p, uint ability, Rule rule)
        {
            if (rule.Cooldown <= 0)
                return;
            lock (sync)
                readyAt[(p, ability)] = DateTime.UtcNow + TimeSpan.FromSeconds(rule.Cooldown) - CooldownSlack;
        }

        // ---- Charged leaps -------------------------------------------------------------------------------------------

        private static readonly Dictionary<WorldSession, (AbilityCastAttempt Attempt, DateTime Began)> charging = new();

        /// <summary>
        /// A charged leap started: the client charges until the key is let go and starts the Jump effect itself then
        /// (FUN_142e14780); the cast is answered only at FinishChargedCast or CancelCast (a CastSuccess during the charge
        /// would end it). Returns false when it is not one.
        /// </summary>
        public static bool BeginCharge(WorldSession p, AbilityCastAttempt attempt)
        {
            if (!IsCharged(attempt.AbilityId))
                return false;
            AbilityCastAttempt old = null;
            lock (sync)
            {
                if (charging.Remove(p, out var was))
                    old = was.Attempt;
                charging[p] = (attempt, DateTime.UtcNow);
            }
            if (old != null)
                Abilities.AnswerCharged(p, old, 0);
            float full = maxCharge[attempt.AbilityId];
            // A charge nobody let go of (lost packet): answered after the longest charge plus the client's 4 s auto-release.
            Later(TimeSpan.FromSeconds(full + 5), () => End(p, attempt));
            log.Debug($"{p.CharacterName} charging leap {attempt.AbilityId}.");
            return true;
        }

        /// <summary>
        /// FinishChargedCast / CancelCast: answers a pending leap. Returns true when one was pending. A released leap starts
        /// its cooldown, and Explosive Leap's take-off blast goes off.
        /// </summary>
        public static bool Finish(WorldSession p, AbilityFinishChargedCast finish)
        {
            AbilityCastAttempt a;
            DateTime began;
            lock (sync)
            {
                if (!charging.TryGetValue(p, out var pending) || (finish != null && !finish.Names(pending.Attempt.AbilityId)))
                    return false;
                charging.Remove(p);
                (a, began) = pending;
            }
            if (!p.Disconnected)
                Abilities.AnswerCharged(p, a, 0);
            if (finish != null && rules.TryGetValue(a.AbilityId, out Rule rule))
                StartCooldown(p, a.AbilityId, rule);
            if (finish != null && a.AbilityId == ExplosiveLeap && !p.Disconnected)
                TakeOffBlast(p, Math.Clamp((float)(DateTime.UtcNow - began).TotalSeconds / maxCharge[ExplosiveLeap], 0f, 1f));
            log.Info($"{p.CharacterName} leap {a.AbilityId} {(finish != null ? "released" : "given up")}.");
            return true;
        }

        /// <summary>
        /// The charge timeout: gives up <paramref name="attempt"/>'s charge if it is still the one pending. The check and the
        /// removal are one step under the lock; calling <see cref="Finish"/> after releasing it (as before 2026-10-18) could
        /// end a leap started in between instead (review #6).
        /// </summary>
        private static void End(WorldSession p, AbilityCastAttempt attempt)
        {
            lock (sync)
            {
                if (!charging.TryGetValue(p, out var a) || !ReferenceEquals(a.Attempt, attempt))
                    return;
                charging.Remove(p);
            }
            if (!p.Disconnected)
                Abilities.AnswerCharged(p, attempt, 0);
            log.Info($"{p.CharacterName} leap {attempt.AbilityId} given up (no release).");
        }

        /// <summary>Explosive Leap's blast radius round the player, metres [I: no source gives it].</summary>
        public const float BlastRadius = 5f;

        /// <summary>
        /// Explosive Leap: "a powerful leap forward when released that deals damage to all opponents near you" (565077 /
        /// 565078) [P text]; the take-off damage grows with the charge (14 Oct 2014 post) [P 2014]. The amount is ours: the
        /// player's base weapon damage × (1 + charge) to every creature within <see cref="BlastRadius"/> in sight [I].
        /// (AbilityParameterDefinitions row 429, Offense × 1.3 + 500 × charge, is only a weak candidate, OPEN-LEADS L168.)
        /// </summary>
        private static void TakeOffBlast(WorldSession p, float charge)
        {
            uint damage = (uint)Math.Round(Abilities.WeaponDamageFor(p) * (1f + charge));
            int hit = 0;
            foreach (ulong target in Creatures.InSight(p.ZoneId, p.Position, BlastRadius))
                if (Creatures.Damage(p, target, damage, ranged: false).Dealt > 0)
                    hit++;
            log.Info($"{p.CharacterName} Explosive Leap blast (charge {charge:P0}): {damage} to {hit} creature(s).");
        }

        // ---- Channels (326 Sprint, 328 Immediate Delivery, 542 Endless Breeze) ---------------------------------------

        private sealed class Channel
        {
            public AbilityCastAttempt Attempt;
            public Rule Rule;
            public DateTime Started;
            public uint Zone;
            /// <summary>A zone change stopped it here; the client is told once the re-zone is over.</summary>
            public bool Stopped;
        }

        private static readonly Dictionary<WorldSession, Channel> channels = new();

        /// <summary>Casts seen per player (<see cref="NoteCast"/>): a release's answer is skipped when another came since.</summary>
        private static readonly Dictionary<WorldSession, long> castsSeen = new();

        /// <summary>
        /// How long a release's CastSuccess waits for proof that the client is not stuck (any further cast) [I]. A stuck
        /// client waits this long before it can cast again.
        /// </summary>
        public static readonly TimeSpan ReleaseAnswerDelay = TimeSpan.FromSeconds(1);

        /// <summary>The Energy tick of a channel and its checks (zone change, death) [I: the 2014 cost is "per second"].</summary>
        private static readonly TimeSpan ChannelTick = TimeSpan.FromSeconds(1);

        /// <summary>Every cast attempt (Abilities.HandleCast) counts: a client that casts is not stuck in a channel.</summary>
        public static void NoteCast(WorldSession p)
        {
            lock (sync)
                castsSeen[p] = castsSeen.GetValueOrDefault(p) + 1;
        }

        /// <summary>
        /// A channel starts (the first second's Energy is paid): confirmed with CastContinue, never with a CastSuccess, which
        /// would end it. An older channel still on record (its CancelCast lost) is dropped without an answer: the client
        /// replaced it with this cast.
        /// </summary>
        private static void BeginChannel(WorldSession p, AbilityCastAttempt attempt, Rule rule)
        {
            var channel = new Channel { Attempt = attempt, Rule = rule, Started = DateTime.UtcNow, Zone = p.ZoneId };
            lock (sync)
                channels[p] = channel;
            p.EnqueueMessage(new AbilityCastContinue
            {
                CasterGuid = p.PlayerGuid,
                CastGuid = attempt.CastGuid,
                CastHash = attempt.CastHash,
                AbilityId = attempt.AbilityId
            });
            log.Debug($"{p.CharacterName} channels {attempt.AbilityId}.");
            ScheduleTick(p, channel);
        }

        private static void ScheduleTick(WorldSession p, Channel channel)
            => Later(ChannelTick, () => SafeTick(p, channel));

        /// <summary>A tick that throws ends its channel (logged) rather than leaving it open with no further tick.</summary>
        private static void SafeTick(WorldSession p, Channel channel)
        {
            try
            {
                Tick(p, channel);
            }
            catch (Exception e)
            {
                log.Error(e, $"{p.CharacterName} channel {channel.Attempt.AbilityId}: tick failed; ended.");
                EndChannel(p, channel, "tick failed");
            }
        }

        /// <summary>
        /// Once a second while a channel lasts: the Energy (a channel with a cost) and the safety ends: disconnect (dropped),
        /// death, out of Energy (CastSuccess now), a zone change or re-zone (stopped at once, the client told once it has
        /// arrived: packets during a re-zone are held for the player anyway).
        /// </summary>
        private static void Tick(WorldSession p, Channel channel)
        {
            lock (sync)
                if (!channels.TryGetValue(p, out Channel current) || !ReferenceEquals(current, channel))
                    return;
            if (p.Disconnected)
            {
                lock (sync)
                    channels.Remove(p);
                return;
            }
            if (!channel.Stopped && (p.ZoneId != channel.Zone || p.RezoneStarted >= channel.Started))
            {
                channel.Stopped = true;
                log.Info($"{p.CharacterName} channel {channel.Attempt.AbilityId}: zone change, stopped.");
            }
            if (channel.Stopped)
            {
                if (p.RezoneTarget == null)
                    EndChannel(p, channel, "after the zone change");
                else
                    ScheduleTick(p, channel);
                return;
            }
            if (Health.IsDead(p))
            {
                EndChannel(p, channel, "died");
                return;
            }
            if (channel.Rule.Energy > 0 && !WeaponAttacks.TrySpendEnergy(p, channel.Rule.Energy))
            {
                EndChannel(p, channel, "out of Energy");
                return;
            }
            ScheduleTick(p, channel);
        }

        /// <summary>The server ends a channel: CastSuccess (Force off: only while it is still the client's current cast).</summary>
        private static void EndChannel(WorldSession p, Channel channel, string why)
        {
            lock (sync)
            {
                if (!channels.TryGetValue(p, out Channel current) || !ReferenceEquals(current, channel))
                    return;
                channels.Remove(p);
            }
            StartCooldown(p, channel.Attempt.AbilityId, channel.Rule);
            if (!p.Disconnected)
                AnswerChannel(p, channel.Attempt);
            log.Info($"{p.CharacterName} channel {channel.Attempt.AbilityId} ended: {why}.");
        }

        /// <summary>
        /// The final answer of a channel: CastSuccess with Force off (it ends the client's channel and clears its casting
        /// state only while the ability is still the current cast, FUN_142e18340), then AbilityEnd.
        /// </summary>
        private static void AnswerChannel(WorldSession p, AbilityCastAttempt attempt)
        {
            p.EnqueueMessage(new AbilityCastSuccess
            {
                CasterGuid = p.PlayerGuid,
                CastGuid = attempt.CastGuid,
                CastHash = attempt.CastHash,
                AbilityId = attempt.AbilityId,
                Force = false
            });
            Abilities.SendAbilityEnd(p, attempt.CastGuid, attempt.CastHash, attempt.AbilityId);
        }

        /// <summary>
        /// CancelCast while a channel is open: the key was let go (or another action interrupted it). The Energy stops, the
        /// cooldown starts, and the client gets its CastSuccess after <see cref="ReleaseAnswerDelay"/> unless it casts
        /// something first (then it was not stuck, and an answer could end that new cast). Returns false when no channel was
        /// open.
        /// </summary>
        public static bool Release(WorldSession p, bool interrupted)
        {
            Channel channel;
            long seen;
            lock (sync)
            {
                if (!channels.Remove(p, out channel))
                    return false;
                seen = castsSeen.GetValueOrDefault(p);
            }
            StartCooldown(p, channel.Attempt.AbilityId, channel.Rule);
            log.Info($"{p.CharacterName} channel {channel.Attempt.AbilityId} {(interrupted ? "interrupted" : "released")} after "
                + $"{(DateTime.UtcNow - channel.Started).TotalSeconds:0.0} s.");
            Later(ReleaseAnswerDelay, () =>
            {
                bool castSince;
                lock (sync)
                    castSince = castsSeen.GetValueOrDefault(p) != seen;
                if (!castSince && !p.Disconnected)
                    AnswerChannel(p, channel.Attempt);
            });
            return true;
        }

        /// <summary>Dev: the player's open channel or charge.</summary>
        public static string DevState(WorldSession p)
        {
            lock (sync)
            {
                var parts = new List<string>();
                if (channels.TryGetValue(p, out Channel c))
                    parts.Add($"channel {c.Attempt.AbilityId} for {(DateTime.UtcNow - c.Started).TotalSeconds:0.0} s{(c.Stopped ? " (stopped, zone change)" : "")}");
                if (charging.TryGetValue(p, out var ch))
                    parts.Add($"charging {ch.Attempt.AbilityId} for {(DateTime.UtcNow - ch.Began).TotalSeconds:0.0} s");
                DateTime now = DateTime.UtcNow;
                foreach (var r in readyAt.Where(r => r.Key.Item1 == p && r.Value > now))
                    parts.Add($"{r.Key.Item2} ready in {(r.Value - now).TotalSeconds:0.0} s");
                return parts.Count == 0 ? "nothing" : string.Join("; ", parts);
            }
        }

        /// <summary>Forgets a player who left (their pending timers find nothing and do nothing).</summary>
        public static void Forget(WorldSession p)
        {
            lock (sync)
            {
                channels.Remove(p);
                charging.Remove(p);
                castsSeen.Remove(p);
                foreach (var key in readyAt.Keys.Where(k => k.Item1 == p).ToList())
                    readyAt.Remove(key);
            }
        }
    }
}
