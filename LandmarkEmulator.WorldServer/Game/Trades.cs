using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Player-to-player trade. TradeBase 0x3E, u8 sub (client dispatcher FUN_14358ee50; docs/protocol.md "Trade"):
    /// C→S 1 Initiate {u64 target}, 3 InviteReply {u32 trade, bool accept}, 6 Accept {u32 trade, u32 sequence, u32},
    /// 7 Cancel {u32 trade}, 8 OfferItem {u32 trade, u64 item, u32 quantity}, 9 CoinCount {u32 trade, u32 coins} (→ 10 UpdateCoinCount {u32 trade, u32 sequence, bool mine, u32 coins});
    /// S→C 2 Invite, 4 StartSession, 5 EndSession, 10 UpdateCoinCount, 11 UpdateAddItem, 12 UpdateItemCount,
    /// 13 UpdateAccept, 14 Message, 15 Rejected.
    /// Each side's offer is shown on both screens ("mine" true for the offerer). Any change clears both accepts; when
    /// both have accepted the server re-checks the offers and moves the items.
    /// </summary>
    public static class Trades
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte InitiateSub = 1, InviteSub = 2, InviteReplySub = 3, StartSessionSub = 4, EndSessionSub = 5,
            AcceptSub = 6, CancelSub = 7, OfferItemSub = 8, CoinCountSub = 9, UpdateCoinCountSub = 10,
            UpdateAddItemSub = 11, UpdateItemCountSub = 12, UpdateAcceptSub = 13, MessageSub = 14, RejectedSub = 15;
        /// <summary>TradeMessage codes the client explains.</summary>
        /// <summary>
        /// TradeMessage codes the client explains (FUN_14358ee50 case 0xE): 1 TradeCanceled, 2 ...PartnerDisconnected,
        /// 3 / 4 partner's / your offer invalid, 6 TradeCompleted, 7 TradeChangedAfterAccept, 8 "TradeCanceledYouEntered
        /// InstancedZone", 9 "...PartnerEnteredInstancedZone" (both close the window), 10 TradeCannotAcceptFullInventory.
        /// </summary>
        private const byte Canceled = 1, PartnerDisconnected = 2, PartnerOfferInvalid = 3, YourOfferInvalid = 4,
            Completed = 6, ChangedAfterAccept = 7, YouEnteredInstance = 8, PartnerEnteredInstance = 9, InventoryFull = 10;

        private class Side
        {
            public WorldSession Session;
            /// <summary>Offered items: guid → quantity.</summary>
            public readonly Dictionary<ulong, uint> Items = new();
            /// <summary>Offered coins.</summary>
            public uint Coins;
            public bool Accepted;
        }

        private class Trade
        {
            public int Id;
            public Side A, B;
            public bool Started;
            public int Sequence;
            public Side Of(WorldSession s) => A.Session == s ? A : B.Session == s ? B : null;
            public Side Other(Side s) => s == A ? B : A;
        }

        private static readonly object sync = new();
        private static readonly Dictionary<int, Trade> trades = new();
        private static int nextId;

        public static void Handle(WorldSession session, byte sub, byte[] body)
        {
            var r = new GamePacketReader(body);
            try
            {
                switch (sub)
                {
                    case InitiateSub: Initiate(session, r.ReadULong()); break;
                    case InviteReplySub: { int id = (int)r.ReadUInt(); Reply(session, id, r.ReadBool()); break; }
                    case AcceptSub: Accept(session, (int)r.ReadUInt()); break;
                    case CancelSub: Cancel(session, (int)r.ReadUInt()); break;
                    case OfferItemSub: { int id = (int)r.ReadUInt(); ulong item = r.ReadULong(); Offer(session, id, item, r.ReadUInt()); break; }
                    case CoinCountSub: { int id = (int)r.ReadUInt(); Coins(session, id, r.ReadUInt()); break; }
                    default: log.Debug($"Trade packet {sub} from {session.CharacterName}: {BitConverter.ToString(body)}"); break;
                }
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable trade packet {sub} from {session.CharacterName}: {e.Message}");
            }
        }

        /// <summary>Initiate (Lua InitiateTradeByGuid): invite the target if both are free and on the same landscape.</summary>
        private static void Initiate(WorldSession session, ulong target)
        {
            WorldSession other = PlayerManager.FindByGuid(target);
            if (Ignores.Refuses(other, session))
                return;
            lock (sync)
            {
                if (other == null || other == session || other.ZoneId != session.ZoneId
                    || trades.Values.Any(t => t.A.Session == session || t.B.Session == session
                        || t.A.Session == other || t.B.Session == other))
                {
                    log.Info($"{session.CharacterName} can't trade with 0x{target:X} now.");
                    return;
                }
                var trade = new Trade { Id = ++nextId, A = new Side { Session = session }, B = new Side { Session = other } };
                trades[trade.Id] = trade;
                Send(other, InviteSub, w => { Identity(w, session); w.Write((uint)trade.Id); });
                log.Info($"{session.CharacterName} invited {other.CharacterName} to trade ({trade.Id}).");
            }
        }

        private static void Reply(WorldSession session, int id, bool accept)
        {
            Trade trade;
            lock (sync)
            {
                if (!trades.TryGetValue(id, out trade) || trade.B.Session != session || trade.Started)
                    return;
                if (!accept)
                    trades.Remove(id);
                else
                    trade.Started = true;
            }
            if (!accept)
            {
                Send(trade.A.Session, RejectedSub, w => Identity(w, session));
                return;
            }
            foreach ((Side me, Side other) in new[] { (trade.A, trade.B), (trade.B, trade.A) })
                Send(me.Session, StartSessionSub, w => { w.Write(other.Session.PlayerGuid); Identity(w, other.Session); w.Write((uint)id); });
            log.Info($"Trade {id} started: {trade.A.Session.CharacterName} ↔ {trade.B.Session.CharacterName}.");
        }

        /// <summary>OfferItem: set how many of a bag item this side offers (0 takes it back).</summary>
        private static void Offer(WorldSession session, int id, ulong itemGuid, uint quantity)
        {
            Trade trade; Side me;
            lock (sync)
            {
                if (!Active(session, id, out trade, out me))
                    return;
                CharacterItemModel item = session.Inventory.Get(itemGuid);
                // NO_TRADE items (Lumens, site flags, store expansions, titles...) stay with their owner.
                if (item == null || item.Container != Inventory.Inventory.BagContainer || Chests.HasItems(itemGuid)
                    || Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is { NoTrade: true })
                    return;
                quantity = Math.Min(quantity, item.Count);
                bool known = me.Items.ContainsKey(itemGuid);
                if (quantity == 0)
                    me.Items.Remove(itemGuid);
                else
                    me.Items[itemGuid] = quantity;
                Changed(trade);
                int seq = trade.Sequence;
                foreach (Side viewer in new[] { me, trade.Other(me) })
                {
                    bool mine = viewer == me;
                    if (known || quantity == 0)
                        Send(viewer.Session, UpdateItemCountSub, w =>
                        {
                            w.Write((uint)id); w.Write((uint)seq); w.Write(mine);
                            w.Write(itemGuid); w.Write(quantity); w.Write(false);
                        });
                    else
                    {
                        var record = Inventory.Inventory.ToPacket(item);
                        record.Count = quantity;
                        Send(viewer.Session, UpdateAddItemSub, w =>
                        {
                            w.Write((uint)id); w.Write((uint)seq); w.Write(mine);
                            record.WriteRecord(w); w.Write(false);
                        });
                    }
                }
            }
        }

        /// <summary>CoinCount: the coins a side offers (at most its balance); shown on both screens.</summary>
        private static void Coins(WorldSession session, int id, uint coins)
        {
            Trade trade; Side me; int seq;
            lock (sync)
            {
                if (!Active(session, id, out trade, out me))
                    return;
                if (coins > session.Coins)
                {
                    Send(session, MessageSub, w => w.Write(YourOfferInvalid));
                    coins = me.Coins;   // keep (and re-show) the previous offer
                }
                else if (coins != me.Coins)
                {
                    me.Coins = coins;
                    Changed(trade);
                }
                seq = trade.Sequence;
            }
            foreach (Side viewer in new[] { me, trade.Other(me) })
                Send(viewer.Session, UpdateCoinCountSub, w =>
                {
                    w.Write((uint)id); w.Write((uint)seq); w.Write(viewer == me); w.Write(coins);
                });
        }

        private static void Accept(WorldSession session, int id)
        {
            Trade trade; Side me; bool both;
            lock (sync)
            {
                if (!Active(session, id, out trade, out me))
                    return;
                me.Accepted = true;
                both = trade.Other(me).Accepted;
            }
            Send(session, UpdateAcceptSub, w => { w.Write((uint)id); w.Write(false); });
            Send(trade.Other(me).Session, UpdateAcceptSub, w => { w.Write((uint)id); w.Write(true); });
            if (both)
                Complete(trade);
        }

        /// <summary>Both accepted: check the offers still exist and fit, then swap them.</summary>
        private static void Complete(Trade trade)
        {
            lock (sync)
                trades.Remove(trade.Id);
            foreach (Side side in new[] { trade.A, trade.B })
            {
                bool valid = side.Coins <= side.Session.Coins && side.Items.All(o => side.Session.Inventory.Get(o.Key) is { } i
                    && i.Container == Inventory.Inventory.BagContainer && i.Count >= o.Value);
                if (!valid)
                {
                    Send(side.Session, MessageSub, w => w.Write(YourOfferInvalid));
                    Send(trade.Other(side).Session, MessageSub, w => w.Write(PartnerOfferInvalid));
                    End(trade);
                    log.Info($"Trade {trade.Id} failed: {side.Session.CharacterName}'s offer is no longer there.");
                    return;
                }
                Side receiver = trade.Other(side);
                var incoming = side.Items.Select(o => (side.Session.Inventory.Get(o.Key).ItemDefinitionId, o.Value)).ToList();
                if (!receiver.Session.Inventory.CanReceive(incoming, receiver.Items))
                {
                    Send(receiver.Session, MessageSub, w => w.Write(InventoryFull));
                    Send(side.Session, MessageSub, w => w.Write(Canceled));
                    End(trade);
                    log.Info($"Trade {trade.Id} failed: {receiver.Session.CharacterName}'s bag has no room.");
                    return;
                }
            }
            // Take both offers first, then give, so neither side's incoming items fill the slots of its outgoing ones.
            var moving = new List<(WorldSession To, uint Definition, uint Count)>();
            var designs = new List<(WorldSession To, ulong OldItem)>();
            foreach (Side side in new[] { trade.A, trade.B })
                foreach ((ulong guid, uint count) in side.Items)
                {
                    uint definition = side.Session.Inventory.Get(guid).ItemDefinitionId;
                    side.Session.TakeFromStack(guid, count);
                    if (definition == DesignItems.PlayerDesignItem)
                        designs.Add((trade.Other(side).Session, guid));
                    else
                        moving.Add((trade.Other(side).Session, definition, count));
                }
            foreach ((WorldSession to, uint definition, uint count) in moving)
                to.GiveItem(definition, count);
            // A Player Design item is one design each: a new single item, with the design moved onto it.
            foreach ((WorldSession to, ulong oldItem) in designs)
                if (to.Inventory.AddSingle(DesignItems.PlayerDesignItem) is { } single)
                {
                    to.SendItem(single, replace: false);
                    DesignItems.Moved(oldItem, single.Id);
                }
            if (designs.Count > 0)
                foreach (Side side in new[] { trade.A, trade.B })
                    Achievements.DesignTraded(side.Session);
            foreach (Side side in new[] { trade.A, trade.B })
                if (side.Coins > 0 && side.Session.ChangeCoins(-side.Coins))
                    trade.Other(side).Session.ChangeCoins(side.Coins);
            foreach (Side side in new[] { trade.A, trade.B })
            {
                Send(side.Session, MessageSub, w => w.Write(Completed));
                if (trade.A.Items.Count + trade.B.Items.Count > 0)
                    Achievements.Traded(side.Session);
            }
            End(trade);
            log.Info($"Trade {trade.Id} completed: {trade.A.Session.CharacterName} gave {trade.A.Items.Count} stack(s), "
                + $"{trade.B.Session.CharacterName} gave {trade.B.Items.Count} (coins {trade.A.Coins} / {trade.B.Coins}).");
        }

        private static void Cancel(WorldSession session, int id)
        {
            Trade trade;
            lock (sync)
            {
                if (!trades.TryGetValue(id, out trade) || trade.Of(session) == null)
                    return;
                trades.Remove(id);
            }
            foreach (Side side in new[] { trade.A, trade.B })
                Send(side.Session, MessageSub, w => w.Write(Canceled));
            End(trade);
        }

        /// <summary>A player left: their partner is told and the trade ends.</summary>
        public static void Forget(WorldSession session)
        {
            List<Trade> gone;
            lock (sync)
            {
                gone = trades.Values.Where(t => t.Of(session) != null).ToList();
                foreach (Trade t in gone)
                    trades.Remove(t.Id);
            }
            foreach (Trade trade in gone)
            {
                WorldSession other = trade.Other(trade.Of(session)).Session;
                Send(other, MessageSub, w => w.Write(PartnerDisconnected));
                Send(other, EndSessionSub, w => w.Write((uint)trade.Id));
            }
        }

        /// <summary>
        /// A trader moved away from the trade: to another landscape (both see TradeCanceled) or into a running instanced
        /// game (codes 8 / 9, "...you / your partner entered an instanced zone"). Trades need both on one landscape.
        /// </summary>
        public static void Moved(WorldSession session, bool intoGame)
        {
            List<Trade> gone;
            lock (sync)
            {
                gone = trades.Values.Where(t => t.Of(session) != null).ToList();
                foreach (Trade t in gone)
                    trades.Remove(t.Id);
            }
            foreach (Trade trade in gone)
            {
                WorldSession other = trade.Other(trade.Of(session)).Session;
                Send(session, MessageSub, w => w.Write(intoGame ? YouEnteredInstance : Canceled));
                Send(other, MessageSub, w => w.Write(intoGame ? PartnerEnteredInstance : Canceled));
                End(trade);
                log.Info($"Trade {trade.Id} canceled: {session.CharacterName} {(intoGame ? "entered a game" : "changed landscape")}.");
            }
        }

        private static void End(Trade trade)
        {
            foreach (Side side in new[] { trade.A, trade.B })
                Send(side.Session, EndSessionSub, w => w.Write((uint)trade.Id));
        }

        /// <summary>A change to an offer: bump the sequence and clear accepts (telling both sides if one had accepted).</summary>
        private static void Changed(Trade trade)
        {
            trade.Sequence++;
            if (trade.A.Accepted || trade.B.Accepted)
            {
                trade.A.Accepted = trade.B.Accepted = false;
                foreach (Side side in new[] { trade.A, trade.B })
                    Send(side.Session, MessageSub, w => w.Write(ChangedAfterAccept));
            }
        }

        private static bool Active(WorldSession session, int id, out Trade trade, out Side me)
        {
            me = null;
            if (!trades.TryGetValue(id, out trade) || !trade.Started)
                return false;
            me = trade.Of(session);
            return me != null;
        }

        /// <summary>Character identity as the client reads it (FUN_142b8c3c0): u32 ×3, first name, last name.</summary>
        private static void Identity(GamePacketWriter w, WorldSession s)
            => new SendSelfToClient.CharacterIdentity { FirstName = s.CharacterName }.Write(w);

        private static void Send(WorldSession session, byte sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(sub);
            body(writer);
            session.EnqueueMessage(ZoneMessageOpcode.TradeBase, output.ToArray());
        }
    }
}
