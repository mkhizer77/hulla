using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Navigation
{
    /// <summary>The five tabs of the bottom navigation (GDD 14.1).</summary>
    public enum NavTab
    {
        Home,
        Friends,
        Shop,
        Pass,
        Profile,
    }

    /// <summary>
    /// The bottom navigation (GDD 14.1, design §2): Home, Friends, Shop, Pass, Profile, a drawn icon
    /// over a label, the active tab in the theme's accent and a badge for what waits.
    /// </summary>
    public sealed class NavBar : VisualElement
    {
        public const int BadgeCap = 9;
        private static readonly NavTab[] Order = { NavTab.Home, NavTab.Friends, NavTab.Shop, NavTab.Pass, NavTab.Profile };
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
                button.AddToClassList("nav__tab");
                button.Add(new NavIcon(tab));
                var text = new Label(label(tab));
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
