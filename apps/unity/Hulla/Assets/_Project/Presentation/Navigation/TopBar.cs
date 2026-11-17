using System;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>
    /// The top of a list screen (design rules: one title plate, one way back or done): tab screens
    /// carry the gear on the left and the tab's name as the title; pushed screens carry ‹ on the left.
    /// </summary>
    public static class TopBar
    {
        /// <summary>A tab screen: the gear opens Settings and the title names the tab, not the segment.</summary>
        public static void Tab(VisualElement root, string title, Action openSettings)
        {
            var label = root.Q<Label>("title");
            if (label != null)
            {
                label.text = title;
            }

            var gear = root.Q<Button>("gear");
            if (gear != null)
            {
                gear.text = "";
                gear.tooltip = Text.UiText.Settings;
                gear.clicked += openSettings;
            }
        }

        /// <summary>Wires a top-bar button by name, when the screen has it.</summary>
        public static void On(VisualElement root, string name, string tooltip, Action action)
        {
            var button = root.Q<Button>(name);
            if (button == null)
            {
                return;
            }

            button.text = "";
            button.tooltip = tooltip;
            button.clicked += action;
        }

        /// <summary>The wallet pill on the right of the Shop and Pass tops: coins, then gems.</summary>
        /// <summary>The wallet pill before the balances are known: empty amounts, never a zero that isn't true.</summary>
        public static void Wallet(VisualElement root, long? coins, long? gems)
        {
            if (coins != null && gems != null)
            {
                Wallet(root, coins.Value, gems.Value);
                return;
            }

            foreach (var name in new[] { "coin-amount", "gem-amount" })
            {
                var amount = root.Q<Label>(name);
                if (amount != null)
                {
                    amount.text = "";
                }
            }
        }

        public static void Wallet(VisualElement root, long coins, long gems)
        {
            var culture = Text.Strings.Culture;
            var coinAmount = root.Q<Label>("coin-amount");
            if (coinAmount != null)
            {
                coinAmount.text = coins.ToString("N0", culture);
            }

            var gemAmount = root.Q<Label>("gem-amount");
            if (gemAmount != null)
            {
                gemAmount.text = gems.ToString("N0", culture);
            }

            var pill = root.Q<VisualElement>("coin-pill");
            if (pill != null)
            {
                pill.tooltip = Text.Strings.Format(Text.UiText.Balances, coins, gems);
            }
        }
    }
}
