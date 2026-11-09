using System;
using System.Threading;
using Hulla.Application.Ads;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>
    /// Ads (GDD 13.6): three optional rewarded placements within the server's daily caps, and an
    /// interstitial after Quick Play results from the fourth round, at most once per interval, never
    /// for a player who has paid. The client shows the ad; the server pays it from the network's
    /// signed callback, so a watched ad waits for the server's count to move before it says done.
    /// </summary>
    public sealed class AdsViewModel
    {
        public const string DoubleCoins = "double_coins";
        public const string Reroll = "reroll";
        public const string ThemeTrial = "theme_trial";

        /// <summary>How many times the reward is looked for after a watched ad (about a second apart).</summary>
        public const int RewardChecks = 10;

        private readonly IAdNetwork _network;
        private readonly IAdsService _service;
        private readonly Func<double> _nowSeconds;
        private readonly Func<CancellationToken, Awaitable> _pause;
        private double? _lastInterstitial;
        private int _tzOffsetMinutes;

        /// <param name="nowSeconds">A clock in seconds, for the interstitial interval.</param>
        /// <param name="pause">The wait between reward checks.</param>
        public AdsViewModel(IAdNetwork network, IAdsService service, Func<double> nowSeconds, Func<CancellationToken, Awaitable> pause)
        {
            _network = network;
            _service = service;
            _nowSeconds = nowSeconds;
            _pause = pause;
        }

        public event Action? Changed;

        public AdsInfo? Info { get; private set; }

        /// <summary>True once the network is up for this account.</summary>
        public bool Started { get; private set; }

        /// <summary>The placement being watched, so its button can wait.</summary>
        public string? Busy { get; private set; }

        /// <summary>A player who has bought anything sees no interstitials (GDD 13.6: non-payers only).</summary>
        public bool Paid { get; set; }

        /// <summary>Under 18, or no birth year: ads stay contextual (GDD 24.6, 25.7).</summary>
        public bool UnderEighteen { get; set; } = true;

        public bool Supported => _network.Supported;

        /// <summary>After the first round (GDD 13.6: ATT and consent come then): the network, then today's caps.</summary>
        public async Awaitable StartAsync(string userId, int tzOffsetMinutes, CancellationToken cancellation)
        {
            _tzOffsetMinutes = tzOffsetMinutes;
            if (!_network.Supported || Started)
            {
                return;
            }

            Started = await _network.StartAsync(userId, UnderEighteen, cancellation);
            if (Started)
            {
                await RefreshAsync(cancellation);
            }
        }

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            var info = await _service.GetAsync(_tzOffsetMinutes, cancellation);
            if (info.IsOk)
            {
                Info = info.Value;
                Changed?.Invoke();
            }
        }

        /// <summary>Whether to offer the placement now: the network is up, the switch is on and today's cap has room.</summary>
        public bool CanOffer(string placement) =>
            Started && Busy == null && Info != null && Info.RewardedEnabled && Info.Left(placement) > 0;

        /// <summary>Re-rolls already earned today wait to be spent before another ad is offered.</summary>
        public int RerollCredits => Info?.RerollCredits ?? 0;

        /// <summary>
        /// Shows the rewarded ad and waits for the server to count it. True once the reward is paid;
        /// false when the ad was closed early, did not show, or the server never confirmed it.
        /// </summary>
        public async Awaitable<bool> WatchAsync(string placement, string customData, CancellationToken cancellation)
        {
            if (!CanOffer(placement))
            {
                return false;
            }

            Busy = placement;
            Changed?.Invoke();
            var before = Used(placement);
            var outcome = await _network.ShowRewardedAsync(placement, customData, cancellation);
            var paid = false;
            if (outcome == AdOutcome.Watched)
            {
                for (var i = 0; i < RewardChecks && !paid; i++)
                {
                    await _pause(cancellation);
                    await RefreshAsync(cancellation);
                    paid = Used(placement) > before;
                }
            }

            Busy = null;
            Changed?.Invoke();
            return paid;
        }

        /// <summary>An interstitial after Quick Play results (GDD 13.6); never in private rooms, never for payers.</summary>
        public bool InterstitialDue(bool quickPlay, int roundsPlayed)
        {
            if (!quickPlay || Paid || !Started || Info == null || !Info.Interstitials || !_network.InterstitialReady)
            {
                return false;
            }

            if (roundsPlayed < Info.FromRound)
            {
                return false;
            }

            return _lastInterstitial == null || _nowSeconds() - _lastInterstitial.Value >= Info.IntervalSeconds;
        }

        public async Awaitable ShowInterstitialAsync(CancellationToken cancellation)
        {
            _lastInterstitial = _nowSeconds();
            await _network.ShowInterstitialAsync(cancellation);
        }

        private int Used(string placement) => Info != null && Info.Used.TryGetValue(placement, out var used) ? used : 0;
    }
}
