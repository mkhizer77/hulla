using System.Collections.Generic;
using Hulla.Application.Economy;
using Hulla.Application.Services;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using UnityEngine;

namespace Hulla.Composition
{
    /// <summary>
    /// The shell's share of the event log (docs/design/analytics-events.md): screens, sign-in, joining
    /// and leaving tables, purchases, ads, rewards, and what is known about the player. The table's own
    /// events come from <see cref="AnalyticsTracker"/>. Nothing here runs before the player agrees:
    /// <see cref="Hulla.Application.Analytics.AnalyticsClient"/> drops everything until then.
    /// </summary>
    public sealed partial class AppFlow
    {
        /// <summary>Frames counted before a perf_sample is worth sending.</summary>
        private const int PerfMinFrames = 600;

        /// <summary>How the player is getting to the next table; EnterAsync reads it once.</summary>
        private string? _joinVia;

        /// <summary>How the next account to sign in on this phone got here.</summary>
        private string _signInMethod = "device";
        private string _lastScreen = "";
        private float _perfSeconds;
        private int _perfFrames;

        private void WireAnalytics()
        {
            _shell.ScreenShown += OnScreenShown;
            _app.Purchases.Started += OnPurchaseStarted;
            _app.Purchases.Failed += OnPurchaseFailed;
            _app.Purchases.Changed += DescribePlayer;
            _app.Progress.Changed += DescribePlayer;
            _app.Friends.Changed += DescribePlayer;
            _app.Ads.Watched += OnAdWatched;
            _app.Ads.InterstitialShown += OnInterstitialShown;
        }

        private void UnwireAnalytics()
        {
            _shell.ScreenShown -= OnScreenShown;
            _app.Purchases.Started -= OnPurchaseStarted;
            _app.Purchases.Failed -= OnPurchaseFailed;
            _app.Purchases.Changed -= DescribePlayer;
            _app.Progress.Changed -= DescribePlayer;
            _app.Friends.Changed -= DescribePlayer;
            _app.Ads.Watched -= OnAdWatched;
            _app.Ads.InterstitialShown -= OnInterstitialShown;
        }

        private void OnScreenShown(string screen, bool overlay)
        {
            var name = screen.ToLowerInvariant();
            // Home redraws itself often; one event per arrival.
            if (!overlay && name == _lastScreen)
            {
                return;
            }

            if (!overlay)
            {
                _lastScreen = name;
            }

            _app.Analytics.Track(AnalyticsEvents.ScreenView(name, overlay));
        }

        private void OnPurchaseStarted(string productId) => _app.Analytics.Track(AnalyticsEvents.IapStart(productId));

        private void OnPurchaseFailed(string productId, string code) =>
            _app.Analytics.Track(code == PurchasesViewModel.Cancelled
                ? AnalyticsEvents.IapResult(productId, "cancelled")
                : AnalyticsEvents.IapResult(productId, "failed", code));

        private void OnAdWatched(string placement, bool rewarded)
        {
            _app.Analytics.Track(AnalyticsEvents.AdWatch(placement, rewarded));
            if (rewarded)
            {
                _app.Analytics.Track(AnalyticsEvents.AdReward(placement));
            }
        }

        private void OnInterstitialShown() => _app.Analytics.Track(AnalyticsEvents.AdImpression("interstitial"));

        /// <summary>Once per account per launch, from AfterSignIn.</summary>
        private void TrackSignIn()
        {
            DescribePlayer();
            _app.Analytics.Track(AnalyticsEvents.SignIn(_signInMethod));
            _signInMethod = "device";
        }

        private static string ProviderName(SocialProvider provider) => provider switch
        {
            SocialProvider.Apple => "apple",
            SocialProvider.Google => "google",
            _ => "facebook",
        };

        /// <summary>A soft-currency buy in the shop went through.</summary>
        private void TrackShopPurchase(ShopViewModel shop, string itemId)
        {
            ShopItem? bought = null;
            foreach (var item in shop.Catalog?.Items ?? System.Array.Empty<ShopItem>())
            {
                if (item.Id == itemId)
                {
                    bought = item;
                    break;
                }
            }

            _app.Analytics.Track(bought == null
                ? AnalyticsEvents.Purchase(itemId, placement: "shop")
                : bought.Gems > 0
                    ? AnalyticsEvents.Purchase(itemId, bought.Gems, "shop", "gems")
                    : AnalyticsEvents.Purchase(itemId, bought.Coins, "shop", "coins"));
        }

        /// <summary>
        /// What Amplitude knows about the player besides their events: the account's id and a few facts
        /// to split charts by. No name, handle, birth year or contact detail. Only changes are sent.
        /// </summary>
        private void DescribePlayer()
        {
            var settings = _app.Settings;
            var properties = new Dictionary<string, object>
            {
                ["locale"] = Strings.Locale,
                ["text_scale"] = settings.TextScale.ToString(),
                ["reduce_motion"] = settings.ReduceMotion,
                ["four_colour_deck"] = settings.FourColourDeck,
                ["high_contrast"] = settings.HighContrast,
                ["large_cards"] = settings.LargeCards,
                ["left_handed"] = settings.LeftHanded,
                ["one_tap_play"] = settings.OneTapPlay,
                ["voice_chat"] = settings.VoiceChat,
                ["rounds_on_device"] = _app.Store.GetInt(AdRoundsKey, 0),
            };
            var user = _app.Sessions.Current?.UserId;
            if (user != null)
            {
                if (_app.Progress.Info is { } info)
                {
                    properties["level"] = info.Level;
                }

                if (_app.Progress.BalancesKnown)
                {
                    properties["coins"] = _app.Progress.Coins;
                    properties["gems"] = _app.Progress.Gems;
                }

                var entitlements = _app.Purchases.Entitlements;
                properties["payer"] = entitlements.RemoveAds || entitlements.Products.Count > 0;
                properties["remove_ads"] = entitlements.RemoveAds;
                properties["friends"] = _app.Friends.Friends.Count;
            }

            _app.Analytics.Identify(user, properties);
        }

        private bool IsFriend(string userId)
        {
            foreach (var friend in _app.Friends.Friends)
            {
                if (friend.UserId == userId)
                {
                    return true;
                }
            }

            return false;
        }

        private void CountFrame(float deltaSeconds)
        {
            _perfSeconds += deltaSeconds;
            _perfFrames++;
        }

        /// <summary>One performance sample per stretch in the foreground (GDD 23.3), sent as the app goes to the background.</summary>
        private void TrackPerf()
        {
            if (_perfFrames >= PerfMinFrames && _perfSeconds > 0f)
            {
                var memoryMb = SystemInfo.systemMemorySize;
                _app.Analytics.Track(AnalyticsEvents.PerfSample(
                    _perfFrames / _perfSeconds,
                    UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0),
                    memoryMb < 3500 ? "low" : memoryMb < 6500 ? "mid" : "high"));
            }

            _perfSeconds = 0f;
            _perfFrames = 0;
        }
    }
}
