using System.Collections.Generic;
using System.Linq;
using Hulla.Presentation.TableKit;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Tests
{
    public sealed class HandViewHoldTests
    {
        private static List<VisualElement> Cards(VisualElement root) => root.Query<VisualElement>(className: "card").ToList();

        [Test]
        public void HeldCardsKeepTheirSlotButStayOutOfSightUntilRevealed()
        {
            var root = new VisualElement();
            var hand = new HandView(root);
            hand.Show(new[] { "AS", "KS", "7S", "3S", "2H" }, _ => false, false);
            hand.Hold(new[] { "7S", "3S" });
            var cards = Cards(root);
            Assert.AreEqual(5, cards.Count, "the picked-up cards have slots in the fan");
            Assert.AreEqual(2, cards.Count(c => c.ClassListContains("card--arriving")));
            Assert.IsTrue(hand.IsHeld("7S"));

            hand.Reveal("7S");
            cards = Cards(root);
            var seven = cards.First(c => ((CardElement)c).Card == "7S");
            Assert.IsFalse(seven.ClassListContains("card--arriving"));
            Assert.IsTrue(seven.ClassListContains("card--new"), "a landed card is lit as new (GDD 14.6)");
            Assert.AreEqual(1, cards.Count(c => c.ClassListContains("card--arriving")), "the other card is still in the air");
            Assert.IsFalse(hand.IsHeld("7S"));
        }

        [Test]
        public void ARedrawWhileCardsAreInTheAirKeepsThemHidden()
        {
            var root = new VisualElement();
            var hand = new HandView(root);
            hand.Show(new[] { "AS", "7S", "3S" }, _ => false, false);
            hand.Hold(new[] { "7S", "3S" });
            hand.Show(new[] { "AS", "7S", "3S" }, card => card == "AS", true);
            Assert.AreEqual(2, Cards(root).Count(c => c.ClassListContains("card--arriving")));
            hand.Reveal("3S");
            hand.Reveal("7S");
            Assert.AreEqual(0, Cards(root).Count(c => c.ClassListContains("card--arriving")));
        }

        [Test]
        public void RevealingACardThatWasNeverHeldDoesNothing()
        {
            var root = new VisualElement();
            var hand = new HandView(root);
            hand.Show(new[] { "AS", "KS" }, _ => false, false);
            hand.Reveal("AS");
            Assert.IsFalse(Cards(root)[0].ClassListContains("card--new"));
        }
    }
}
