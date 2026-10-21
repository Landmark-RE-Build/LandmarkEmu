using LandmarkEmulator.WorldServer.Game;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Right-click menus (Game/RightClickMenus.cs, docs/request-variants.md): each RadialMenuRequest sender's request type
    /// answers with its own RadialMenuTypes menu — Friends list 2 → 4, party window 3 → 5, guild roster 4 → 6, chat tab
    /// 6 → 7 (senders FUN_1435e2d20 / FUN_1435f1880 / FUN_1435b98e0 / FUN_1435ae1d0) — and the rows listed per target.
    /// Before, the request type was used as the menu: the party window got the player menu, the guild roster nothing
    /// and a chat tab the guild member menu.
    /// </summary>
    public class RightClickMenuRuleTests
    {
        private static uint[] Ids(uint menu, RightClickMenus.Target target)
            => RightClickMenus.Buttons(menu, target).Select(b => b.Id).ToArray();

        private static readonly RightClickMenus.Target Stranger = new(Self: false, InMyParty: false, IAmLeader: false, Friend: false, Dead: false, Ignored: false);

        [Theory]
        [InlineData(RightClickMenus.RequestFriend, RightClickMenus.MenuFriend)]
        [InlineData(RightClickMenus.RequestParty, RightClickMenus.MenuParty)]
        [InlineData(RightClickMenus.RequestGuild, RightClickMenus.MenuGuild)]
        [InlineData(RightClickMenus.RequestChatTab, RightClickMenus.MenuChatTab)]
        [InlineData(5u, RightClickMenus.MenuNone)]
        [InlineData(7u, RightClickMenus.MenuNone)]
        public void EachSender_GetsItsOwnMenu(uint request, uint menu)
            => Assert.Equal(menu, RightClickMenus.MenuFor(request));

        [Fact]
        public void TheRequestValues_AreTheSendersOwn()
        {
            // ClientFriends 2, Groups 3, Guild 4, Chat 6 (the u32 each HandleRightClick native writes); world entities 1.
            Assert.Equal(new uint[] { 1, 2, 3, 4, 6 }, new[] { RightClickMenus.RequestWorld, RightClickMenus.RequestFriend,
                RightClickMenus.RequestParty, RightClickMenus.RequestGuild, RightClickMenus.RequestChatTab });
        }

        [Fact]
        public void AChatTab_OffersClose()
            => Assert.Equal(new uint[] { 67 }, Ids(RightClickMenus.MenuChatTab, default));

        [Fact]
        public void AStranger_InTheWorld_GetsInviteTradeFriendGuildInspect()
            => Assert.Equal(new uint[] { 21, 19, 15, 57, 75 }, Ids(RightClickMenus.MenuPlayer, Stranger));

        [Fact]
        public void ADeadFriend_InMyParty_WhileILead_GetsKickLeaderReviveButNoInviteOrAddFriend()
            => Assert.Equal(new uint[] { 19, 25, 27, 41, 57, 75 },
                Ids(RightClickMenus.MenuPlayer, Stranger with { InMyParty = true, IAmLeader = true, Friend = true, Dead = true }));

        [Fact]
        public void APartyMember_WhenIAmNotLeader_GetsNoPartyButtons()
            => Assert.Equal(new uint[] { 19, 15, 57, 75 }, Ids(RightClickMenus.MenuPlayer, Stranger with { InMyParty = true }));

        [Fact]
        public void AFriend_InTheFriendsList()
        {
            Assert.Equal(new uint[] { 24, 16, 23, 58, 65 }, Ids(RightClickMenus.MenuFriend, Stranger with { Friend = true }));
            Assert.Equal(new uint[] { 16, 23, 28, 30, 58, 65 },
                Ids(RightClickMenus.MenuFriend, Stranger with { Friend = true, InMyParty = true, IAmLeader = true }));
        }

        [Fact]
        public void ThePartyWindow_OnMyself_OffersLeave_AndDisbandToTheLeader()
        {
            var me = Stranger with { Self = true, InMyParty = true };
            Assert.Equal(new uint[] { 36 }, Ids(RightClickMenus.MenuParty, me));
            Assert.Equal(new uint[] { 36, 39 }, Ids(RightClickMenus.MenuParty, me with { IAmLeader = true }));
        }

        [Fact]
        public void ThePartyWindow_OnAnotherMember()
        {
            var member = Stranger with { InMyParty = true };
            Assert.Equal(new uint[] { 38 }, Ids(RightClickMenus.MenuParty, member));
            Assert.Equal(new uint[] { 35, 37, 38 }, Ids(RightClickMenus.MenuParty, member with { IAmLeader = true }));
            Assert.Equal(new uint[] { 35, 37 }, Ids(RightClickMenus.MenuParty, member with { IAmLeader = true, Friend = true }));
        }

        [Fact]
        public void TheGuildRoster_SwitchesIgnoreAndStopIgnoring()
        {
            Assert.Equal(new uint[] { 47, 52, 68, 48, 49, 42, 46, 44 }, Ids(RightClickMenus.MenuGuild, Stranger));
            Assert.Equal(new uint[] { 52, 68, 48, 50, 42, 46, 44 },
                Ids(RightClickMenus.MenuGuild, Stranger with { Ignored = true, InMyParty = true }));
        }

        [Fact]
        public void MenusAboutOtherPeople_ShowNothingForMyself()
        {
            var me = Stranger with { Self = true };
            Assert.Empty(Ids(RightClickMenus.MenuPlayer, me));
            Assert.Empty(Ids(RightClickMenus.MenuFriend, me));
            Assert.Empty(Ids(RightClickMenus.MenuGuild, me));
            Assert.Empty(Ids(RightClickMenus.MenuNone, Stranger));
        }

        [Fact]
        public void SortOrders_AreTheTablesButtonColumn()
        {
            // RadialMenuButtons.txt (0.695) BUTTON per row for the rows above.
            var order = RightClickMenus.Buttons(RightClickMenus.MenuGuild, Stranger).ToDictionary(b => b.Id, b => b.Order);
            Assert.Equal(0u, order[47]); Assert.Equal(1u, order[52]); Assert.Equal(2u, order[68]); Assert.Equal(3u, order[48]);
            Assert.Equal(4u, order[49]); Assert.Equal(6u, order[42]); Assert.Equal(7u, order[46]); Assert.Equal(8u, order[44]);
            var player = RightClickMenus.Buttons(RightClickMenus.MenuPlayer, Stranger with { Dead = true }).ToDictionary(b => b.Id, b => b.Order);
            Assert.Equal(5u, player[41]); Assert.Equal(7u, player[75]);
        }
    }
}
