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
            _balances.text = _model.Coins is long coins && _model.Gems is long gems ? string.Format(UiText.Balances, coins, gems) : "";
            _status.text = _model.ErrorCode == null ? "" : UiText.Error(_model.ErrorCode);
            foreach (var chip in _categories.Children())
            {
                chip.EnableInClassList("chip--on", chip.name == "cat-" + _model.Category);
            }

            _list.Clear();
            var spotlight = _model.Spotlight;
            if (spotlight.Count > 0)
            {
                Heading(UiText.Spotlight);
                foreach (var item in spotlight)
                {
                    _list.Add(Row(item));
                }
            }

            Heading(ItemText.Category(_model.Category));
            foreach (var item in _model.Shown)
            {
                _list.Add(Row(item));
            }
        }

        private void Heading(string text)
        {
            var heading = new Label(text);
            heading.AddToClassList("caption");
            heading.AddToClassList("settings__heading");
            _list.Add(heading);
        }

        private VisualElement Row(ShopItem item)
        {
            var row = new VisualElement();
            row.AddToClassList("lobby-seat");
            row.AddToClassList("friend-row");
            var swatch = new VisualElement();
            swatch.AddToClassList("shop__swatch");
            swatch.AddToClassList("shop__swatch--" + item.Category);
            swatch.style.backgroundColor = CosmeticLook.Swatch(item.Id, item.Category);
            row.Add(swatch);
            var text = new VisualElement();
            text.AddToClassList("lobby-seat__name");
            text.Add(new Label(ItemText.Name(item.Id)));
            var rarity = new Label(ItemText.Rarity(item.Rarity));
            rarity.AddToClassList("caption");
            rarity.AddToClassList("rarity--" + item.Rarity);
            text.Add(rarity);
            row.Add(text);

            if (item.Equipped)
            {
                var tag = new Label(UiText.Equipped);
                tag.AddToClassList("tag--ok");
                row.Add(tag);
                return row;
            }

            var busy = _model.Busy == item.Id;
            Button action;
            if (item.Owned)
            {
                action = new Button(() => _equip(item)) { text = UiText.Equip };
            }
            else
            {
                action = new Button(() => _buy(item)) { text = Price(item) };
                action.AddToClassList("chip--on");
                action.SetEnabled(_model.CanAfford(item));
            }

            action.AddToClassList("chip");
            if (busy || _model.Busy != null)
            {
                action.SetEnabled(false);
            }

            row.Add(action);
            return row;
        }

        public static string Price(ShopItem item) =>
            item.Gems > 0 ? string.Format(UiText.PriceGems, item.Gems) : item.Coins > 0 ? string.Format(UiText.PriceCoins, item.Coins) : UiText.Free;
    }
}
