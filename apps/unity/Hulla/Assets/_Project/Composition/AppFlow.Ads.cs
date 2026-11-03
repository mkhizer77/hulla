using Hulla.Application.ViewModels;
using Hulla.Presentation.Screens.Missions;
using Hulla.Presentation.Screens.Shop;
using Hulla.Presentation.Text;
using UnityEngine;

namespace Hulla.Composition
{
    /// <summary>
    /// Ads in the shell (GDD 13.6): the network starts after the first round, the three rewarded
    /// placements, and the interstitial on leaving a Quick Play table between rounds.
    /// </summary>
    public sealed partial class AppFlow
    {
        /// <summary>Live rounds finished on this phone, for the interstitial's first round.</summary>
        public const string AdRoundsKey = "hulla.ads.rounds";

        private bool _quickPlayTable;
        private bool _adsWired;

        /// <summary>Once a live round has finished (ATT and consent come after the first round), for the signed-in account.</summary>
        private void StartAds()
        {
            var user = _app.Sessions.Current?.UserId;
            if (user == null || _app.Store.GetInt(WelcomePrompts.FirstRoundKey, 0) == 0)
            {
                return;
            }

            if (!_adsWired)
            {
                _adsWired = true;
                _app.Purchases.Changed += () => _app.Ads.Paid = _app.Purchases.Entitlements.RemoveAds || _app.Purchases.Entitlements.Products.Count > 0;
            }

            _ = _app.Ads.StartAsync(user, LocalOffsetMinutes(), _app.Lifetime);
        }

        /// <summary>The round is paid: count it, start the network after the first, and offer 2x coins.</summary>
        private void RoundFinishedForAds(string roundId, long coins)
        {
            _app.Store.SetInt(AdRoundsKey, _app.Store.GetInt(AdRoundsKey, 0) + 1);
            _app.Store.Save();
            StartAds();
            var tableId = _app.Session?.Table?.Client.Ticket.TableId ?? "";
            if (coins <= 0 || tableId.Length == 0 || !_app.Ads.CanOffer(AdsViewModel.DoubleCoins))
            {
                _tableView?.OfferDouble(null, null);
                return;
            }

            _tableView?.OfferDouble(UiText.DoubleForAd, () => WatchForDouble(tableId, roundId, coins));
        }

        private async void WatchForDouble(string tableId, string roundId, long coins)
        {
            _tableView?.OfferDouble(UiText.DoubleForAd, null);
            var paid = await _app.Ads.WatchAsync(AdsViewModel.DoubleCoins, AdsViewModel.DoubleCoins + ":" + tableId + ":" + roundId, _app.Lifetime);
            _tableView?.OfferDouble(paid ? Strings.Format(UiText.CoinsDoubled, coins) : UiText.AdNoReward, null);
            if (paid)
            {
                _ = _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
            }
        }

        /// <summary>The extra swap on Missions: a re-roll earned from an ad is spent by the next swap.</summary>
        private void WireAds(MissionsView view, int tz)
        {
            view.ExtraSwaps = () => _app.Ads.RerollCredits;
            view.SwapAdOffered = () => _app.Ads.CanOffer(AdsViewModel.Reroll);
            view.WatchForSwap = async startSwapping =>
            {
                if (await _app.Ads.WatchAsync(AdsViewModel.Reroll, AdsViewModel.Reroll, _app.Lifetime))
                {
                    startSwapping();
                }
                else
                {
                    view.Redraw();
                }
            };
            _ = _app.Ads.RefreshAsync(_app.Lifetime);
        }

        /// <summary>The 24-hour table trial in the Shop; equip it straight away once the server grants it.</summary>
        private void WireAds(ShopView view)
        {
            view.TrialOffered = () => _app.Ads.CanOffer(AdsViewModel.ThemeTrial);
            view.WatchForTrial = async item =>
            {
                var paid = await _app.Ads.WatchAsync(AdsViewModel.ThemeTrial, AdsViewModel.ThemeTrial + ":" + item.Id, _app.Lifetime);
                if (paid)
                {
                    await _app.Shop.EquipAsync(item.Id, _app.Lifetime);
                }

                var text = paid ? Strings.Format(UiText.TrialStarted, ItemText.Name(item.Id)) : UiText.AdNoReward;
                _shell.SetBanner(text);
                await Awaitable.WaitForSecondsAsync(4f);
                if (UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(_shell.Root, "shell-banner")?.text == text)
                {
                    _shell.SetBanner(null);
                }
            };
        }

        /// <summary>After Quick Play results, from the fourth round, once per interval, never for payers (GDD 13.6).</summary>
        private bool InterstitialAfterLeaving()
        {
            if (!_app.Ads.InterstitialDue(_quickPlayTable, _app.Store.GetInt(AdRoundsKey, 0)))
            {
                return false;
            }

            _ = _app.Ads.ShowInterstitialAsync(_app.Lifetime);
            return true;
        }
    }
}
