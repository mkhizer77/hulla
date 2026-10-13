using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Economy;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The shop and collection (GDD 12.11): one category at a time, buying and equipping.</summary>
    public sealed class ShopViewModel
    {
        public static readonly string[] Categories = { "cardBack", "table", "cardFace", "emotePack", "celebration", "soundSet", "voPack" };

        private readonly IShopService _service;

        public ShopViewModel(IShopService service)
        {
            _service = service;
        }

        public event Action? Changed;

        public string Category { get; private set; } = Categories[0];

        public ShopCatalog? Catalog { get; private set; }

        public long? Coins { get; private set; }

        public long? Gems { get; private set; }

        public string? ErrorCode { get; private set; }

        /// <summary>The item being bought or equipped, so its button can wait.</summary>
        public string? Busy { get; private set; }

        public IReadOnlyList<ShopItem> Shown
        {
            get
            {
                var shown = new List<ShopItem>();
                if (Catalog == null)
                {
                    return shown;
                }

                foreach (var item in Catalog.Items)
                {
                    if (item.Category == Category)
                    {
                        shown.Add(item);
                    }
                }

                return shown;
            }
        }

        public IReadOnlyList<ShopItem> Spotlight
        {
            get
            {
                var featured = new List<ShopItem>();
                if (Catalog == null)
                {
                    return featured;
                }

                foreach (var id in Catalog.Spotlight)
                {
                    foreach (var item in Catalog.Items)
                    {
                        if (item.Id == id && !item.Owned)
                        {
                            featured.Add(item);
                        }
                    }
                }

                return featured;
            }
        }

        public void Show(string category)
        {
            Category = category;
            Changed?.Invoke();
        }

        public void SetBalances(long coins, long gems)
        {
            Coins = coins;
            Gems = gems;
            Changed?.Invoke();
        }

        /// <summary>Whether the wallet covers the item; unknown balances let the server decide.</summary>
        public bool CanAfford(ShopItem item) =>
            item.Gems > 0 ? Gems == null || Gems >= item.Gems : Coins == null || Coins >= item.Coins;

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            var catalog = await _service.CatalogAsync(cancellation);
            ErrorCode = catalog.IsOk ? null : catalog.Error.Code;
            if (catalog.IsOk)
            {
                Catalog = catalog.Value;
            }

            Changed?.Invoke();
        }

        public async Awaitable<bool> BuyAsync(string itemId, CancellationToken cancellation)
        {
            if (Busy != null)
            {
                return false;
            }

            Busy = itemId;
            Changed?.Invoke();
            var bought = await _service.PurchaseAsync(itemId, cancellation);
            if (bought.IsOk)
            {
                Coins = bought.Value.Coins;
                Gems = bought.Value.Gems;
            }

            Busy = null;
            await RefreshAsync(cancellation);
            KeepError(bought.IsOk ? null : bought.Error.Code);
            return bought.IsOk;
        }

        public async Awaitable<bool> EquipAsync(string itemId, CancellationToken cancellation)
        {
            if (Busy != null)
            {
                return false;
            }

            Busy = itemId;
            Changed?.Invoke();
            var equipped = await _service.EquipAsync(itemId, cancellation);
            Busy = null;
            await RefreshAsync(cancellation);
            KeepError(equipped.IsOk ? null : equipped.Error.Code);
            return equipped.IsOk;
        }

        /// <summary>The purchase's or equip's refusal outlives the catalog refresh that follows it.</summary>
        private void KeepError(string? code)
        {
            if (code != null)
            {
                ErrorCode = code;
                Changed?.Invoke();
            }
        }
    }
}
