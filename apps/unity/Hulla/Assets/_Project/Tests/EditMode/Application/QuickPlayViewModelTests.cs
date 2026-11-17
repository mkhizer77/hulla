using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class QuickPlayViewModelTests
    {
        private sealed class FakeMatchmaker : IMatchmaker
        {
            public readonly List<(int Seats, bool AnySize)> Searches = new List<(int Seats, bool AnySize)>();

            public int Cancels;

            /// <summary>When set, every search is refused with this code (a dropped socket).</summary>
            public string? Refuse;

            public event Action<TableTicket>? Matched;

            public void Match(string matchId) => Matched?.Invoke(TableTicket.Matchmade(matchId));

            public Awaitable<Result<bool>> SearchAsync(int seats, bool anySize, CancellationToken cancellation)
            {
                Searches.Add((seats, anySize));
                var done = new AwaitableCompletionSource<Result<bool>>();
                done.SetResult(Refuse == null ? Result<bool>.Ok(true) : Result<bool>.Fail(Refuse));
                return done.Awaitable;
            }

            public readonly List<int> RankedBands = new List<int>();

            public Awaitable<Result<bool>> SearchRankedAsync(int band, CancellationToken cancellation)
            {
                RankedBands.Add(band);
                var done = new AwaitableCompletionSource<Result<bool>>();
                done.SetResult(Refuse == null ? Result<bool>.Ok(true) : Result<bool>.Fail(Refuse));
                return done.Awaitable;
            }

            public int RungSearches;

            public readonly List<string> BotGames = new List<string>();

            public Awaitable<Result<bool>> SearchRungAsync(CancellationToken cancellation)
            {
                RungSearches++;
                var done = new AwaitableCompletionSource<Result<bool>>();
                done.SetResult(Result<bool>.Ok(true));
                return done.Awaitable;
            }

            public Awaitable<Result<TableTicket>> BotTableAsync(int seats, string gameId, CancellationToken cancellation)
            {
                BotGames.Add(gameId);
                return BotTableAsync(seats, cancellation);
            }

            public Awaitable<Result<TableTicket>> BotTableAsync(int seats, CancellationToken cancellation)
            {
                BotTables.Add(seats);
                var done = new AwaitableCompletionSource<Result<TableTicket>>();
                done.SetResult(Result<TableTicket>.Ok(new TableTicket("bots.1", "BOTS42", "t_BOTS42")));
                return done.Awaitable;
            }

            public readonly List<int> BotTables = new List<int>();

            public Awaitable CancelAsync()
            {
                Cancels++;
                var done = new AwaitableCompletionSource();
                done.SetResult();
                return done.Awaitable;
            }
        }

        private FakeMatchmaker _matchmaker = null!;
        private QuickPlayViewModel _model = null!;

        [SetUp]
        public void SetUp()
        {
            _matchmaker = new FakeMatchmaker();
            _model = new QuickPlayViewModel(_matchmaker);
        }

        [TearDown]
        public void TearDown() => _model.Dispose();

        [Test]
        public void TheSearchStartsStrictOnTableSize()
        {
            _ = _model.StartAsync(5, default);
            Assert.AreEqual(QuickPlayViewModel.Stage.Strict, _model.Current);
            CollectionAssert.AreEqual(new[] { (5, false) }, _matchmaker.Searches);
        }

        [Test]
        public void AtTenSecondsTheSearchAcceptsAnyTableSizeOnce()
        {
            _ = _model.StartAsync(6, default);
            _model.Tick(9.9f);
            Assert.AreEqual(1, _matchmaker.Searches.Count);
            _model.Tick(0.2f);
            _model.Tick(3f);
            Assert.AreEqual(QuickPlayViewModel.Stage.Widened, _model.Current);
            CollectionAssert.AreEqual(new[] { (6, false), (6, true) }, _matchmaker.Searches);
        }

        [Test]
        public void AtTwentySecondsItSaysTheTableStartsWithThree()
        {
            _ = _model.StartAsync(4, default);
            _model.Tick(QuickPlayViewModel.StartAtSeconds);
            Assert.AreEqual(QuickPlayViewModel.Stage.Starting, _model.Current);
        }

        [Test]
        public void ARungSearchAsksForFourAndNeverWidens()
        {
            _ = _model.StartRungAsync(default);
            Assert.AreEqual("rung", _model.Game);
            Assert.AreEqual(4, _model.Seats);
            Assert.AreEqual(1, _matchmaker.RungSearches);
            _model.Tick(QuickPlayViewModel.WidenAtSeconds + 1f);
            Assert.AreEqual(QuickPlayViewModel.Stage.Strict, _model.Current);
            Assert.AreEqual(0, _matchmaker.Searches.Count, "no Thulla ticket, no wider one");
        }

        [Test]
        public void ARungSearchThatFindsNobodyGetsBotsAfterFifteenSeconds()
        {
            TableTicket? found = null;
            _model.Found += t => found = t;
            _ = _model.StartRungAsync(default);
            _model.Tick(QuickPlayViewModel.RungBotsAtSeconds - 0.1f);
            Assert.AreEqual(0, _matchmaker.BotGames.Count);
            _model.Tick(0.2f);
            CollectionAssert.AreEqual(new[] { "rung" }, _matchmaker.BotGames);
            Assert.IsTrue(_model.WithBots);
            Assert.IsNotNull(found);
        }

        [Test]
        public void AThullaSearchAfterARungOneIsThullaAgain()
        {
            _ = _model.StartRungAsync(default);
            _ = _model.CancelAsync();
            _ = _model.StartAsync(4, default);
            Assert.AreEqual("thulla", _model.Game);
            _model.Tick(QuickPlayViewModel.BotsAtSeconds);
            CollectionAssert.AreEqual(new[] { "thulla" }, _matchmaker.BotGames);
        }

        [Test]
        public void AtThirtySecondsWithNobodyItOpensATableWithBots()
        {
            TableTicket? found = null;
            _model.Found += t => found = t;
            _ = _model.StartAsync(4, default);
            _model.Tick(QuickPlayViewModel.BotsAtSeconds - 0.1f);
            Assert.IsNull(found);
            CollectionAssert.IsEmpty(_matchmaker.BotTables);
            _model.Tick(0.2f);
            CollectionAssert.AreEqual(new[] { 4 }, _matchmaker.BotTables);
            Assert.AreEqual(1, _matchmaker.Cancels, "the matchmaker ticket goes first");
            Assert.AreEqual("bots.1", found?.MatchId);
            Assert.IsTrue(_model.WithBots);
            Assert.AreEqual(QuickPlayViewModel.Stage.Found, _model.Current);
            _matchmaker.Match("late");
            Assert.AreEqual("bots.1", found?.MatchId, "a late human match is ignored");
        }

        [Test]
        public void RankedNeverFillsWithBots()
        {
            _ = _model.StartRankedAsync(100, 50, 400, default);
            _model.Tick(QuickPlayViewModel.BotsAtSeconds + 5f);
            CollectionAssert.IsEmpty(_matchmaker.BotTables);
        }

        [Test]
        public void AMatchHandsOverTheTableOnce()
        {
            var found = new List<TableTicket>();
            _model.Found += found.Add;
            _ = _model.StartAsync(4, default);
            _matchmaker.Match("match-1");
            _matchmaker.Match("match-2");
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("match-1", found[0].MatchId);
            Assert.IsTrue(found[0].IsMatchmade);
        }

        [Test]
        public void CancellingLeavesTheQueueAndIgnoresALateMatch()
        {
            var found = new List<TableTicket>();
            _model.Found += found.Add;
            _ = _model.StartAsync(4, default);
            _ = _model.CancelAsync();
            _matchmaker.Match("late");
            Assert.AreEqual(1, _matchmaker.Cancels);
            Assert.AreEqual(QuickPlayViewModel.Stage.Idle, _model.Current);
            Assert.IsEmpty(found);
        }

        [Test]
        public void ARankedSearchWidensItsBandEveryTenSecondsUpToTheCap()
        {
            _ = _model.StartRankedAsync(150, 150, 600, default);
            Assert.IsTrue(_model.Ranked);
            Assert.AreEqual(4, _model.Seats);
            _model.Tick(9.9f);
            CollectionAssert.AreEqual(new[] { 150 }, _matchmaker.RankedBands);
            _model.Tick(0.2f);
            Assert.AreEqual(QuickPlayViewModel.Stage.Widened, _model.Current);
            _model.Tick(10f);
            _model.Tick(10f);
            _model.Tick(10f);
            _model.Tick(30f);
            CollectionAssert.AreEqual(new[] { 150, 300, 450, 600 }, _matchmaker.RankedBands);
            Assert.AreEqual(600, _model.Band);
            Assert.IsEmpty(_matchmaker.Searches, "Ranked never falls back to a Quick Play ticket");
        }

        [Test]
        public void ARankedSearchNeverSaysItStartsWithThree()
        {
            _ = _model.StartRankedAsync(150, 150, 600, default);
            _model.Tick(QuickPlayViewModel.StartAtSeconds + 1f);
            Assert.AreNotEqual(QuickPlayViewModel.Stage.Starting, _model.Current);
            _ = _model.StartAsync(4, default);
            Assert.IsFalse(_model.Ranked, "a Quick Play search after a Ranked one is Quick Play again");
        }

        [Test]
        public void ARankedSearchStopsAfterNinetySecondsAndAsks()
        {
            _ = _model.StartRankedAsync(150, 150, 600, default);
            _model.Tick(QuickPlayViewModel.RankedTimeoutSeconds - 1f);
            Assert.IsTrue(_model.Searching);
            _model.Tick(1f);
            Assert.AreEqual(QuickPlayViewModel.Stage.TimedOut, _model.Current);
            Assert.IsTrue(_model.Stopped);
            Assert.IsFalse(_model.Searching);
            Assert.AreEqual(1, _matchmaker.Cancels, "the ticket is dropped while the player decides");
            _matchmaker.Match("late.1");
            Assert.AreEqual(QuickPlayViewModel.Stage.TimedOut, _model.Current, "a late match does not seat a player who was asked");
            Assert.IsEmpty(_matchmaker.BotTables, "Ranked never fills with bots");
        }

        [Test]
        public void KeepSearchingStartsTheRankedSearchAgainAtTheBandItReached()
        {
            _ = _model.StartRankedAsync(150, 150, 600, default);
            for (var second = 0; second < QuickPlayViewModel.RankedTimeoutSeconds; second += 10)
            {
                _model.Tick(10f);
            }

            Assert.AreEqual(QuickPlayViewModel.Stage.TimedOut, _model.Current);
            var bandsBefore = _matchmaker.RankedBands.Count;
            _ = _model.RetryAsync(default);
            Assert.AreEqual(QuickPlayViewModel.Stage.Strict, _model.Current);
            Assert.IsTrue(_model.Ranked);
            Assert.AreEqual(0f, _model.Elapsed);
            Assert.AreEqual(bandsBefore + 1, _matchmaker.RankedBands.Count);
            Assert.AreEqual(600, _matchmaker.RankedBands[_matchmaker.RankedBands.Count - 1], "the widened band is kept");
        }

        [Test]
        public void CancelFromAStoppedSearchGoesIdleWithoutAnotherCall()
        {
            _ = _model.StartRankedAsync(150, 150, 600, default);
            _model.Tick(QuickPlayViewModel.RankedTimeoutSeconds);
            var cancels = _matchmaker.Cancels;
            _ = _model.CancelAsync();
            Assert.AreEqual(QuickPlayViewModel.Stage.Idle, _model.Current);
            Assert.AreEqual(cancels, _matchmaker.Cancels);
        }

        [Test]
        public void AFailedSearchCanBeRetriedAsTheSameSearch()
        {
            _matchmaker.Refuse = "matchmaker.unavailable";
            _ = _model.StartAsync(5, default);
            Assert.AreEqual(QuickPlayViewModel.Stage.Failed, _model.Current);
            Assert.IsTrue(_model.Stopped);
            _matchmaker.Refuse = null;
            _ = _model.RetryAsync(default);
            Assert.AreEqual(QuickPlayViewModel.Stage.Strict, _model.Current);
            Assert.AreEqual(5, _model.Seats);
            Assert.IsNull(_model.ErrorCode);
            _ = _model.StartRungAsync(default);
            _matchmaker.Refuse = "matchmaker.unavailable";
            _model.ConnectionLost("session.unavailable");
            _matchmaker.Refuse = null;
            var rungBefore = _matchmaker.RungSearches;
            _ = _model.RetryAsync(default);
            Assert.AreEqual("rung", _model.Game);
            Assert.AreEqual(rungBefore + 1, _matchmaker.RungSearches);
        }

        [Test]
        public void AfterAReconnectTheSearchAsksForItsTicketAgain()
        {
            _ = _model.StartAsync(4, default);
            _model.Tick(QuickPlayViewModel.WidenAtSeconds);
            var before = _matchmaker.Searches.Count;
            _ = _model.ResumeAsync(default);
            Assert.AreEqual(before + 1, _matchmaker.Searches.Count);
            Assert.AreEqual((4, true), _matchmaker.Searches[_matchmaker.Searches.Count - 1], "the widened search stays widened");
            Assert.IsTrue(_model.Searching);

            _ = _model.StartRankedAsync(150, 150, 600, default);
            _ = _model.ResumeAsync(default);
            Assert.AreEqual(150, _matchmaker.RankedBands[_matchmaker.RankedBands.Count - 1]);

            _matchmaker.Refuse = "matchmaker.unavailable";
            _ = _model.ResumeAsync(default);
            Assert.AreEqual(QuickPlayViewModel.Stage.Failed, _model.Current, "a ticket that can't be asked for again fails in sight");
            Assert.AreEqual("matchmaker.unavailable", _model.ErrorCode);
        }

        [Test]
        public void ALostConnectionFailsARunningSearchOnly()
        {
            _model.ConnectionLost("session.unavailable");
            Assert.AreEqual(QuickPlayViewModel.Stage.Idle, _model.Current);
            _ = _model.StartAsync(4, default);
            _model.ConnectionLost("session.unavailable");
            Assert.AreEqual(QuickPlayViewModel.Stage.Failed, _model.Current);
            Assert.AreEqual("session.unavailable", _model.ErrorCode);
        }

        [Test]
        public void SeatsAreKeptBetweenThreeAndSix()
        {
            _ = _model.StartAsync(9, default);
            Assert.AreEqual(6, _model.Seats);
            _ = _model.StartAsync(3, default);
            Assert.AreEqual(3, _model.Seats, "a three-seat table is a real choice");
            _ = _model.StartAsync(2, default);
            Assert.AreEqual(3, _model.Seats);
        }
    }
}
