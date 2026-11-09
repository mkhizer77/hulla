using System;
using System.Collections.Generic;
using Hulla.Application.Economy;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Shop
{
    /// <summary>
    /// What the stores sell (GDD 13.4): the Gems tab of the shop (gem packs, the starter bundle and
    /// Remove Ads) and the premium pass sheet. Prices are the store's, in the player's currency; the
    /// sheet's yellow button is the one confirm before the store's own.
    /// </summary>
    public static class StoreView
    {
        public const string Category = "store";

        /// <summary>The Gems tab: one tile per product that is not a pass, or a line when the store is away.</summary>
        /// <param name="retry">Connects to the store again when it was away; null hides Retry.</param>
        public static void Fill(VisualElement list, VisualElement root, PurchasesViewModel model, Action? retry = null)
        {
            if (!model.Supported)
            {
                ListState.Empty(list, UiText.StorePhoneOnly);
                return;
            }

            if (!model.Ready)
            {
                if (model.Connecting)
                {
                    ListState.Loading(list);
                }
                else
                {
                    ListState.Empty(list, UiText.StoreOffline, retry == null ? null : UiText.Retry, retry);
                }

                return;
            }

            var grid = new VisualElement();
            grid.AddToClassList("shop__grid");
            foreach (var product in model.Products)
            {
                if (product.Pass == null && model.Price(product.Id) != null)
                {
                    grid.Add(Tile(root, model, product));
                }
            }

            list.Add(grid);
        }

        public static string Name(string productId) => Strings.Text("product." + productId);

        /// <summary>What the product gives, one short line each.</summary>
        public static IReadOnlyList<string> Lines(ProductInfo product)
        {
            var lines = new List<string>();
            if (product.Gems > 0)
            {
                lines.Add(Strings.Format(UiText.ProductGems, product.Gems));
            }

            foreach (var item in product.Items)
            {
                lines.Add(ItemText.Name(item));
            }

            if (product.RemoveAds)
            {
                lines.Add(UiText.RemoveAdsLine);
            }

            return lines;
        }

        /// <summary>The product's picture: a class per product, so art lands in USS without code.</summary>
        public static VisualElement Art(ProductInfo product)
        {
            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.AddToClassList("shop__art");
            art.AddToClassList("shop__art--picture");
            art.AddToClassList("store__art");
            art.AddToClassList("store__art--" + product.Id);
            return art;
        }

        /// <summary>The product sheet: art, name, what it gives, and Buy with the store's price.</summary>
        public static void OpenProduct(VisualElement root, PurchasesViewModel model, ProductInfo product)
        {
            var panel = Navigation.Sheet.Open(root, Name(product.Id));
            panel.AddToClassList("item-sheet");
            var art = Art(product);
            art.AddToClassList("shop__art--sheet");
            panel.Add(art);
            foreach (var line in Lines(product))
            {
                panel.Add(Navigation.Sheet.Line(line));
            }

            panel.Add(BuyButton(panel, model, product.Id));
        }

        /// <summary>The premium pass sheet: Premium and Premium+, each with its line and price.</summary>
        public static void OpenPremium(VisualElement root, PurchasesViewModel model)
        {
            var panel = Navigation.Sheet.Open(root, UiText.UnlockPremium);
            panel.AddToClassList("item-sheet");
            foreach (var product in model.Products)
            {
                if (product.Pass == null || model.Price(product.Id) == null)
                {
                    continue;
                }

                var option = new VisualElement();
                option.AddToClassList("store__option");
                var name = new Label(Name(product.Id));
                name.AddToClassList("store__option-name");
                option.Add(name);
                option.Add(Navigation.Sheet.Line(product.Pass == "plus" ? UiText.PremiumPlusLine : UiText.PremiumLine));
                var buy = BuyButton(panel, model, product.Id);
                buy.EnableInClassList("sheet__answer--yes", product.Pass == "premium");
                option.Add(buy);
                panel.Add(option);
            }
        }

        /// <summary>True when the pass can be bought here; else the Pass keeps its quiet note.</summary>
        public static bool PremiumOnSale(PurchasesViewModel model)
        {
            foreach (var product in model.Products)
            {
                if (product.Pass != null && model.CanBuy(product.Id))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The line after a grant, or null for a product this build does not name.</summary>
        public static string? Thanks(ProductInfo? product)
        {
            if (product == null)
            {
                return null;
            }

            if (product.Pass != null)
            {
                return UiText.PassUnlocked;
            }

            if (product.RemoveAds && product.Gems == 0)
            {
                return UiText.AdsRemoved;
            }

            if (product.Items.Count > 0)
            {
                return UiText.BundleAdded;
            }

            return product.Gems > 0 ? Strings.Format(UiText.GemsAdded, product.Gems) : null;
        }

        private static VisualElement Tile(VisualElement root, PurchasesViewModel model, ProductInfo product)
        {
            var tile = new Button(() => OpenProduct(root, model, product)) { text = "" };
            tile.AddToClassList("shop__tile");
            var name = new Label(Name(product.Id)) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("shop__tile-name");
            tile.Add(name);
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("shop__tile-row");
            row.Add(Art(product));
            var owned = !product.Consumable && model.Entitlements.Owns(product.Id);
            var chip = new Label(owned ? UiText.Owned : model.Price(product.Id)) { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("shop__state");
            chip.EnableInClassList("shop__state--owned", owned);
            chip.EnableInClassList("shop__state--price", !owned);
            row.Add(chip);
            tile.Add(row);
            return tile;
        }

        private static Button BuyButton(VisualElement panel, PurchasesViewModel model, string productId)
        {
            Button button;
            if (!model.CanBuy(productId))
            {
                button = Navigation.Sheet.Button(UiText.Owned, () => { });
                button.SetEnabled(false);
            }
            else
            {
                button = Navigation.Sheet.Button(Strings.Format(UiText.BuyPrice, model.Price(productId)), () =>
                {
                    Navigation.Sheet.Close(panel);
                    model.Buy(productId);
                }, "yes");
                button.SetEnabled(model.Busy == null);
            }

            button.AddToClassList("item-sheet__action");
            return button;
        }
    }
}
