using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Ads;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class AdsViewModelTests
    {
        private static Awaitable<T> Done<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }

        private static Awaitable Now()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }

        private sealed class FakeNetwork : IAdNetwork
        {
            public AdOutcome Outcome = AdOutcome.Watched;
            public bool Interstitial = true;
            public readonly List<string> Shown = new List<string>();
            public FakeServer? PaysTo;

            public bool Supported => true;

            public bool RewardedReady => true;

            public bool InterstitialReady => Interstitial;

            public Awaitable<bool> StartAsync(string userId, CancellationToken cancellation) => Done(true);

            public Awaitable<AdOutcome> ShowRewardedAsync(string placement, string customData, CancellationToken cancellation)
            {
                Shown.Add(customData);
                if (Outcome == AdOutcome.Watched && PaysTo != null)
                {
                    PaysTo.Used[placement] = PaysTo.Used.TryGetValue(placement, out var n) ? n + 1 : 1;
                }

                return Done(Outcome);
            }

            public Awaitable<bool> ShowInterstitialAsync(CancellationToken cancellation)
            {
                Shown.Add("interstitial");
                return Done(true);
            }
        }

        private sealed class FakeServer : IAdsService
        {
            public readonly Dictionary<string, int> Used = new Dictionary<string, int>();
            public bool Interstitials = true;
            public bool Rewarded = true;

            public Awaitable<Result<AdsInfo>> GetAsync(int tzOffsetMinutes, CancellationToken cancellation) =>
                Done(Result<AdsInfo>.Ok(new AdsInfo(
                    Rewarded,
                    new Dictionary<string, int> { ["double_coins"] = 5, ["reroll"] = 2, ["theme_trial"] = 1 },
                    new Dictionary<string, int>(Used),
                    0,
                    Interstitials,
                    4,
                    240)));
        }

        private static (AdsViewModel Model, FakeNetwork Network, FakeServer Server, double[] Clock) Make()
        {
            var server = new FakeServer();
            var network = new FakeNetwork { PaysTo = server };
            var clock = new[] { 0.0 };
            var model = new AdsViewModel(network, server, () => clock[0], _ => Now());
            _ = model.StartAsync("me", 0, CancellationToken.None);
            return (model, network, server, clock);
        }

        [Test]
        public void AWatchedAdCountsOnlyOnceTheServerHasPaidIt()
        {
            var (model, network, server, _) = Make();
            Assert.IsTrue(model.CanOffer(AdsViewModel.Reroll));
            var paid = false;
            Run(model.WatchAsync(AdsViewModel.Reroll, "reroll", CancellationToken.None), r => paid = r);
            Assert.IsTrue(paid);
            CollectionAssert.AreEqual(new[] { "reroll" }, network.Shown);

            network.PaysTo = null;
            Run(model.WatchAsync(AdsViewModel.Reroll, "reroll", CancellationToken.None), r => paid = r);
            Assert.IsFalse(paid, "the server never counted it");
            Assert.IsNull(model.Busy);
        }

        [Test]
        public void AnAdClosedEarlyPaysNothingAndTheCapStopsTheOffer()
        {
            var (model, network, _, _) = Make();
            network.Outcome = AdOutcome.Closed;
            var paid = true;
            Run(model.WatchAsync(AdsViewModel.ThemeTrial, "theme_trial:table_marble", CancellationToken.None), r => paid = r);
            Assert.IsFalse(paid);

            network.Outcome = AdOutcome.Watched;
            Run(model.WatchAsync(AdsViewModel.ThemeTrial, "theme_trial:table_marble", CancellationToken.None), r => paid = r);
            Assert.IsTrue(paid);
            Assert.IsFalse(model.CanOffer(AdsViewModel.ThemeTrial), "one trial a day");
        }

        [Test]
        public void NoRewardedOffersWhenTheSwitchIsOff()
        {
            var server = new FakeServer { Rewarded = false };
            var model = new AdsViewModel(new FakeNetwork(), server, () => 0, _ => Now());
            _ = model.StartAsync("me", 0, CancellationToken.None);
            Assert.IsFalse(model.CanOffer(AdsViewModel.DoubleCoins));
        }

        [Test]
        public void InterstitialsFollowTheQuickPlayRoundAndIntervalRules()
        {
            var (model, _, _, clock) = Make();
            Assert.IsFalse(model.InterstitialDue(true, 3), "from the fourth round");
            Assert.IsFalse(model.InterstitialDue(false, 10), "never in private rooms");
            Assert.IsTrue(model.InterstitialDue(true, 4));
            _ = model.ShowInterstitialAsync(CancellationToken.None);
            clock[0] = 200;
            Assert.IsFalse(model.InterstitialDue(true, 5), "one per four minutes");
            clock[0] = 241;
            Assert.IsTrue(model.InterstitialDue(true, 6));
            model.Paid = true;
            Assert.IsFalse(model.InterstitialDue(true, 6), "never for payers");
        }

        [Test]
        public void RemoveAdsTurnsInterstitialsOffOnTheServer()
        {
            var server = new FakeServer { Interstitials = false };
            var model = new AdsViewModel(new FakeNetwork(), server, () => 0, _ => Now());
            _ = model.StartAsync("me", 0, CancellationToken.None);
            Assert.IsFalse(model.InterstitialDue(true, 10));
        }

        [Test]
        public void NothingIsOfferedWithoutANetwork()
        {
            var model = new AdsViewModel(new NoAdNetwork(), new FakeServer(), () => 0, _ => Now());
            _ = model.StartAsync("me", 0, CancellationToken.None);
            Assert.IsFalse(model.Started);
            Assert.IsFalse(model.CanOffer(AdsViewModel.Reroll));
            Assert.IsFalse(model.InterstitialDue(true, 10));
        }

        private static async void Run(Awaitable<bool> task, System.Action<bool> done) => done(await task);
    }
}
