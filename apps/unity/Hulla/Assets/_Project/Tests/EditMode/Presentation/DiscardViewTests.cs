using Hulla.Presentation.TableKit;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Tests
{
    public sealed class DiscardViewTests
    {
        private static (DiscardView View, VisualElement Root) Make()
        {
            var root = new VisualElement();
            return (new DiscardView(root, "Discard"), root);
        }

        [Test]
        public void AnEmptyDiscardIsNotShown()
        {
            var (view, root) = Make();
            view.Show(0);
            Assert.AreEqual(DisplayStyle.None, root.style.display.value);
            view.Show(3);
            Assert.AreEqual(DisplayStyle.Flex, root.style.display.value);
            Assert.AreEqual("3", root.Q<Label>(className: "discard__count").text);
        }

        [Test]
        public void CardsInFlightCountOnlyAsTheyLand()
        {
            var (view, root) = Make();
            var count = root.Q<Label>(className: "discard__count");
            view.Show(4);
            view.Expect(3);
            view.Show(7);
            Assert.AreEqual("4", count.text, "three cards are still in the air");
            view.Arrive();
            Assert.AreEqual("5", count.text);
            Assert.IsTrue(count.ClassListContains("discard__count--bump"), "the count pops as a card lands");
            view.Arrive();
            view.Arrive();
            Assert.AreEqual("7", count.text);
            view.Arrive();
            Assert.AreEqual("7", count.text, "a stray arrival changes nothing");
        }

        [Test]
        public void ANewRoundOrADrawFromTheDiscardShowsTheSmallerCountAtOnce()
        {
            var (view, root) = Make();
            var count = root.Q<Label>(className: "discard__count");
            view.Show(5);
            view.Expect(2);
            view.Show(7);
            view.Show(6);
            Assert.AreEqual("6", count.text, "R26: the drawn card left the stack");
            view.Show(0);
            Assert.AreEqual(0, view.Shown);
            Assert.AreEqual(DisplayStyle.None, root.style.display.value);
        }
    }
}
