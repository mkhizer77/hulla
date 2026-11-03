using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hulla.Application.Economy;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Hulla.Infrastructure.Purchasing
{
    /// <summary>
    /// Apple and Google through Unity IAP (GDD 13.4). The product ids are the same on both stores
    /// (content/products). A purchase is held as pending until the server has granted it; Unity IAP
    /// offers an unfinished one again on the next start.
    /// </summary>
    public sealed class UnityIapStore : IAppStore
    {
        private readonly Dictionary<string, PendingOrder> _orders = new Dictionary<string, PendingOrder>();
        private StoreController? _controller;
        private string _store = "";

        public bool Supported => true;

        public event Action<StoreReceipt>? Pending;

        public event Action<string, string>? Failed;

        public async Awaitable<bool> ConnectAsync(IReadOnlyList<(string Id, bool Consumable)> products, CancellationToken cancellation)
        {
            var store = UnityIAPServices.GetDefaultStore();
            _store = store == AppleAppStore.Name ? "apple" : store == GooglePlay.Name ? "google" : "";
            if (_store.Length == 0)
            {
                return false;
            }

            var controller = UnityIAPServices.StoreController();
            _controller = controller;
            // Every handler before Connect: a purchase left from an earlier run can arrive at once.
            controller.OnPurchasePending += OnPurchasePending;
            controller.OnPurchaseFailed += failed => Failed?.Invoke(ProductOf(failed), Code(failed.FailureReason));
            controller.OnPurchaseDeferred += deferred => Failed?.Invoke(ProductOf(deferred), "economy.purchase_deferred");
            controller.OnStoreDisconnected += failure => Debug.LogWarning($"store disconnected: {failure.Message}");
            try
            {
                await controller.Connect();
            }
            catch (Exception error)
            {
                Debug.LogWarning($"store connect: {error.GetType().Name}");
                return false;
            }

            cancellation.ThrowIfCancellationRequested();
            var fetched = new AwaitableCompletionSource<bool>();
            controller.OnProductsFetched += _ => fetched.TrySetResult(true);
            controller.OnProductsFetchFailed += failure =>
            {
                Debug.LogWarning($"store products: {failure.FailureReason}");
                fetched.TrySetResult(controller.GetProducts().Count > 0);
            };
            var definitions = products
                .Select(p => new ProductDefinition(p.Id, p.Consumable ? ProductType.Consumable : ProductType.NonConsumable))
                .ToList();
            controller.FetchProducts(definitions);
            var ok = await fetched.Awaitable;
            // Purchases the store still holds (paid, never granted) come back through OnPurchasePending.
            controller.FetchPurchases();
            return ok;
        }

        public StoreProduct? Product(string id)
        {
            var product = _controller?.GetProductById(id);
            return product == null || !product.availableToPurchase
                ? null
                : new StoreProduct(id, product.metadata.localizedPriceString);
        }

        public void Buy(string productId)
        {
            if (_controller == null)
            {
                Failed?.Invoke(productId, "economy.store_unavailable");
                return;
            }

            _controller.PurchaseProduct(productId);
        }

        public void Finish(StoreReceipt receipt)
        {
            if (receipt.Handle is PendingOrder order && _controller != null)
            {
                _controller.ConfirmPurchase(order);
                _orders.Remove(receipt.TransactionId);
            }
        }

        public void Restore() => _controller?.RestoreTransactions((ok, error) =>
        {
            if (!ok)
            {
                Debug.LogWarning($"store restore: {error}");
            }
        });

        private void OnPurchasePending(PendingOrder order)
        {
            var productId = ProductOf(order);
            var transaction = order.Info.TransactionID ?? "";
            var payload = _store == "apple" ? order.Info.Apple?.AppReceipt : null;
            payload ??= PayloadOf(order.Info.Receipt);
            if (productId.Length == 0 || string.IsNullOrEmpty(payload))
            {
                Debug.LogWarning("store purchase without a receipt");
                Failed?.Invoke(productId, "economy.receipt_invalid");
                return;
            }

            _orders[transaction] = order;
            Pending?.Invoke(new StoreReceipt(productId, transaction, _store, payload!, order));
        }

        /// <summary>Unity IAP's receipt is <c>{Store, TransactionID, Payload}</c>; the server takes the Payload.</summary>
        private static string? PayloadOf(string? receipt)
        {
            if (string.IsNullOrEmpty(receipt))
            {
                return null;
            }

            try
            {
                return (string?)JObject.Parse(receipt!)["Payload"];
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }

        private static string ProductOf(Order order) =>
            order.CartOrdered?.Items().FirstOrDefault()?.Product?.definition?.id ?? "";

        private static string Code(PurchaseFailureReason reason) => reason switch
        {
            PurchaseFailureReason.UserCancelled => Hulla.Application.ViewModels.PurchasesViewModel.Cancelled,
            PurchaseFailureReason.ExistingPurchasePending => "economy.purchase_pending",
            PurchaseFailureReason.PaymentDeclined => "economy.payment_declined",
            _ => "economy.store_unavailable",
        };
    }
}
