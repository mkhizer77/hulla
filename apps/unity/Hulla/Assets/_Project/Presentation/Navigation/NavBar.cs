using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>The four tabs of the bottom navigation (GDD 14.1, docs/design/basant-home.md).</summary>
    public enum NavTab
    {
        Home,
        Friends,
        Trophies,
        Shop,
    }

    /// <summary>
    /// The bottom navigation (GDD 14.1, docs/design/basant-home.md): Home, Friends, Trophies, Shop,
    /// each a tile with its icon over its word, the active tile lit and a badge for what waits. The
    /// word is a localised text sprite where one exists, else the label.
    /// </summary>
    public sealed class NavBar : VisualElement
    {
        public const int BadgeCap = 9;
        private static readonly NavTab[] Order = { NavTab.Home, NavTab.Friends, NavTab.Trophies, NavTab.Shop };
        private readonly Dictionary<NavTab, Button> _buttons = new Dictionary<NavTab, Button>();
        private readonly Dictionary<NavTab, Label> _badges = new Dictionary<NavTab, Label>();

        public NavBar(Func<NavTab, string> label)
        {
            name = "shell-nav";
            AddToClassList("nav");
            foreach (var tab in Order)
            {
                var chosen = tab;
                var button = new Button(() => Tap(chosen)) { name = "nav-" + tab.ToString().ToLowerInvariant() };
                // Not focusable: a tapped tab would keep :focus and show the normal tile over the selected one.
                button.focusable = false;
                button.AddToClassList("nav__tab");
                var key = tab.ToString().ToLowerInvariant();
                button.Add(new NavIcon(tab));
                var art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.AddToClassList("nav__art");
                art.AddToClassList("nav__art--" + key);
                button.Add(art);
                var text = Theme.TextArt.Mark(new Label(label(tab)), "nav-" + key);
                text.AddToClassList("nav__label");
                button.Add(text);
                var badge = new Label();
                badge.AddToClassList("nav__badge");
                badge.style.display = DisplayStyle.None;
                button.Add(badge);
                _buttons[tab] = button;
                _badges[tab] = badge;
                Add(button);
            }
        }

        /// <summary>Raised when a tab is tapped, the active one included.</summary>
        public event Action<NavTab>? Chosen;

        public NavTab Active { get; private set; }

        /// <summary>What a tap on a tab does; public so it can be driven without a panel.</summary>
        public void Tap(NavTab tab) => Chosen?.Invoke(tab);

        public void Select(NavTab tab)
        {
            Active = tab;
            foreach (var pair in _buttons)
            {
                pair.Value.EnableInClassList("nav__tab--on", pair.Key == tab);
            }
        }

        /// <summary>A count on the tab; 0 hides it, more than nine shows "9+".</summary>
        public void SetBadge(NavTab tab, int count)
        {
            var badge = _badges[tab];
            badge.text = BadgeText(count);
            badge.style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public static string BadgeText(int count) => count <= 0 ? "" : count > BadgeCap ? BadgeCap + "+" : count.ToString();
    }
}
