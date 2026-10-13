using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using Hulla.Infrastructure.Nakama;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class RankedViewModelTests
    {
        private sealed class FakeRanked : IRankedService
        {
            public Result<RankedCard> Card = Result<RankedCard>.Fail("table.unavailable");
            public int BoardCalls;

            public Awaitable<Result<RankedCard>> CardAsync(CancellationToken cancellation)
            {
                var done = new AwaitableCompletionSource<Result<RankedCard>>();
                done.SetResult(Card);
                return done.Awaitable;
            }

            public Awaitable<Result<IReadOnlyList<RankedRow>>> BoardAsync(CancellationToken cancellation)
            {
                BoardCalls++;
                var done = new AwaitableCompletionSource<Result<IReadOnlyList<RankedRow>>>();
                done.SetResult(Result<IReadOnlyList<RankedRow>>.Ok(new List<RankedRow> { new RankedRow(1, "u1", "ayesha", 1600, "ustad") }));
                return done.Awaitable;
            }
        }

        private const string Body = @"{
            ""ok"": true,
            ""season"": { ""id"": ""s2"", ""number"": 2, ""startTs"": 0, ""endTs"": 5000 },
            ""rating"": 1287, ""league"": ""gold"", ""peakLeague"": ""gold"", ""rank"": 14,
            ""placement"": { ""played"": 10, ""of"": 10 }, ""protectedRounds"": 2, ""provisional"": false,
            ""leagues"": [
                { ""id"": ""bronze"", ""name"": ""league.bronze"", ""minRating"": 0, ""reward"": { ""title"": ""ranked_bronze"", ""coins"": 200 } },
                { ""id"": ""ustad"", ""name"": ""league.ustad"", ""minRating"": 1550, ""topOnly"": true,
                  ""reward"": { ""title"": ""ranked_ustad"", ""coins"": 1500, ""items"": [""back_ranked_ustad""], ""frame"": ""ranked_ustad"" } }
            ],
            ""matchmaking"": { ""band"": 150, ""widenBy"": 100, ""maxBand"": 600 },
            ""seats"": 4,
            ""lastSeason"": { ""seasonId"": ""s1"", ""league"": ""silver"", ""rating"": 1150, ""rank"": null, ""reward"": { ""title"": ""ranked_silver"" } },
            ""seasonSettled"": true
        }";

        [Test]
        public void TheCardReadsLeaguePlacementRewardsAndTheBand()
        {
            var card = NakamaRankedService.Parse(Body);
            Assert.AreEqual(2, card.SeasonNumber);
            Assert.AreEqual(1287, card.Rating);
            Assert.AreEqual("gold", card.League);
            Assert.AreEqual(14, card.Rank);
            Assert.IsTrue(card.Placed);
            Assert.AreEqual(2, card.ProtectedRounds);
            Assert.AreEqual((150, 100, 600), (card.Band, card.WidenBy, card.MaxBand));
            Assert.AreEqual(2, card.Leagues.Count);
            Assert.IsFalse(card.Leagues[0].RewardCardBack);
            Assert.IsTrue(card.Leagues[1].TopOnly);
            Assert.IsTrue(card.Leagues[1].RewardCardBack && card.Leagues[1].RewardFrame);
            Assert.AreEqual("silver", card.LastSeason!.League);
            Assert.IsNull(card.LastSeason.Rank);
            Assert.IsTrue(card.SeasonSettled);
        }

        [Test]
        public void DuringPlacementTheLeagueIsHidden()
        {
            var body = Body.Replace(@"""league"": ""gold""", @"""league"": null").Replace(@"""played"": 10", @"""played"": 3");
            var card = NakamaRankedService.Parse(body);
            Assert.IsNull(card.League);
            Assert.IsFalse(card.Placed);
        }

        [Test]
        public void RefreshLoadsTheCardThenTheTopPlaces()
        {
            var service = new FakeRanked { Card = Result<RankedCard>.Ok(NakamaRankedService.Parse(Body)) };
            var model = new RankedViewModel(service);
            _ = model.RefreshAsync(CancellationToken.None);
            Assert.IsFalse(model.Loading);
            Assert.AreEqual(1287, model.Card!.Rating);
            Assert.AreEqual("ayesha", model.Top[0].Name);
        }

        [Test]
        public void AFailedCardShowsTheErrorAndSkipsTheBoard()
        {
            var service = new FakeRanked();
            var model = new RankedViewModel(service);
            _ = model.RefreshAsync(CancellationToken.None);
            Assert.AreEqual("table.unavailable", model.ErrorCode);
            Assert.IsNull(model.Card);
            Assert.AreEqual(0, service.BoardCalls);
        }
    }
}
