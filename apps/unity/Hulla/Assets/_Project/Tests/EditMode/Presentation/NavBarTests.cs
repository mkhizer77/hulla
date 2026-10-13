using Hulla.Presentation.Navigation;
using Hulla.Presentation.Text;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Tests
{
    /// <summary>The bottom navigation (GDD 14.1): five tabs in order, one lit, badges that cap at 9+.</summary>
    public sealed class NavBarTests
    {
        [Test]
        public void FiveTabsInTheGddOrder()
        {
            var bar = new NavBar(UiText.NavLabel, UiText.NavGlyph);
            var names = new System.Collections.Generic.List<string>();
            foreach (var child in bar.Children())
            {
                names.Add(child.name);
            }

            CollectionAssert.AreEqual(new[] { "nav-home", "nav-friends", "nav-shop", "nav-pass", "nav-profile" }, names);
        }

        [Test]
        public void SelectingATabLightsItAlone()
        {
            var bar = new NavBar(UiText.NavLabel, UiText.NavGlyph);
            bar.Select(NavTab.Shop);
            Assert.AreEqual(NavTab.Shop, bar.Active);
            foreach (var child in bar.Children())
            {
                Assert.AreEqual(child.name == "nav-shop", child.ClassListContains("nav__tab--on"), child.name);
            }
        }

        [Test]
        public void TappingRaisesTheTab()
        {
            var bar = new NavBar(UiText.NavLabel, UiText.NavGlyph);
            NavTab? chosen = null;
            bar.Chosen += tab => chosen = tab;
            Assert.IsNotNull(bar.Q<Button>("nav-pass"), "the tab is a button");
            bar.Tap(NavTab.Pass);
            Assert.AreEqual(NavTab.Pass, chosen);
        }

        [Test]
        public void BadgesHideAtZeroAndCapAtNinePlus()
        {
            Assert.AreEqual("", NavBar.BadgeText(0));
            Assert.AreEqual("3", NavBar.BadgeText(3));
            Assert.AreEqual("9", NavBar.BadgeText(9));
            Assert.AreEqual("9+", NavBar.BadgeText(12));
            var bar = new NavBar(UiText.NavLabel, UiText.NavGlyph);
            bar.SetBadge(NavTab.Friends, 2);
            var badge = bar.Q<Button>("nav-friends").Q<Label>(className: "nav__badge");
            Assert.AreEqual("2", badge.text);
            Assert.AreEqual(DisplayStyle.Flex, badge.style.display.value);
            bar.SetBadge(NavTab.Friends, 0);
            Assert.AreEqual(DisplayStyle.None, badge.style.display.value);
        }
    }
}
