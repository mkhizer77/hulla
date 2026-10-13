using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class PassViewModelTests
    {
        private sealed class FakePass : IPassService
        {
            public bool Season = true;
            public readonly HashSet<string> Claimed = new HashSet<string>();

            private static Awaitable<T> Done<T>(T value)
            {
                var source = new AwaitableCompletionSource<T>();
                source.SetResult(value);
                return source.Awaitable;
            }

            private PassReward R(long tier, bool premium, long coins, long gems) =>
                new PassReward(coins, gems, new List<string>(), null, Claimed.Contains(tier + (premium ? ":p" : ":f")));

            public Awaitable<Result<PassInfo?>> PassAsync(CancellationToken c)
            {
                if (!Season)
                {
                    return Done(Result<PassInfo?>.Ok(null));
                }

                var tiers = new List<PassTier>();
                for (var t = 1; t <= 3; t++)
                {
                    tiers.Add(new PassTier(t, t <= 2, R(t, false, 50, 0), R(t, true, 0, 5)));
                }

                return Done(Result<PassInfo?>.Ok(new PassInfo("season.basant_2026", 0, 1000, 2300, 2, 300, false, false, tiers)));
            }

            public Awaitable<Result<bool>> ClaimAsync(long tier, bool premium, CancellationToken c)
            {
                if (premium)
                {
                    return Done(Result<bool>.Fail("table.not_authorised"));
                }

                Claimed.Add(tier + ":f");
                return Done(Result<bool>.Ok(true));
            }
        }

        [Test]
        public void ReachedFreeTiersAreClaimableWithoutThePremiumPass()
        {
            var service = new FakePass();
            var model = new PassViewModel(service);
            _ = model.RefreshAsync(CancellationToken.None);
            Assert.AreEqual(2, model.Claimable, "tiers 1 and 2 free; premium is locked");
            _ = model.ClaimAsync(1, false, CancellationToken.None);
            Assert.AreEqual(1, model.Claimable);
            Assert.IsTrue(model.Info!.Tiers[0].Free.Claimed);
        }

        [Test]
        public void ARefusedPremiumClaimShowsTheReason()
        {
            var model = new PassViewModel(new FakePass());
            _ = model.RefreshAsync(CancellationToken.None);
            _ = model.ClaimAsync(1, true, CancellationToken.None);
            Assert.AreEqual("table.not_authorised", model.ErrorCode);
        }

        [Test]
        public void BetweenSeasonsThereIsNoPass()
        {
            var model = new PassViewModel(new FakePass { Season = false });
            _ = model.RefreshAsync(CancellationToken.None);
            Assert.IsTrue(model.NoSeason);
            Assert.IsNull(model.Info);
        }
    }
}
