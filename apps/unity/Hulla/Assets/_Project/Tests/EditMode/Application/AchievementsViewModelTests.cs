using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class AchievementsViewModelTests
    {
        private sealed class FakeAchievements : IAchievementsService
        {
            public Awaitable<Result<(IReadOnlyList<AchievementInfo> Achievements, IReadOnlyList<string> UnlockedNow)>> AchievementsAsync(CancellationToken cancellation)
            {
                var source = new AwaitableCompletionSource<Result<(IReadOnlyList<AchievementInfo>, IReadOnlyList<string>)>>();
                source.SetResult(Result<(IReadOnlyList<AchievementInfo>, IReadOnlyList<string>)>.Ok((new List<AchievementInfo>
                {
                    new AchievementInfo("friends_10", "social", "friends", 10, 2, null, 15, null),
                    new AchievementInfo("rounds_100", "play", "rounds", 100, 90, null, 15, null),
                    new AchievementInfo("rounds_10", "play", "rounds", 10, 10, 5, 5, null),
                    new AchievementInfo("rounds_1000", "play", "rounds", 1000, 90, null, 50, "purana_khiladi_ustad"),
                }, new List<string>())));
                return source.Awaitable;
            }
        }

        [Test]
        public void CategoriesComeInOrderWithUnlockedFirstThenClosest()
        {
            var model = new AchievementsViewModel(new FakeAchievements());
            _ = model.RefreshAsync(CancellationToken.None);
            Assert.AreEqual("play", model.Groups[0].Category);
            Assert.AreEqual("social", model.Groups[1].Category);
            var play = model.Groups[0].Items;
            Assert.AreEqual("rounds_10", play[0].Id);
            Assert.AreEqual("rounds_100", play[1].Id);
            Assert.AreEqual("rounds_1000", play[2].Id);
            Assert.AreEqual(1, model.UnlockedCount);
            Assert.AreEqual(4, model.Total);
        }
    }
}
