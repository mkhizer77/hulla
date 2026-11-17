using Hulla.Application.Progress;
using Hulla.Application.Social;
using Hulla.Application.Table;
using Hulla.Application.ViewModels;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    /// <summary>The pieces every screen needs to treat Thulla and Rung alike (docs/design/two-games.md).</summary>
    public sealed class TwoGamesTests
    {
        [Test]
        public void AMetricNamesItsGameAndSharedOnesShowUnderEveryFilter()
        {
            Assert.AreEqual(GameFilter.Rung, GameFilters.OfMetric("rung_courts"));
            Assert.AreEqual(GameFilter.Thulla, GameFilters.OfMetric("thullas_given"));
            Assert.IsNull(GameFilters.OfMetric("rounds_played"));
            Assert.IsTrue(GameFilters.Shows(GameFilter.Rung, GameFilter.Rung));
            Assert.IsFalse(GameFilters.Shows(GameFilter.Rung, GameFilter.Thulla));
            Assert.IsTrue(GameFilters.Shows(GameFilter.Rung, null), "a mission for either game shows under both");
            Assert.IsTrue(GameFilters.Shows(GameFilter.All, GameFilter.Thulla));
            Assert.AreEqual("all", GameFilters.Wire(GameFilter.All));
            Assert.AreEqual("rung", GameFilters.Wire(GameFilter.Rung));
            Assert.AreEqual("thulla", GameFilters.Wire(GameFilter.Thulla));
        }

        [Test]
        public void MissionsAndAchievementsKnowTheirGame()
        {
            Assert.AreEqual(GameFilter.Rung, new MissionInfo("d_rung_piles_3", "rung_piles", 3, 0, false, false, 60, 0, true).Game);
            Assert.IsNull(new MissionInfo("w_rounds_15", "rounds_played", 15, 0, false, false, 250, 5, false).Game);
            Assert.AreEqual(GameFilter.Rung, new AchievementInfo("rung_goon_court", "skill", "rung_goon_courts", 1, 0, null, 20, null).Game);
            Assert.AreEqual(GameFilter.Thulla, new AchievementInfo("first_thulla", "play", "thullas_given", 1, 0, null, 5, null).Game);
        }

        [Test]
        public void TheRungCardShowsWinRatesAndTheFavouriteRung()
        {
            var card = ProfileViewModel.RungOf(new RungStats
            {
                Deals = 40, DealsWon = 23, Bazis = 6, BazisWon = 4, Courts = 3,
                CallerDeals = 22, CallerWins = 14, DefenderDeals = 18, DefenderWins = 9,
                Trumps = new RungTrumps { S = 2, H = 5, C = 5, D = 1 },
            });
            Assert.IsNotNull(card);
            Assert.AreEqual(67, card!.BaziWinPercent);
            Assert.AreEqual(58, card.DealWinPercent);
            Assert.AreEqual(64, card.CallerWinPercent);
            Assert.AreEqual(50, card.DefenderWinPercent);
            Assert.AreEqual('H', card.FavouriteRung, "hearts and clubs tie; hearts comes first");
            Assert.IsNull(ProfileViewModel.RungOf(null));
            Assert.AreEqual(0, RungCard.Percent(0, 0));
            Assert.IsNull(RungCard.Favourite(0, 0, 0, 0));
        }

        [Test]
        public void TheHostSwapsTwoSeats()
        {
            var gateway = new FakeGateway();
            var client = new TableClient(gateway, new Services.TableTicket("m1", "ABC123", "t_ABC123"), Messages.Me, 100);
            client.SwapSeats(1, 3);
            var sent = gateway.Sent[gateway.Sent.Count - 1];
            Assert.AreEqual(MessageTypes.SwapSeats, sent.Type);
            Assert.AreEqual(1, ((SwapSeats)sent.Payload).A);
            Assert.AreEqual(3, ((SwapSeats)sent.Payload).B);
        }
    }
}
