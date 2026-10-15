using Hulla.Infrastructure.Content;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Tests
{
    /// <summary>Seats show the avatar set (GDD 15.6) and the player's own seat is named and tagged "You".</summary>
    public sealed class SeatBadgeTests
    {
        [OneTimeSetUp]
        public void LoadThePacks() => AvatarArt.Packs ??= new AddressablePacks();

        [Test]
        public void EveryAvatarInTheSetHasAFace()
        {
            foreach (var id in new[] { "a01", "a02", "a03", "a04", "a05", "a06", "a07", "a08" })
            {
                Assert.IsNotNull(AvatarArt.Find(id), id);
            }

            Assert.IsNull(AvatarArt.Find("zzz"));
            Assert.IsNull(AvatarArt.Find(null));
        }

        [Test]
        public void AnOpponentShowsTheirFaceAndNoYouTag()
        {
            var badge = new SeatBadge();
            badge.Show("Sara", 13, false, "", avatar: "a02");
            Assert.AreEqual("Sara", badge.Name);
            Assert.IsTrue(badge.HasImage);
            Assert.IsFalse(badge.ShowsYou);
        }

        [Test]
        public void AnUnknownAvatarFallsBackToTheTintedInitial()
        {
            var badge = new SeatBadge();
            badge.Show("Sara", 13, false, "", avatar: "a02");
            badge.Show("Sara", 13, false, "", avatar: "not_an_avatar");
            Assert.IsFalse(badge.HasImage);
            var avatar = badge.Q<VisualElement>(className: "seat__avatar");
            Assert.IsTrue(avatar.ClassListContains(AvatarTint.ClassFor("Sara")));
            Assert.AreEqual(DisplayStyle.Flex, badge.Q<Label>(className: "seat__initial").style.display.value);
        }

        [Test]
        public void TheLobbyShowsThePlayersOwnNameWithAYouTag()
        {
            var badge = new SeatBadge();
            badge.ShowLobby("Khizer", host: true, ready: false, avatar: "a05", me: true);
            Assert.AreEqual("Khizer", badge.Name);
            Assert.IsTrue(badge.ShowsYou);
            Assert.AreEqual(UiText.You, badge.Q<Label>(className: "seat__you").text);
            Assert.IsTrue(badge.ClassListContains("seat--me"));
            Assert.IsTrue(badge.HasImage);

            badge.ShowLobby(null, false, false);
            Assert.IsFalse(badge.ShowsYou, "an empty seat is nobody's");
            Assert.IsFalse(badge.HasImage);
        }

        [Test]
        public void ThePlayerBadgeNamesThePlayerAndShowsTheirTurnAndLeadOnTheFace()
        {
            var me = new PlayerBadge();
            me.Show("Khizer", "a07", me: true, turn: true, leader: true);
            Assert.AreEqual("Khizer", me.Name);
            Assert.IsTrue(me.ShowsYou);
            Assert.IsTrue(me.HasImage);
            Assert.IsTrue(me.ClassListContains("seat--turn"));
            Assert.IsTrue(me.ShowsCrown);
            me.ShowTimer(0.5f, 7f);
            Assert.IsTrue(me.ShowsTimer);

            me.Show("Khizer", null, me: true, turn: false);
            Assert.IsFalse(me.HasImage);
            Assert.IsFalse(me.ClassListContains("seat--turn"));
            Assert.IsFalse(me.ShowsCrown);
            Assert.IsFalse(me.ShowsTimer, "the ring goes with the turn");
        }
    }
}
