using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class LeaderboardViewModelTests
    {
        private sealed class FakeBoards : ILeaderboardService
        {
            public readonly List<(LeaderboardPeriod Period, bool Friends)> Asked = new List<(LeaderboardPeriod, bool)>();
            public AwaitableCompletionSource<Result<LeaderboardPage>>? Pending;

            public Awaitable<Result<LeaderboardPage>> BoardAsync(LeaderboardPeriod period, bool friends, CancellationToken cancellation)
            {
                Asked.Add((period, friends));
                Pending = new AwaitableCompletionSource<Result<LeaderboardPage>>();
                return Pending.Awaitable;
            }
        }

        private static LeaderboardPage PageOf(string name) =>
            new LeaderboardPage(new List<LeaderboardRow> { new LeaderboardRow(1, "u1", name, 40, 3) }, null, null);

        [Test]
        public void SwitchingBoardsAsksForThatBoardAndScope()
        {
            var service = new FakeBoards();
            var model = new LeaderboardViewModel(service);
            _ = model.ShowAsync(LeaderboardPeriod.Monthly, true, CancellationToken.None);
            Assert.AreEqual((LeaderboardPeriod.Monthly, true), service.Asked[0]);
            Assert.IsTrue(model.Loading);
            service.Pending!.SetResult(Result<LeaderboardPage>.Ok(PageOf("Ayesha")));
            Assert.IsFalse(model.Loading);
            Assert.AreEqual("Ayesha", model.Page!.Rows[0].Name);
        }

        [Test]
        public void ASlowEarlierAnswerDoesNotReplaceTheLatest()
        {
            var service = new FakeBoards();
            var model = new LeaderboardViewModel(service);
            _ = model.ShowAsync(LeaderboardPeriod.Weekly, false, CancellationToken.None);
            var first = service.Pending!;
            _ = model.ShowAsync(LeaderboardPeriod.Weekly, true, CancellationToken.None);
            service.Pending!.SetResult(Result<LeaderboardPage>.Ok(PageOf("Friend")));
            first.SetResult(Result<LeaderboardPage>.Ok(PageOf("Everyone")));
            Assert.AreEqual("Friend", model.Page!.Rows[0].Name);
        }
    }
}
