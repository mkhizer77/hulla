using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Ads
{
    /// <summary>How a rewarded ad ended: watched to the end, closed early, or not shown.</summary>
    public enum AdOutcome
    {
        Watched,
        Closed,
        Failed,
    }

    /// <summary>
    /// The ad network (AppLovin MAX, GDD 13.6). It only shows ads: a reward is paid by the server
    /// from the network's signed callback, never by the client.
    /// </summary>
    public interface IAdNetwork
    {
        /// <summary>False in the editor, on desktop, and until the build carries ad unit ids.</summary>
        bool Supported { get; }

        /// <summary>Starts the SDK for the account; the network's own consent flow runs here (GDPR, UK, ATT on iOS).</summary>
        /// <param name="underEighteen">No personalised ads and no sale or sharing of data, whatever the consent (GDD 24.6, 25.7).</param>
        Awaitable<bool> StartAsync(string userId, bool underEighteen, CancellationToken cancellation);

        bool RewardedReady { get; }

        bool InterstitialReady { get; }

        /// <param name="customData">The placement as the server reads it, e.g. <c>reroll</c> or <c>theme_trial:table_marble</c>.</param>
        Awaitable<AdOutcome> ShowRewardedAsync(string placement, string customData, CancellationToken cancellation);

        Awaitable<bool> ShowInterstitialAsync(CancellationToken cancellation);
    }

    /// <summary>No ads: the editor, desktop builds, and builds without ad unit ids.</summary>
    public sealed class NoAdNetwork : IAdNetwork
    {
        public bool Supported => false;

        public bool RewardedReady => false;

        public bool InterstitialReady => false;

        public Awaitable<bool> StartAsync(string userId, bool underEighteen, CancellationToken cancellation) => Done(false);

        public Awaitable<AdOutcome> ShowRewardedAsync(string placement, string customData, CancellationToken cancellation) => Done(AdOutcome.Failed);

        public Awaitable<bool> ShowInterstitialAsync(CancellationToken cancellation) => Done(false);

        private static Awaitable<T> Done<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }
    }

    /// <summary>What ads_get says the account may see today (GDD 13.6).</summary>
    public sealed class AdsInfo
    {
        public AdsInfo(bool rewardedEnabled, IReadOnlyDictionary<string, int> caps, IReadOnlyDictionary<string, int> used, int rerollCredits, bool interstitials, int fromRound, int intervalSeconds)
        {
            RewardedEnabled = rewardedEnabled;
            Caps = caps;
            Used = used;
            RerollCredits = rerollCredits;
            Interstitials = interstitials;
            FromRound = fromRound;
            IntervalSeconds = intervalSeconds;
        }

        public bool RewardedEnabled { get; }

        public IReadOnlyDictionary<string, int> Caps { get; }

        public IReadOnlyDictionary<string, int> Used { get; }

        /// <summary>Extra re-rolls earned today and not yet spent.</summary>
        public int RerollCredits { get; }

        /// <summary>False for an account that holds Remove Ads, or when the switch is off.</summary>
        public bool Interstitials { get; }

        public int FromRound { get; }

        public int IntervalSeconds { get; }

        public int Left(string placement) =>
            (Caps.TryGetValue(placement, out var cap) ? cap : 0) - (Used.TryGetValue(placement, out var used) ? used : 0);
    }

    /// <summary>ads_get.</summary>
    public interface IAdsService
    {
        Awaitable<Result<AdsInfo>> GetAsync(int tzOffsetMinutes, CancellationToken cancellation);
    }
}
