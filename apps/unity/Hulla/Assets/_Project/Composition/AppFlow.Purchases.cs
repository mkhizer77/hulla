using Hulla.Application.Economy;
using Hulla.Presentation.Screens.Shop;
using Hulla.Presentation.Text;
using UnityEngine;

namespace Hulla.Composition
{
    /// <summary>In-app purchases in the shell (GDD 13.4): the store after sign-in, the thanks line and the refreshes after a grant.</summary>
    public sealed partial class AppFlow
    {
        private string? _purchasesUser;

        /// <summary>Once per signed-in account: products, the store, and any purchase still waiting for the server.</summary>
        private void StartPurchases()
        {
            var user = _app.Sessions.Current?.UserId;
            if (user == null || user == _purchasesUser)
            {
                return;
            }

            if (_purchasesUser != null)
            {
                _app.Purchases.SignedOut();
            }

            _purchasesUser = user;
            _ = _app.Purchases.StartAsync(_app.Lifetime);
        }

        /// <summary>A purchase was granted: say thanks and bring the wallet, the shop and the pass up to date.</summary>
        private async void OnPurchaseGranted(PurchaseGrant grant)
        {
            if (_app.Purchases.Balances is (long coins, long gems))
            {
                _app.Shop.SetBalances(coins, gems);
            }

            var info = _app.Purchases.Info(grant.ProductId);
            if (info?.Pass != null)
            {
                _ = _app.Pass.RefreshAsync(_app.Lifetime);
            }

            if (info != null && info.Items.Count > 0)
            {
                _ = _app.Shop.RefreshAsync(_app.Lifetime);
            }

            _ = _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
            var text = StoreView.Thanks(info);
            if (text == null)
            {
                return;
            }

            _shell.SetBanner(text);
            await Awaitable.WaitForSecondsAsync(4f);
            if (UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(_shell.Root, "shell-banner")?.text == text)
            {
                _shell.SetBanner(null);
            }
        }

        /// <summary>Settings › Account: Restore purchases, where the phone has a store.</summary>
        private System.Action<System.Action<string>>? RestorePurchasesAction() =>
            !_app.Purchases.Supported ? null : async status =>
            {
                await _app.Purchases.RestoreAsync(_app.Lifetime);
                status(_app.Purchases.ErrorCode != null ? UiText.Error(_app.Purchases.ErrorCode) : UiText.PurchasesRestored);
            };
    }
}
