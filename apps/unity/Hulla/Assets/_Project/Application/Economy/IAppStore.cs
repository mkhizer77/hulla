using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Hulla.Application.Economy
{
    /// <summary>A product as the phone's store prices it, in the player's own currency.</summary>
    public sealed class StoreProduct
    {
        public StoreProduct(string id, string price)
        {
            Id = id;
            Price = price;
        }

        public string Id { get; }

        public string Price { get; }
    }

    /// <summary>
    /// A purchase the store holds until the server has granted it: just bought, restored, or left
    /// over from a run that stopped before the grant (GDD 13.4).
    /// </summary>
    public sealed class StoreReceipt
    {
        public StoreReceipt(string productId, string transactionId, string store, string payload, object handle)
        {
            ProductId = productId;
            TransactionId = transactionId;
            Store = store;
            Payload = payload;
            Handle = handle;
        }

        public string ProductId { get; }

        public string TransactionId { get; }

        /// <summary>apple or google, as iap_verify names them.</summary>
        public string Store { get; }

        /// <summary>What the server validates: the App Store receipt, or Google's json and signature.</summary>
        public string Payload { get; }

        /// <summary>The store's own order, to finish it once granted.</summary>
        public object Handle { get; }
    }

    /// <summary>
    /// The phone's store (Apple or Google). It never grants anything: every purchase goes to the
    /// server first and is finished only once the server has granted it (ARCHITECTURE 8.10).
    /// </summary>
    public interface IAppStore
    {
        /// <summary>False in the editor and on desktop builds, where no store exists.</summary>
        bool Supported { get; }

        /// <summary>A purchase is waiting for the server.</summary>
        event Action<StoreReceipt>? Pending;

        /// <summary>A purchase did not go through: the product id and an error code.</summary>
        event Action<string, string>? Failed;

        /// <summary>Connects and prices the products; pending purchases arrive through <see cref="Pending"/> after it.</summary>
        Awaitable<bool> ConnectAsync(IReadOnlyList<(string Id, bool Consumable)> products, CancellationToken cancellation);

        StoreProduct? Product(string id);

        void Buy(string productId);

        /// <summary>Tells the store the purchase is delivered, so it stops offering it again.</summary>
        void Finish(StoreReceipt receipt);

        /// <summary>Asks the store for past purchases again (Apple asks for a Restore button).</summary>
        void Restore();
    }

    /// <summary>The store where there is none: the editor, desktop and web builds.</summary>
    public sealed class NoAppStore : IAppStore
    {
        public bool Supported => false;

        public event Action<StoreReceipt>? Pending
        {
            add { }
            remove { }
        }

        public event Action<string, string>? Failed
        {
            add { }
            remove { }
        }

        public Awaitable<bool> ConnectAsync(IReadOnlyList<(string Id, bool Consumable)> products, CancellationToken cancellation)
        {
            var source = new AwaitableCompletionSource<bool>();
            source.SetResult(false);
            return source.Awaitable;
        }

        public StoreProduct? Product(string id) => null;

        public void Buy(string productId)
        {
        }

        public void Finish(StoreReceipt receipt)
        {
        }

        public void Restore()
        {
        }
    }
}
