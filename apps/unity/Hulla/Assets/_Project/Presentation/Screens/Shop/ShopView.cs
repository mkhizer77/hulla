using System;
using Hulla.Application.Economy;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using Hulla.Presentation.Theme;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Shop
{
    /// <summary>
    /// The shop and collection in one (GDD 12.11, the Bazaar in docs/design/rooftop-redesign.md): this
    /// week's lead item as a large spotlight card, the rest of the week and one category at a time as
    /// tiles in a two-column grid, each with a preview, rarity, and Buy (after a confirm) or Equip.
    /// </summary>
    public sealed class ShopView : IDisposable
    {
        private readonly ShopViewModel _model;
        private readonly Action<ShopItem> _buy;
        private readonly Action<ShopItem> _equip;
        private readonly VisualElement _list;
        private readonly VisualElement _categories;
        private readonly Label _status;
        private readonly Label _balances;

        public ShopView(VisualElement root, ShopViewModel model, Action back, Action<ShopItem> buy, Action<ShopItem> equip)
        {
            _model = model;
            _buy = buy;
            _equip = equip;
            root.Q<Label>("title").text = UiText.Shop;
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            _list = root.Q<VisualElement>("list");
            _categories = root.Q<VisualElement>("categories");
            _status = root.Q<Label>("status");
            _balances = root.Q<Label>("balances");
            foreach (var category in ShopViewModel.Categories)
            {
                var chip = new Button(() => _model.Show(category)) { text = ItemText.Category(category), name = "cat-" + category };
                chip.AddToClassList("chip");
                _categories.Add(chip);
            }

            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            _balances.text = _model.Coins is long coins && _model.Gems is long gems ? Strings.Format(UiText.Balances, coins, gems) : "";
            _status.text = "";
            foreach (var chip in _categories.Children())
            {
                chip.EnableInClassList("chip--on", chip.name == "cat-" + _model.Category);
            }

            _list.Clear();
            if (_model.ErrorCode != null && _model.Shown.Count == 0 && _model.Spotlight.Count == 0)
            {
                ListState.Error(_list, _model.ErrorCode, Retry);
                return;
            }

            if (_model.ErrorCode != null)
            {
                _status.text = UiText.Error(_model.ErrorCode);
            }

            var spotlight = _model.Spotlight;
            if (spotlight.Count > 0)
            {
                Heading(UiText.Spotlight);
                _list.Add(Spotlight(spotlight[0]));
                if (spotlight.Count > 1)
                {
                    var rest = Grid();
                    for (var i = 1; i < spotlight.Count; i++)
                    {
                        rest.Add(Tile(spotlight[i]));
                    }
                }
            }

            Heading(ItemText.Category(_model.Category));
            if (_model.Shown.Count == 0)
            {
                ListState.Empty(_list, UiText.NothingThisWeek);
            }

            var grid = Grid();
            foreach (var item in _model.Shown)
            {
                grid.Add(Tile(item));
            }
        }

        /// <summary>Loads the catalog again after an error; null hides Retry.</summary>
        public Action? Retry { get; set; }

        private void Heading(string text)
        {
            var heading = new Label(text);
            heading.AddToClassList("caption");
            heading.AddToClassList("settings__heading");
            _list.Add(heading);
        }

        private VisualElement Grid()
        {
            var grid = new VisualElement();
            grid.AddToClassList("shop__grid");
            _list.Add(grid);
            return grid;
        }

        /// <summary>The item's look: a card back or face as a small card, anything else as a swatch.</summary>
        private static VisualElement Preview(ShopItem item, string size)
        {
            var preview = new VisualElement { pickingMode = PickingMode.Ignore };
            preview.AddToClassList("shop__swatch");
            preview.AddToClassList("shop__swatch--" + item.Category);
            preview.AddToClassList("shop__preview");
            preview.AddToClassList("shop__preview--" + size);
            preview.style.backgroundColor = CosmeticLook.Swatch(item.Id, item.Category);
            return preview;
        }

        private static Label Rarity(ShopItem item)
        {
            var rarity = new Label(ItemText.Rarity(item.Rarity));
            rarity.AddToClassList("caption");
            rarity.AddToClassList("rarity--" + item.Rarity);
            rarity.AddToClassList("shop__rarity");
            return rarity;
        }

        /// <summary>This week's lead item: a large preview, its name and price, and one gold button.</summary>
        private VisualElement Spotlight(ShopItem item)
        {
            var card = new VisualElement();
            card.AddToClassList("shop__spotlight");
            var top = new VisualElement();
            top.AddToClassList("shop__spotlight-top");
            top.Add(Preview(item, "large"));
            var text = new VisualElement();
            text.AddToClassList("shop__spotlight-text");
            var name = new Label(ItemText.Name(item.Id));
            name.AddToClassList("shop__spotlight-name");
            text.Add(name);
            text.Add(Rarity(item));
            if (!item.Owned)
            {
                var price = new Label(Price(item));
                price.AddToClassList("shop__price");
                text.Add(price);
            }

            top.Add(text);
            card.Add(top);
            var action = Action(item, true);
            action.AddToClassList("shop__spotlight-action");
            card.Add(action);
            return card;
        }

        /// <summary>One item in the grid: rarity on top, the preview, and Equip, the price, or Equipped.</summary>
        private VisualElement Tile(ShopItem item)
        {
            var tile = new VisualElement();
            tile.AddToClassList("lobby-seat");
            tile.AddToClassList("shop__tile");
            tile.Add(Rarity(item));
            tile.Add(Preview(item, "small"));
            var name = new Label(ItemText.Name(item.Id));
            name.AddToClassList("lobby-seat__name");
            name.AddToClassList("shop__tile-name");
            tile.Add(name);
            tile.Add(Action(item, false));
            return tile;
        }

        private VisualElement Action(ShopItem item, bool spotlight)
        {
            if (item.Equipped)
            {
                var tag = new Label(UiText.Equipped);
                tag.AddToClassList("tag--ok");
                tag.AddToClassList("shop__equipped");
                return tag;
            }

            Button action;
            if (item.Owned)
            {
                action = new Button(() => _equip(item)) { text = UiText.Equip };
                action.AddToClassList("chip");
            }
            else
            {
                // The spotlight shows its price above, so its button says Buy; a tile's button is the price.
                action = new Button(() => _buy(item)) { text = spotlight ? UiText.Buy : Price(item) };
                action.AddToClassList(spotlight ? "button" : "chip");
                action.AddToClassList(spotlight ? "button--primary" : "chip--on");
                action.SetEnabled(_model.CanAfford(item));
            }

            action.AddToClassList("shop__action");
            if (_model.Busy != null)
            {
                action.SetEnabled(false);
            }

            return action;
        }

        public static string Price(ShopItem item) =>
            item.Gems > 0 ? Strings.Format(UiText.PriceGems, item.Gems) : item.Coins > 0 ? Strings.Format(UiText.PriceCoins, item.Coins) : UiText.Free;
    }
}
