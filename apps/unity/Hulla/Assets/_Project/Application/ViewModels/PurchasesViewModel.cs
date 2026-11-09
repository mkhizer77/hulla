using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Economy;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>
    /// In-app purchases (GDD 13.4): the products the server sells priced by the phone's store, Buy,
    /// and Restore. Every store purchase goes to the server, which validates the receipt and grants
    /// it once; only then is it finished with the store. A purchase the server cannot reach stays
    /// with the store and is sent again on the next start, so nothing paid for is lost.
    /// </summary>
    public sealed class PurchasesViewModel
    {
        public const string Cancelled = "store.cancelled";
        public const string RestoreBusy = "restore";

        private readonly IAppStore _store;
        private readonly IPurchaseService _service;
        private readonly List<StoreReceipt> _waiting = new List<StoreReceipt>();
        private CancellationToken _lifetime;
        private bool _signedIn;
        private bool _connecting;
        private bool _sending;

        public PurchasesViewModel(IAppStore store, IPurchaseService service)
        {
            _store = store;
            _service = service;
            _store.Pending += OnPending;
            _store.Failed += OnFailed;
        }

        public event Action? Changed;

        /// <summary>A purchase was granted: refresh the wallet, the pass or the shop.</summary>
        public event Action<PurchaseGrant>? Granted;

        public IReadOnlyList<ProductInfo> Products { get; private set; } = Array.Empty<ProductInfo>();

        public Entitlements Entitlements { get; private set; } = Entitlements.None;

        /// <summary>True once the store is connected and priced; purchases are offered only then.</summary>
        public bool Ready { get; private set; }

        /// <summary>True while the store connection is being made, so the Gems tab can wait instead of saying it is away.</summary>
        public bool Connecting => _connecting;

        /// <summary>The product being bought, or <see cref="RestoreBusy"/>, so its button can wait.</summary>
        public string? Busy { get; private set; }

        public string? ErrorCode { get; private set; }

        /// <summary>Balances after the last grant, when one has happened.</summary>
        public (long Coins, long Gems)? Balances { get; private set; }

        public bool Supported => _store.Supported;

        public ProductInfo? Info(string productId)
        {
            foreach (var product in Products)
            {
                if (product.Id == productId)
                {
                    return product;
                }
            }

            return null;
        }

        /// <summary>The store's price in the player's currency, or null while the store has none.</summary>
        public string? Price(string productId) => Ready ? _store.Product(productId)?.Price : null;

        /// <summary>Whether Buy is offered: the store priced it and the account does not already own it for good.</summary>
        public bool CanBuy(string productId)
        {
            var info = Info(productId);
            return info != null && Price(productId) != null && (info.Consumable || !Entitlements.Owns(productId));
        }

        /// <summary>After sign-in: the products and entitlements, the store, then any purchase still waiting.</summary>
        public async Awaitable StartAsync(CancellationToken cancellation)
        {
            _lifetime = cancellation;
            var products = await _service.ProductsAsync(cancellation);
            if (!products.IsOk)
            {
                ErrorCode = products.Error.Code;
                Changed?.Invoke();
                return;
            }

            Products = products.Value.Products;
            Entitlements = products.Value.Entitlements;
            _signedIn = true;
            if (_store.Supported && !Ready && !_connecting)
            {
                _connecting = true;
                var list = new List<(string Id, bool Consumable)>();
                foreach (var product in Products)
                {
                    list.Add((product.Id, product.Consumable));
                }

                Ready = await _store.ConnectAsync(list, cancellation);
                _connecting = false;
            }

            Changed?.Invoke();
            await SendWaitingAsync(cancellation);
        }

        /// <summary>A new account on this phone: its entitlements, not the last one's.</summary>
        public void SignedOut()
        {
            _signedIn = false;
            Entitlements = Entitlements.None;
            Balances = null;
            Changed?.Invoke();
        }

        public void Buy(string productId)
        {
            if (Busy != null || !CanBuy(productId))
            {
                return;
            }

            Busy = productId;
            ErrorCode = null;
            Changed?.Invoke();
            _store.Buy(productId);
        }

        /// <summary>Restore purchases: the store offers its past purchases again, and the server grants what is missing.</summary>
        public async Awaitable RestoreAsync(CancellationToken cancellation)
        {
            if (Busy != null)
            {
                return;
            }

            Busy = RestoreBusy;
            ErrorCode = null;
            Changed?.Invoke();
            _store.Restore();
            var restored = await _service.RestoreAsync(cancellation);
            Busy = null;
            if (restored.IsOk)
            {
                Apply(restored.Value);
            }
            else
            {
                ErrorCode = restored.Error.Code;
            }

            Changed?.Invoke();
        }

        public void ClearError()
        {
            ErrorCode = null;
            Changed?.Invoke();
        }

        private void OnPending(StoreReceipt receipt)
        {
            foreach (var waiting in _waiting)
            {
                if (waiting.TransactionId == receipt.TransactionId && waiting.Store == receipt.Store)
                {
                    return;
                }
            }

            _waiting.Add(receipt);
            if (_signedIn)
            {
                _ = SendWaitingAsync(_lifetime);
            }
        }

        private void OnFailed(string productId, string code)
        {
            if (Busy == productId)
            {
                Busy = null;
            }

            ErrorCode = code == Cancelled ? null : code;
            Changed?.Invoke();
        }

        /// <summary>
        /// Sends each waiting receipt to the server. A granted (or already granted) one is finished
        /// with the store; a refused receipt is left unfinished, so the store refunds it in time; when
        /// the server is out of reach the rest wait for the next start.
        /// </summary>
        private async Awaitable SendWaitingAsync(CancellationToken cancellation)
        {
            if (_sending)
            {
                return;
            }

            _sending = true;
            try
            {
                while (_waiting.Count > 0 && _signedIn)
                {
                    var receipt = _waiting[0];
                    var verified = await _service.VerifyAsync(receipt.Store, receipt.Payload, cancellation);
                    if (Busy == receipt.ProductId)
                    {
                        Busy = null;
                    }

                    if (verified.IsOk)
                    {
                        _waiting.RemoveAt(0);
                        _store.Finish(receipt);
                        Apply(verified.Value);
                        Changed?.Invoke();
                        continue;
                    }

                    ErrorCode = verified.Error.Code;
                    if (verified.Error.Code == "economy.receipt_invalid")
                    {
                        _waiting.RemoveAt(0);
                        Changed?.Invoke();
                        continue;
                    }

                    Changed?.Invoke();
                    break;
                }
            }
            finally
            {
                _sending = false;
            }
        }

        private void Apply(PurchaseOutcome outcome)
        {
            Entitlements = outcome.Entitlements;
            Balances = (outcome.Coins, outcome.Gems);
            foreach (var grant in outcome.Purchases)
            {
                if (grant.Applied)
                {
                    Granted?.Invoke(grant);
                }
            }
        }
    }
}
