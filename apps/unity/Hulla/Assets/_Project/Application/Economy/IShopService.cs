using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Economy
{
    /// <summary>One catalog item with the player's ownership (GDD 12.11). Price is coins or gems, never both.</summary>
    public sealed class ShopItem
    {
        public ShopItem(string id, string category, string rarity, long coins, long gems, bool owned, bool equipped, string? season)
        {
            Id = id;
            Category = category;
            Rarity = rarity;
            Coins = coins;
            Gems = gems;
            Owned = owned;
            Equipped = equipped;
            Season = season;
        }

        public string Id { get; }

        /// <summary>cardBack, cardFace, table, emotePack, celebration, voPack or soundSet.</summary>
        public string Category { get; }

        /// <summary>common, rare, epic or legendary.</summary>
        public string Rarity { get; }

        public long Coins { get; }

        public long Gems { get; }

        public bool Free => Coins == 0 && Gems == 0;

        public bool Owned { get; }

        public bool Equipped { get; }

        public string? Season { get; }
    }

    public sealed class ShopCatalog
    {
        public ShopCatalog(IReadOnlyList<ShopItem> items, IReadOnlyList<string> spotlight)
        {
            Items = items;
            Spotlight = spotlight;
        }

        public IReadOnlyList<ShopItem> Items { get; }

        /// <summary>This week's featured item ids.</summary>
        public IReadOnlyList<string> Spotlight { get; }
    }

    /// <summary>economy_catalog, economy_purchase and inventory_equip. Prices and balances live on the server (D2).</summary>
    public interface IShopService
    {
        Awaitable<Result<ShopCatalog>> CatalogAsync(CancellationToken cancellation);

        /// <returns>The balances after the purchase.</returns>
        Awaitable<Result<(long Coins, long Gems)>> PurchaseAsync(string itemId, CancellationToken cancellation);

        Awaitable<Result<bool>> EquipAsync(string itemId, CancellationToken cancellation);
    }
}
