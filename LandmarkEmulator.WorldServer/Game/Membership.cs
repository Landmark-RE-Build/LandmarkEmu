using LandmarkEmulator.Database.Auth.Model;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Membership and the trial key (TASKS 3.12; full trace and evidence: trace/0695/agent-membership.md, docs/membership.md).
    /// <para>
    /// <b>What the client has</b> [P, 0.695 decompile]. A membership <i>type</i> in the local player's data (+0xfc, the
    /// object at DAT_145431a20): 0 none, non-zero = member (Lua <c>Ui.IsMember</c>, FUN_1435d2ec0), 1 = trial
    /// (<c>Ui.IsTrialMember</c>, FUN_1435d2fa0), 2 = the one type <c>Ui.ShouldShowMemberNagScreen</c> (FUN_1435d36a0) never
    /// nags; the client's debug dump prints it as "Player membership type" (FUN_142f509b0). It arrives in
    /// SendSelfToClient (<see cref="Network.Message.Model.SendSelfToClient.MembershipStatus"/>) and changes with
    /// <b>PacketMembershipActivation 0x66</b> {u16 0x66, u32 type} (zone dispatcher case 0x2c, reader FUN_142c4a7c0, which
    /// must consume the packet exactly): the handler stores the type (FUN_142fd3bd0 → +0xfc), sets the local entity's
    /// member bit (FUN_142ec2ff0: +0x1452 bit 0, then the nameplate colour refresh FUN_142ecc900; the "member" effect it
    /// would play is looked up by the hash 0x782d2049, which is not in the 0.695 StringHashToValue.txt, so none plays),
    /// calls Lua <c>MembershipHandler:OnMembershipActivated(type)</c> (no such handler in the 0.695 scripts) and sends
    /// ActivityListRefreshRequest twice (zone and world channel; answered by <see cref="Network.Handlers.ActivityHandler"/>).
    /// Other players' entities carry the same bit: AddLightweightPc's second bool after the scale (reader FUN_142c26e50
    /// +0x115) and <b>PlayerUpdate 0x35 MemberStatus</b> {u64 guid, bool} (<see cref="MemberStatus"/>). Nothing found yet
    /// reads that bit. The type itself is read by the shared engine's store sorting by member price (FUN_1436fb5c0), its
    /// activity list (member-only activities get state 6, FUN_14371f3b0 / FUN_14331c580) and a client requirement check
    /// (FUN_143711350) — none of which Landmark used: no 0.695 window or script calls Ui.IsMember, no live item carries the
    /// members-only flag (definition +0x0C bit 0x20, tooltip "ItemEquipRequirementMembership"; checked over the
    /// 2,885-item dump), the Marketplace SWFs' IsMemberOnly / MemberSalePrice columns belong to the old real-money store,
    /// and the benefits text 557395 is PlanetSide 2's. Landmark's live membership (All Access) gave nothing in Landmark
    /// (wiki "Daybreak Cash": while in beta, Landmark had none of the usual All Access benefits).
    /// </para>
    /// <para>
    /// <b>What membership does here</b>: the client state above (SendSelf, 0x66 on every change, end included; the flag in
    /// AddLightweightPc and MemberStatus to the landscape) and the login queue's member priority (AuthServer LoginQueue:
    /// members wait ahead of non-members, "Member priority in queue" 62067). Bought with Lumens through
    /// <see cref="Purchase"/> (the Showcase store's hook, and the /membership command), given for
    /// <see cref="WorldServerConfiguration.TrialKeyDays"/> days by a friend's trial key. Any further benefit is the
    /// server owner's decision (trace/0695/agent-membership.md §7).
    /// </para>
    /// <para>
    /// <b>The trial key</b> (29 Aug 2016, P6417: "A single-use, 10-day trial key is now available for you to share with
    /// your friends … You should receive an in-game mail with details"). Live's letter is in the client (577594 "Invite
    /// your friends!", 577595 "… Enter "LandmarkTrial" at landmarkthegame.com in Account Management for a one-time free
    /// trial!"): the key was a website code for a new account. Ours keeps the letter and the rules — one key per account,
    /// for a friend, single use, 10 days, one trial per account — with the key as an item in the letter
    /// (<see cref="Inventory.ItemDefinitionBuilder.TrialKey"/>): it may be mailed once (it arrives as the NO_TRADE copy
    /// <see cref="Inventory.ItemDefinitionBuilder.TrialKeyMailed"/>), not to the sender's own account, and the mailed copy,
    /// used from the bag, gives the recipient's account trial membership (type 1).
    /// </para>
    /// State: the account (auth database: memberType, memberUntil, trialKeyMailed, trialRedeemed); the online copy is
    /// <see cref="WorldSession.Account"/>, written through to the database on every change.
    /// </summary>
    public static class Membership
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Membership types as the client reads them (player +0xfc).</summary>
        public const byte None = 0, Trial = 1, Member = 2;

        /// <summary>The player command: status, "buy", and for admins "grant name days" / "revoke name".</summary>
        public const string CommandName = "membership";

        /// <summary>Sender of the trial key letter [I: live's sender of 577594 is not in the client].</summary>
        private const string LetterFrom = "Landmark Team";

        /// <summary>Client text 577594 "Invite your friends!": the subject of live's trial key letter.</summary>
        private const uint LetterSubjectText = 577594;

        /// <summary>Client text 557382 "Thank you for becoming a Premium Member!", shown when a membership starts [I use].</summary>
        private const uint BecameMemberText = 557382;

        /// <summary>How often the online players' memberships are checked for their end.</summary>
        private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(30);

        /// <summary>The type each online client was last given (SendSelf or 0x66).</summary>
        private static readonly ConcurrentDictionary<WorldSession, byte> told = new();
        private static DateTime lastCheck = DateTime.MinValue;

        private static WorldServerConfiguration Config => ConfigurationManager<WorldServerConfiguration>.Instance.Config;

        /// <summary>Start-up: adds the /membership command (sent to every client at zone-in by DynamicCommands.SendAll).</summary>
        public static void Initialise() => DynamicCommands.Register(CommandName, world: false, RunCommand);

        // ---- The store's hook ------------------------------------------------------------------------------------------

        /// <summary>
        /// What membership costs and how long one purchase lasts (WorldServer.json MembershipPriceLumens / MembershipDays).
        /// A price of 0 means it is not for sale. For the Showcase store to list it (trace/0695/agent-membership.md §5).
        /// </summary>
        public static (uint PriceLumens, uint Days) Offer => (Config?.MembershipPriceLumens ?? 0, Config?.MembershipDays ?? 0);

        /// <summary>
        /// Buys <see cref="Offer"/>'s days of membership for its price in Lumens (item 1579, <see cref="WorldSession.Coins"/>).
        /// Runs on the world thread. Days are added after the current membership (a trial becomes a membership and keeps
        /// its days [I]). The client is told at once (0x66 when the type changes) and a line in chat says until when.
        /// Returns whether it was bought and a message for the player.
        /// </summary>
        public static (bool Ok, string Message) Purchase(WorldSession session)
        {
            (uint price, uint days) = Offer;
            if (price == 0 || days == 0)
                return (false, "Membership is not for sale on this server.");
            if (session?.Account == null)
                return (false, "Your account could not be found.");
            if (session.Coins < price || !session.ChangeCoins(-price))
                return (false, $"Membership costs {price:N0} Lumens; you have {session.Coins:N0}.");
            DateTime until = Extend(session, Member, days);
            log.Info($"{session.CharacterName} (account {session.Account.Id}) bought {days} days of membership for {price} Lumens, until {until:u}.");
            return (true, $"{days} days of membership for {price:N0} Lumens: you are a member until {Format(until)}.");
        }

        // ---- Member benefits (user decision 2026-10-17: queue priority, a monthly Lumen stipend, store discounts; ------
        // ---- 2026-10-18: stipend and discount default 0 = live, which had no Landmark member perks; owner settings) ---

        /// <summary>How often a member account gets <see cref="WorldServerConfiguration.MemberMonthlyLumens"/>.</summary>
        private static readonly TimeSpan StipendEvery = TimeSpan.FromDays(30);

        /// <summary>
        /// A Lumen store price for this player: <see cref="WorldServerConfiguration.MemberStoreDiscountPercent"/> off for
        /// members and trials, rounded up to whole 5 Lumens like the store's own prices (never below 5 for a priced item).
        /// </summary>
        /// <summary>The member store discount in percent (0-100).</summary>
        public static uint DiscountPercent => Math.Min(Config?.MemberStoreDiscountPercent ?? 0, 100u);

        public static uint PriceFor(WorldSession session, uint price)
        {
            uint percent = Math.Min(Config?.MemberStoreDiscountPercent ?? 0, 100u);
            if (price == 0 || percent == 0 || !IsMember(session))
                return price;
            uint discounted = (uint)Math.Ceiling(price * (100 - percent) / 100.0 / 5.0) * 5;
            return Math.Max(5u, discounted);
        }

        /// <summary>
        /// The monthly stipend: a member account (type 2, not a trial) that hasn't had it for 30 days gets
        /// <see cref="WorldServerConfiguration.MemberMonthlyLumens"/> Lumens on whichever character is online. The claim is
        /// one atomic update in the auth database (AuthDatabase.ClaimMemberStipend), so it is paid once per account even
        /// with several worlds. Called at world entry and on the membership check.
        /// </summary>
        private static void PayStipend(WorldSession session)
        {
            uint lumens = Config?.MemberMonthlyLumens ?? 0;
            if (lumens == 0 || session?.Account == null || TypeOf(session) != Member)
                return;
            stipendChecked[session] = DateTime.UtcNow;
            ulong accountId = session.Account.Id;
            // The claim runs off the world thread; the Lumens are added back on it (Update drains worldWork).
            DatabaseManager.Instance.AuthDatabase.ClaimMemberStipend(accountId, StipendEvery).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    log.Warn(t.Exception?.GetBaseException(), $"Account {accountId}: member stipend not checked; trying again later.");
                    return;
                }
                if (!t.Result)
                    return;
                worldWork.Enqueue(() =>
                {
                    if (session.Disconnected)
                    {
                        log.Warn($"Account {accountId}: stipend claimed but the player left; it is paid at the next claim period.");
                        return;
                    }
                    session.ChangeCoins(lumens);
                    Chat.System(session, $"Membership: {lumens:N0} Lumens for this month have been added.");
                    log.Info($"{session.CharacterName} (account {accountId}): member stipend of {lumens} Lumens.");
                });
            });
        }

        /// <summary>World-thread work from finished database calls (the stipend's Lumens).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> worldWork = new();
        /// <summary>When each online member's stipend was last checked (rechecked hourly while online).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<WorldSession, DateTime> stipendChecked = new();
        private static readonly TimeSpan StipendRecheck = TimeSpan.FromHours(1);

        /// <summary>The membership type in force for this player's account (0 none, 1 trial, 2 member).</summary>
        public static byte TypeOf(WorldSession session) => session?.Account?.MemberTypeAt(DateTime.UtcNow) ?? None;

        /// <summary>Has this player's account a membership (trial included) now?</summary>
        public static bool IsMember(WorldSession session) => TypeOf(session) != None;

        // ---- Session -------------------------------------------------------------------------------------------------

        /// <summary>The value for SendSelfToClient's membership type (player +0xfc); remembered as what the client knows.</summary>
        public static uint SendSelfType(WorldSession session)
        {
            byte type = TypeOf(session);
            told[session] = type;
            return type;
        }

        /// <summary>
        /// World entry: an account that hasn't had its trial key gets the letter with the key (once per account, whichever
        /// world or character comes first; while TrialKeys is on).
        /// </summary>
        public static void Enter(WorldSession session)
        {
            told.TryAdd(session, TypeOf(session));
            PayStipend(session);
            AccountModel account = session.Account;
            if (Config?.TrialKeys != true || account == null || account.TrialKeyMailed)
                return;
            bool mine;
            try
            {
                var marking = DatabaseManager.Instance.AuthDatabase.MarkTrialKeyMailed(account.Id);
                mine = marking.Wait(TimeSpan.FromSeconds(3)) && marking.Result;
            }
            catch (Exception e)
            {
                log.Warn(e, $"Account {account.Id}: could not mark the trial key as mailed; trying again at the next login.");
                return;
            }
            account.TrialKeyMailed = true;
            if (!mine)
                return;
            string subject = Shared.GameTable.Text.TextManager.Instance.GetTextForId(LetterSubjectText) is string s && s.Length > 0
                ? s : "Invite your friends!";
            Social.SocialServices.Service.SendSystemMail(session.PlayerGuid, LetterFrom, subject, LetterBody(), 0,
                new[] { (Inventory.ItemDefinitionBuilder.TrialKey, 1u) });
            log.Info($"{session.CharacterName} (account {account.Id}): trial key letter sent.");
        }

        /// <summary>
        /// The trial key letter. Live's text (577595) sent players to the website with a code; this one says how our key
        /// works, in live's words where they still fit.
        /// </summary>
        private static string LetterBody()
        {
            uint days = Config?.TrialKeyDays ?? 10;
            return "Greetings, Luminary!\n\n"
                + "Do you know anyone who'd like to give Landmark a try? Share the fun and wonder of Landmark with your "
                + $"friends with a free {days}-day membership trial!\n\n"
                + "The key attached to this letter is for a friend: take it, then mail it to them (Inbox, Compose, drag the "
                + "key onto an attachment slot). A key can be mailed once. Your friend uses it from their bag and their "
                + $"account becomes a member for {days} days. Every account can use one trial key.\n\n"
                + "Happy building!";
        }

        /// <summary>
        /// World tick: every <see cref="CheckEvery"/>, a client whose membership has changed by itself (ended) is told
        /// (0x66 with the new type) and the player gets a line in chat.
        /// </summary>
        public static void Update()
        {
            while (worldWork.TryDequeue(out Action work))
                work();
            if (DateTime.UtcNow - lastCheck < CheckEvery)
                return;
            lastCheck = DateTime.UtcNow;
            foreach (WorldSession gone in told.Keys.Where(s => s.Disconnected).ToList())
            {
                told.TryRemove(gone, out _);
                stipendChecked.TryRemove(gone, out _);
            }
            foreach (WorldSession session in PlayerManager.Snapshot())
            {
                if (!stipendChecked.TryGetValue(session, out DateTime checkedAt) || DateTime.UtcNow - checkedAt >= StipendRecheck)
                    PayStipend(session);
                if (!told.TryGetValue(session, out byte known))
                    continue;
                byte now = TypeOf(session);
                if (now == known)
                    continue;
                Tell(session, now);
                if (now == None)
                {
                    Chat.SystemChannel(session, "Your membership has ended.");
                    log.Info($"{session.CharacterName}: membership ended.");
                }
            }
        }

        // ---- Changes ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Adds days of membership of a type to the player's account (after the current membership, if any), stores it,
        /// and tells the client when its type changed. Returns the new end (UTC).
        /// </summary>
        private static DateTime Extend(WorldSession session, byte type, uint days)
        {
            AccountModel account = session.Account;
            DateTime now = DateTime.UtcNow;
            byte before = account.MemberTypeAt(now);
            DateTime from = before != None && account.MemberUntil is DateTime end && end > now ? end : now;
            account.MemberType = Math.Max(before, type);
            account.MemberUntil = from.AddDays(days);
            Store(account);
            if (TypeOf(session) != (told.TryGetValue(session, out byte known) ? known : None))
            {
                Tell(session, TypeOf(session));
                if (before == None)
                    SystemMessages.Show(session, BecameMemberText);
            }
            return account.MemberUntil.Value;
        }

        /// <summary>Ends the account's membership now (admin), telling the client.</summary>
        private static void End(WorldSession session)
        {
            AccountModel account = session.Account;
            account.MemberType = None;
            account.MemberUntil = null;
            Store(account);
            Tell(session, None);
        }

        /// <summary>Writes the online copy's membership to the auth database (in the background; failures are logged).</summary>
        private static void Store(AccountModel account)
        {
            ulong id = account.Id;
            byte type = account.MemberType;
            DateTime? until = account.MemberUntil;
            bool redeemed = account.TrialRedeemed;
            DatabaseManager.Instance.AuthDatabase.UpdateMembership(id, type, until, redeemed).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    log.Error(t.Exception, $"Account {id}: membership not saved (type {type}, until {until:u}).");
            });
        }

        /// <summary>
        /// PacketMembershipActivation 0x66 {u32 type} [P layout: reader FUN_142c4a7c0 reads u16 + u32 and accepts the
        /// packet only when nothing is left]. The client stores the type, flags its own entity and refreshes its activity
        /// list (see the class remarks). The players in the same landscape get PlayerUpdate 0x35 MemberStatus for the
        /// player's entity (<see cref="MemberStatus"/>), as they got the flag in AddLightweightPc when it was introduced.
        /// </summary>
        private static void Tell(WorldSession session, byte type)
        {
            bool wasMember = told.TryGetValue(session, out byte known) && known != None;
            told[session] = type;
            session.EnqueueMessage(ZoneMessageOpcode.ClientMembershipActivation, BitConverter.GetBytes((uint)type));
            // The member store discount may change this player's prices: the client's store products again, only if they
            // differ from what it has (StoreBundles.Refresh).
            StoreBundles.Refresh(session);
            if (wasMember == (type != None))
                return;
            byte[] status = MemberStatus(session.PlayerGuid, type != None);
            foreach (WorldSession viewer in PlayerManager.InZone(session.ZoneId))
                if (viewer != session)
                    viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateMemberStatusWire, status);
        }

        /// <summary>
        /// PlayerUpdate sub 0x35 MemberStatus body [P: 0.695 dispatcher FUN_142cdfa60 case 0x34, reader FUN_142c4f210 →
        /// FUN_142c25ad0 (u16, u32 sub, u64 guid +0x18, bool +0x20; the packet must be consumed exactly)]: u64 entity guid,
        /// bool member. The client finds the entity (FUN_142f85390) and sets its member bit (FUN_142ec2ff0, effect flag 1:
        /// the effect id would come from the hash 0x782d2049, which the 0.695 data doesn't define, so nothing plays).
        /// </summary>
        public static byte[] MemberStatus(ulong guid, bool member)
        {
            var body = new byte[9];
            BitConverter.TryWriteBytes(body.AsSpan(0), guid);
            body[8] = member ? (byte)1 : (byte)0;
            return body;
        }

        // ---- Trial keys ------------------------------------------------------------------------------------------------

        /// <summary>Is this item one of the trial keys (the sender's copy or the mailed copy)?</summary>
        public static bool IsTrialKey(uint itemDefinition)
            => itemDefinition == Inventory.ItemDefinitionBuilder.TrialKey || itemDefinition == Inventory.ItemDefinitionBuilder.TrialKeyMailed;

        /// <summary>What a mailed item arrives as: the trial key becomes its NO_TRADE copy; everything else is unchanged.</summary>
        public static uint AsMailed(uint itemDefinition)
            => itemDefinition == Inventory.ItemDefinitionBuilder.TrialKey ? Inventory.ItemDefinitionBuilder.TrialKeyMailed : itemDefinition;

        /// <summary>
        /// A compose with a trial key attached may not go to a character of the sender's own account: refused with mail
        /// result 2 (the client's "can't send an attachment to yourself" [I: the nearest result the client has]).
        /// Mail.Success otherwise. (The mailed copy is NO_TRADE, so Mail.CheckBag already refuses to send it again.)
        /// </summary>
        public static uint CheckTrialKeyMail(WorldSession session, IEnumerable<ulong> recipientAccounts,
            IEnumerable<(uint Definition, uint Count)> items)
        {
            if (session?.Account == null || !items.Any(i => i.Definition == Inventory.ItemDefinitionBuilder.TrialKey))
                return Mail.Success;
            if (recipientAccounts.Any(a => a == session.Account.Id))
            {
                Chat.SystemChannel(session, "A trial key is for a friend: it can't be mailed to your own characters.");
                return Mail.SelfWithAttachment;
            }
            return Mail.Success;
        }

        /// <summary><see cref="CheckTrialKeyMail(WorldSession, IEnumerable{ulong}, IEnumerable{ValueTuple{uint, uint}})"/> by recipient name (the remote mail path); names this world's character database doesn't know pass.</summary>
        public static uint CheckTrialKeyMail(WorldSession session, IEnumerable<string> recipientNames,
            IEnumerable<(uint Definition, uint Count)> items)
        {
            if (session?.Account == null || !items.Any(i => i.Definition == Inventory.ItemDefinitionBuilder.TrialKey))
                return Mail.Success;
            var accounts = new List<ulong>();
            foreach (string name in recipientNames.Distinct(StringComparer.OrdinalIgnoreCase))
                if (DatabaseManager.Instance.CharacterDatabase.FindCharacterByName(name.Trim()) is CharacterModel c)
                    accounts.Add(c.AccountId);
            return CheckTrialKeyMail(session, accounts, items);
        }

        /// <summary>
        /// A trial key used from the bag (ItemUses → ActivateItem 0x2D sub 0x15). The sender's copy only explains itself.
        /// The mailed copy gives the account <see cref="WorldServerConfiguration.TrialKeyDays"/> days of trial membership
        /// and is used up — unless trial keys are off, the account has used one before (live: "a one-time free trial"), or
        /// it is a member already (the trial is for players without one [I]); then the key stays in the bag.
        /// </summary>
        public static void UseTrialKey(WorldSession session, CharacterItemModel item)
        {
            uint days = Config?.TrialKeyDays ?? 10;
            if (item.ItemDefinitionId == Inventory.ItemDefinitionBuilder.TrialKey)
            {
                Chat.SystemChannel(session, $"This trial key is for a friend: mail it to them and they can use it for {days} days of membership.");
                return;
            }
            AccountModel account = session.Account;
            if (Config?.TrialKeys != true || days == 0)
                Chat.SystemChannel(session, "Trial keys are switched off on this server.");
            else if (account == null)
                return;
            else if (account.TrialRedeemed)
                Chat.SystemChannel(session, "Your account has already used a trial key.");
            else if (TypeOf(session) != None)
                Chat.SystemChannel(session, "You are a member already; keep the key until your membership has ended.");
            else if (session.TakeFromStack(item.Id, 1) != null)
            {
                account.TrialRedeemed = true;
                DateTime until = Extend(session, Trial, days);
                Chat.SystemChannel(session, $"Trial key used: you are a member until {Format(until)}.");
                log.Info($"{session.CharacterName} (account {account.Id}) used a trial key: trial membership until {until:u}.");
            }
        }

        // ---- /membership ------------------------------------------------------------------------------------------------

        private static string Format(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm") + " UTC";

        /// <summary>
        /// /membership: the player's membership and the offer; "buy" buys it (<see cref="Purchase"/>). Admins (WorldServer
        /// "Admins"): "grant name days" adds days of membership to an online player's account, "revoke name" ends it.
        /// </summary>
        private static void RunCommand(WorldSession session, string args)
        {
            string[] words = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string verb = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            switch (verb)
            {
                case "buy":
                    Chat.SystemChannel(session, Purchase(session).Message);
                    return;
                case "grant" when Admin.IsAdmin(session) && words.Length >= 3 && uint.TryParse(words[2], out uint days) && days > 0:
                    if (Online(session, words[1]) is WorldSession target && target.Account != null)
                    {
                        DateTime until = Extend(target, Member, days);
                        log.Info($"{session.CharacterName} granted {target.CharacterName} {days} days of membership (until {until:u}).");
                        Chat.SystemChannel(session, $"{target.CharacterName} is a member until {Format(until)}.");
                    }
                    return;
                case "revoke" when Admin.IsAdmin(session) && words.Length >= 2:
                    if (Online(session, words[1]) is WorldSession who && who.Account != null)
                    {
                        End(who);
                        log.Info($"{session.CharacterName} ended {who.CharacterName}'s membership.");
                        Chat.SystemChannel(session, $"{who.CharacterName}'s membership has ended.");
                    }
                    return;
            }
            DateTime? end = session.Account?.MemberUntil;
            string state = TypeOf(session) switch
            {
                Trial => $"Trial membership until {Format(end.Value)}.",
                None => "You have no membership.",
                _ => $"Member until {Format(end.Value)}."
            };
            (uint price, uint offerDays) = Offer;
            string offer = price > 0 && offerDays > 0
                ? $" {offerDays} days of membership cost {price:N0} Lumens: /{CommandName} buy" : "";
            Chat.SystemChannel(session, state + offer);
        }

        private static WorldSession Online(WorldSession asking, string name)
        {
            WorldSession found = PlayerManager.Snapshot().FirstOrDefault(p => string.Equals(p.CharacterName, name, StringComparison.OrdinalIgnoreCase));
            if (found == null)
                Chat.SystemChannel(asking, $"{name} is not online in this world.");
            return found;
        }
    }
}
