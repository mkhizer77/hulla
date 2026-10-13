using System.Threading;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class DailyRewardViewModelTests
    {
        private sealed class FakeDaily : IDailyRewardService
        {
            public bool Claimable = true;
            public int Claims;

            private static Awaitable<T> Done<T>(T value)
            {
                var source = new AwaitableCompletionSource<T>();
                source.SetResult(value);
                return source.Awaitable;
            }

            public Awaitable<Result<DailyRewardStatus>> StatusAsync(int tz, CancellationToken c) =>
                Done(Result<DailyRewardStatus>.Ok(new DailyRewardStatus(Claimable, new DailyRewardDay(3, 70, 0), new DailyRewardDay(4, 80, 2))));

            public Awaitable<Result<DailyRewardStatus>> ClaimAsync(CancellationToken c)
            {
                Claims++;
                Claimable = false;
                return Done(Result<DailyRewardStatus>.Ok(new DailyRewardStatus(false, new DailyRewardDay(3, 70, 0), new DailyRewardDay(4, 80, 2))));
            }
        }

        [Test]
        public void TodaysRewardCanBeClaimedOnce()
        {
            var service = new FakeDaily();
            var model = new DailyRewardViewModel(service);
            _ = model.RefreshAsync(0, CancellationToken.None);
            Assert.IsTrue(model.Status!.Claimable);

            _ = model.ClaimAsync(CancellationToken.None);
            Assert.AreEqual(1, service.Claims);
            Assert.IsFalse(model.Status!.Claimable);
            Assert.AreEqual(70, model.Claimed!.Coins);
            Assert.AreEqual(80, model.Status.Next.Coins);

            _ = model.ClaimAsync(CancellationToken.None);
            Assert.AreEqual(1, service.Claims, "a second tap does not claim again");
        }

        [Test]
        public void AClaimedDayShowsTomorrow()
        {
            var model = new DailyRewardViewModel(new FakeDaily { Claimable = false });
            _ = model.RefreshAsync(0, CancellationToken.None);
            Assert.IsFalse(model.Status!.Claimable);
            Assert.IsNull(model.Claimed);
        }
    }
}
