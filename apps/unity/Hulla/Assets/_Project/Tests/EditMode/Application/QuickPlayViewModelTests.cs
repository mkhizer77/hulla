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

            public event Action<TableTicket>? Matched;

            public void Match(string matchId) => Matched?.Invoke(TableTicket.Matchmade(matchId));

            public Awaitable<Result<bool>> SearchAsync(int seats, bool anySize, CancellationToken cancellation)
            {
                Searches.Add((seats, anySize));
                var done = new AwaitableCompletionSource<Result<bool>>();
                done.SetResult(Result<bool>.Ok(true));
                return done.Awaitable;
            }

            public readonly List<int> RankedBands = new List<int>();

            public Awaitable<Result<bool>> SearchRankedAsync(int band, CancellationToken cancellation)
            {
                RankedBands.Add(band);
                var done = new AwaitableCompletionSource<Result<bool>>();
                done.SetResult(Result<bool>.Ok(true));
                return done.Awaitable;
            }

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
