using System;
using System.Threading;
using Hulla.Application.Ads;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Ads
{
    /// <summary>
    /// The ad unit ids for this platform (Resources/HullaAds.json; docs/runbooks/push-and-purchases-setup.md).
    /// They are not secrets. An empty id turns that kind of ad off; with both empty the app shows none.
    /// </summary>
    public sealed class AdsConfig
    {
        public const string ResourceName = "HullaAds";

        public AdsConfig(string rewarded, string interstitial)
        {
            Rewarded = rewarded;
            Interstitial = interstitial;
        }

        public string Rewarded { get; }

        public string Interstitial { get; }

        public bool Any => Rewarded.Length > 0 || Interstitial.Length > 0;

        public static AdsConfig Parse(string json, string platform)
        {
            var units = JObject.Parse(json)[platform];
            return new AdsConfig((string?)units?["rewarded"] ?? "", (string?)units?["interstitial"] ?? "");
        }

        public static AdsConfig Load()
        {
#if UNITY_IOS
            const string platform = "ios";
#else
            const string platform = "android";
#endif
            var asset = Resources.Load<TextAsset>(ResourceName);
            return asset == null ? new AdsConfig("", "") : Parse(asset.text, platform);
        }
    }

    /// <summary>
    /// AppLovin MAX (GDD 13.6). The user id is the Nakama user id and the custom data names the placement, so
    /// the server's callback pays the right account once. MAX's own terms and privacy flow asks for consent
    /// (GDPR, UK, and ATT on iOS) when the SDK starts. A failed load is retried with a growing wait.
    /// </summary>
    public sealed class MaxAdNetwork : IAdNetwork
    {
        private const float MaxRetrySeconds = 64f;

        private readonly AdsConfig _config;
        private AwaitableCompletionSource<AdOutcome>? _rewardedShown;
        private AwaitableCompletionSource<bool>? _interstitialShown;
        private bool _rewardEarned;
        private int _rewardedRetries;
        private int _interstitialRetries;
        private CancellationToken _lifetime;

        public MaxAdNetwork(AdsConfig config)
        {
            _config = config;
        }

        public bool Supported => _config.Any;

        public bool RewardedReady => _config.Rewarded.Length > 0 && MaxSdk.IsInitialized() && MaxSdk.IsRewardedAdReady(_config.Rewarded);

        public bool InterstitialReady => _config.Interstitial.Length > 0 && MaxSdk.IsInitialized() && MaxSdk.IsInterstitialReady(_config.Interstitial);

        public async Awaitable<bool> StartAsync(string userId, CancellationToken cancellation)
        {
            _lifetime = cancellation;
            if (!Supported)
            {
                return false;
            }

            var started = new AwaitableCompletionSource<bool>();
            MaxSdkCallbacks.OnSdkInitializedEvent += _ => started.TrySetResult(true);
            MaxSdk.SetUserId(userId);
            MaxSdk.InitializeSdk();
            await started.Awaitable;
            if (_config.Rewarded.Length > 0)
            {
                MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += (_, _) => _rewardedRetries = 0;
                MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += (_, _) => RetryRewarded();
                MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += (_, _, _) => _rewardEarned = true;
                MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += (_, _, _) => EndRewarded(AdOutcome.Failed);
                MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += (_, _) => EndRewarded(_rewardEarned ? AdOutcome.Watched : AdOutcome.Closed);
                MaxSdk.LoadRewardedAd(_config.Rewarded);
            }

            if (_config.Interstitial.Length > 0)
            {
                MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += (_, _) => _interstitialRetries = 0;
                MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += (_, _) => RetryInterstitial();
                MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += (_, _, _) => EndInterstitial(false);
                MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += (_, _) => EndInterstitial(true);
                MaxSdk.LoadInterstitial(_config.Interstitial);
            }

            return true;
        }

        public async Awaitable<AdOutcome> ShowRewardedAsync(string placement, string customData, CancellationToken cancellation)
        {
            if (!RewardedReady || _rewardedShown != null)
            {
                return AdOutcome.Failed;
            }

            _rewardEarned = false;
            _rewardedShown = new AwaitableCompletionSource<AdOutcome>();
            MaxSdk.ShowRewardedAd(_config.Rewarded, placement, customData);
            return await _rewardedShown.Awaitable;
        }

        public async Awaitable<bool> ShowInterstitialAsync(CancellationToken cancellation)
        {
            if (!InterstitialReady || _interstitialShown != null)
            {
                return false;
            }

            _interstitialShown = new AwaitableCompletionSource<bool>();
            MaxSdk.ShowInterstitial(_config.Interstitial);
            return await _interstitialShown.Awaitable;
        }

        private void EndRewarded(AdOutcome outcome)
        {
            var shown = _rewardedShown;
            _rewardedShown = null;
            shown?.TrySetResult(outcome);
            MaxSdk.LoadRewardedAd(_config.Rewarded);
        }

        private void EndInterstitial(bool shown)
        {
            var waiting = _interstitialShown;
            _interstitialShown = null;
            waiting?.TrySetResult(shown);
            MaxSdk.LoadInterstitial(_config.Interstitial);
        }

        private async void RetryRewarded()
        {
            _rewardedRetries++;
            await WaitAsync(_rewardedRetries);
            if (!_lifetime.IsCancellationRequested)
            {
                MaxSdk.LoadRewardedAd(_config.Rewarded);
            }
        }

        private async void RetryInterstitial()
        {
            _interstitialRetries++;
            await WaitAsync(_interstitialRetries);
            if (!_lifetime.IsCancellationRequested)
            {
                MaxSdk.LoadInterstitial(_config.Interstitial);
            }
        }

        private async Awaitable WaitAsync(int retries)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(Mathf.Min(MaxRetrySeconds, Mathf.Pow(2f, Mathf.Min(6, retries))), _lifetime);
            }
            catch (OperationCanceledException)
            {
                // The app is closing; nothing to reload.
            }
        }
    }
}
