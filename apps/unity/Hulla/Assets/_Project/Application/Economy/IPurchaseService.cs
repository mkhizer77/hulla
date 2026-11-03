using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Economy
{
    /// <summary>A product of content/products: what a verified receipt grants (GDD 13.4).</summary>
    public sealed class ProductInfo
    {
        public ProductInfo(string id, bool consumable, long gems, string? pass, bool removeAds, IReadOnlyList<string> items)
        {
            Id = id;
            Consumable = consumable;
            Gems = gems;
            Pass = pass;
            RemoveAds = removeAds;
            Items = items;
        }

        public string Id { get; }

        public bool Consumable { get; }

        public long Gems { get; }

        /// <summary>premium or plus for a season pass, else null.</summary>
        public string? Pass { get; }

        public bool RemoveAds { get; }

        public IReadOnlyList<string> Items { get; }
    }

    /// <summary>What the account holds from purchases; kept on the server so a new phone restores it.</summary>
    public sealed class Entitlements
    {
        public static readonly Entitlements None = new Entitlements(false, null, new Dictionary<string, int>());

        public Entitlements(bool removeAds, string? pendingPass, IReadOnlyDictionary<string, int> products)
        {
            RemoveAds = removeAds;
            PendingPass = pendingPass;
            Products = products;
        }

        public bool RemoveAds { get; }

        /// <summary>A pass bought between seasons, waiting for the next one.</summary>
        public string? PendingPass { get; }

        /// <summary>Product id to the number of granted transactions.</summary>
        public IReadOnlyDictionary<string, int> Products { get; }

        public bool Owns(string productId) => Products.TryGetValue(productId, out var count) && count > 0;
    }

    /// <summary>One transaction in a verify or restore answer.</summary>
    public sealed class PurchaseGrant
    {
        public PurchaseGrant(string productId, string transactionId, bool applied, string? reason)
        {
            ProductId = productId;
            TransactionId = transactionId;
            Applied = applied;
            Reason = reason;
        }

        public string ProductId { get; }

        public string TransactionId { get; }

        public bool Applied { get; }

        /// <summary>unknown_product, other_account, refunded or already_granted when not applied.</summary>
        public string? Reason { get; }
    }

    public sealed class PurchaseOutcome
    {
        public PurchaseOutcome(IReadOnlyList<PurchaseGrant> purchases, long coins, long gems, Entitlements entitlements)
        {
            Purchases = purchases;
            Coins = coins;
            Gems = gems;
            Entitlements = entitlements;
        }

        public IReadOnlyList<PurchaseGrant> Purchases { get; }

        public long Coins { get; }

        public long Gems { get; }

        public Entitlements Entitlements { get; }
    }

    /// <summary>economy_products, iap_verify and iap_restore: the server checks every receipt with the store (D2).</summary>
    public interface IPurchaseService
    {
        Awaitable<Result<(IReadOnlyList<ProductInfo> Products, Entitlements Entitlements)>> ProductsAsync(CancellationToken cancellation);

        Awaitable<Result<PurchaseOutcome>> VerifyAsync(string store, string receipt, CancellationToken cancellation);

        /// <summary>The store purchases the server can list for the account, granted again where missing.</summary>
        Awaitable<Result<PurchaseOutcome>> RestoreAsync(CancellationToken cancellation);
    }
}
