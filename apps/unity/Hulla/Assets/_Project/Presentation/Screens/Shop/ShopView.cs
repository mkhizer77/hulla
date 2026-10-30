using System;
using Hulla.Application.Economy;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using Hulla.Presentation.Theme;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Shop
{
    /// <summary>
    /// The shop and collection in one (GDD 12.11): this week's spotlight, then one category at a
    /// time with a swatch, rarity and price, Buy (after a confirm) or Equip for what you own.
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

        private readonly VisualElement _root;

        public ShopView(VisualElement root, ShopViewModel model, Action back, Action<ShopItem> buy, Action<ShopItem> equip)
        {
            _model = model;
            _buy = buy;
            _equip = equip;
            _root = root;
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
                chip.AddToClassList("shop__chip");
                _categories.Add(chip);
            }

            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        /// <summary>
        /// The shop (design: Shop): the balance on the top's coin pill, the spotlight card with its days,
        /// the category chips, then a two-column grid where each tile shows its art, name and one chip
        /// for its state. A tile opens the item sheet; buying happens there, with one confirm only.
        /// </summary>
        private void Refresh()
        {
            Navigation.TopBar.Coins(_root, _model.Coins ?? 0);
            _balances.text = _model.Gems is long gems && gems > 0 ? Strings.Format(UiText.GemsOnlyBalance, gems) : "";
            _balances.style.display = _balances.text.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
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
                _list.Add(SpotlightCard(spotlight[0]));
            }

            if (_model.Shown.Count == 0)
            {
                ListState.Empty(_list, UiText.NothingThisWeek);
                return;
            }

            var grid = new VisualElement();
            grid.AddToClassList("shop__grid");
            foreach (var item in _model.Shown)
            {
                grid.Add(Tile(item));
            }

            _list.Add(grid);
        }

        /// <summary>Loads the catalog again after an error; null hides Retry.</summary>
        public Action? Retry { get; set; }

        private VisualElement SpotlightCard(ShopItem item)
        {
            var card = new Button(() => OpenItem(item)) { text = "" };
            card.AddToClassList("shop__spotlight");
            var days = ((int)DayOfWeek.Monday - (int)DateTime.Now.DayOfWeek + 7) % 7;
            var head = new Label(Strings.Format(UiText.SpotlightDays, days == 0 ? 7 : days)) { pickingMode = PickingMode.Ignore };
            head.AddToClassList("shop__spotlight-head");
            card.Add(head);
            var name = new Label(ItemText.Name(item.Id)) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("shop__spotlight-name");
            card.Add(name);
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("shop__spotlight-row");
            var art = ItemArt(item);
            art.AddToClassList("shop__art--large");
            row.Add(art);
            row.Add(StateChip(item));
            card.Add(row);
            return card;
        }

        private VisualElement Tile(ShopItem item)
        {
            var tile = new Button(() => OpenItem(item)) { text = "" };
            tile.AddToClassList("shop__tile");
            var name = new Label(ItemText.Name(item.Id)) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("shop__tile-name");
            tile.Add(name);
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("shop__tile-row");
            row.Add(ItemArt(item));
            row.Add(StateChip(item));
            tile.Add(row);
            return tile;
        }

        /// <summary>The one chip that says the state: the price, Owned · Equip, or Equipped.</summary>
        private Label StateChip(ShopItem item)
        {
            var chip = new Label(item.Equipped ? UiText.Equipped : item.Owned ? UiText.OwnedEquip : Price(item)) { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("shop__state");
            chip.EnableInClassList("shop__state--equipped", item.Equipped);
            chip.EnableInClassList("shop__state--owned", item.Owned && !item.Equipped);
            chip.EnableInClassList("shop__state--price", !item.Owned);
            chip.EnableInClassList("shop__state--short", !item.Owned && !_model.CanAfford(item));
            return chip;
        }

        /// <summary>
        /// The item's own art (GDD 12.11): its picture, or for a card face the king of hearts drawn in that
        /// style. An item without a picture yet keeps a drawn stand-in in its own colour.
        /// </summary>
        public static VisualElement ItemArt(ShopItem item)
        {
            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.AddToClassList("shop__art");
            art.AddToClassList("shop__art--" + item.Category);
            if (item.Category == "cardFace")
            {
                art.AddToClassList("shop__art--face");
                CosmeticArt.ApplyFace(art, item.Id);
                var card = new TableKit.CardElement().Show("KH");
                card.pickingMode = PickingMode.Ignore;
                art.Add(card);
                return art;
            }

            if (CosmeticArt.Has(item.Id))
            {
                art.AddToClassList("shop__art--picture");
                CosmeticArt.Apply(art, item.Id);
                return art;
            }

            var inner = new VisualElement { pickingMode = PickingMode.Ignore };
            inner.AddToClassList("shop__art-inner");
            inner.style.backgroundColor = CosmeticLook.Swatch(item.Id, item.Category);
            art.Add(inner);
            var motif = new VisualElement { pickingMode = PickingMode.Ignore };
            motif.AddToClassList("shop__art-motif");
            inner.Add(motif);
            return art;
        }

        /// <summary>The item sheet (design: Shop item): large art, name, type and rarity, one button, the balance.</summary>
        private void OpenItem(ShopItem item)
        {
            var panel = Navigation.Sheet.Open(_root, ItemText.Name(item.Id));
            panel.AddToClassList("item-sheet");
            var art = ItemArt(item);
            art.AddToClassList("shop__art--sheet");
            panel.Add(art);
            var kind = Navigation.Sheet.Line(Strings.Format(UiText.ItemTypeRarity, ItemText.Category(item.Category), ItemText.Rarity(item.Rarity)), "rarity--" + item.Rarity);
            panel.Add(kind);
            Button action;
            if (item.Equipped)
            {
                action = Navigation.Sheet.Button(UiText.Equipped, () => { }, "");
                action.SetEnabled(false);
            }
            else if (item.Owned)
            {
                action = Navigation.Sheet.Button(UiText.Equip, () =>
                {
                    Navigation.Sheet.Close(panel);
                    _equip(item);
                }, "yes");
            }
            else if (_model.CanAfford(item))
            {
                action = Navigation.Sheet.Button(Strings.Format(UiText.BuyEquip, Price(item)), () =>
                {
                    Navigation.Sheet.Close(panel);
                    _buy(item);
                }, "yes");
            }
            else
            {
                var missing = item.Gems > 0 ? item.Gems - (_model.Gems ?? 0) : item.Coins - (_model.Coins ?? 0);
                action = Navigation.Sheet.Button(Strings.Format(UiText.CoinsShort, item.Gems > 0 ? Strings.Format(UiText.PriceGems, missing) : Strings.Format(UiText.PriceCoins, missing)), () => { }, "");
                action.SetEnabled(false);
            }

            action.AddToClassList("item-sheet__action");
            if (_model.Busy != null)
            {
                action.SetEnabled(false);
            }

            panel.Add(action);
            panel.Add(Navigation.Sheet.Line(Strings.Format(UiText.YouHave, _model.Coins ?? 0, _model.Gems ?? 0), "item-sheet__balance"));
        }

        public static string Price(ShopItem item) =>
            item.Gems > 0 ? Strings.Format(UiText.PriceGems, item.Gems) : item.Coins > 0 ? Strings.Format(UiText.PriceCoins, item.Coins) : UiText.Free;
    }
}
