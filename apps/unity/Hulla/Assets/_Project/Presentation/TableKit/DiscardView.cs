using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The discard pile on the felt (GDD 14.3): a face-down stack with the count of cards out of play
    /// this round. A trick nobody picked up flies here card by card, and the count climbs with a small
    /// pop as each one lands, so cards still in the air are not counted yet.
    /// </summary>
    public sealed class DiscardView
    {
        public const int BumpMs = 120;

        /// <summary>Cards announced with <see cref="Expect"/> count themselves after this, flight or not.</summary>
        public const int SettleMs = 2500;

        private readonly VisualElement _root;
        private readonly VisualElement _stack;
        private readonly Label _count;
        private readonly System.Collections.Generic.List<VisualElement> _cards = new System.Collections.Generic.List<VisualElement>();
        private long _known;
        private int _inFlight;

        public DiscardView(VisualElement root, string label)
        {
            _root = root;
            _stack = new VisualElement();
            _stack.AddToClassList("discard__stack");
            for (var i = 1; i <= 3; i++)
            {
                var card = new VisualElement();
                card.AddToClassList("discard__card");
                card.AddToClassList("discard__card--" + i);
                card.pickingMode = PickingMode.Ignore;
                _stack.Add(card);
                _cards.Add(card);
            }

            _root.Add(_stack);
            var text = new VisualElement();
            text.AddToClassList("discard__text");
            _root.Add(text);
            _count = new Label();
            _count.AddToClassList("discard__count");
            text.Add(_count);
            var caption = new Label(label);
            caption.AddToClassList("discard__label");
            text.Add(caption);
            Apply();
        }

        /// <summary>The stack shows the player's own card back (GDD 12.11); null keeps the default.</summary>
        public void SetBack(string? cardBack)
        {
            foreach (var card in _cards)
            {
                Theme.CosmeticArt.Apply(card, cardBack);
            }
        }

        /// <summary>The count on the stack: the model's count less the cards still flying in.</summary>
        public long Shown => _known - _inFlight;

        /// <summary>Where a discarded card flies to, in panel space.</summary>
        public Rect Target => _stack.worldBound;

        /// <summary>The model's count. A smaller count is a new round or a draw from the discard, shown at once.</summary>
        public void Show(long count)
        {
            if (count < _known)
            {
                _inFlight = 0;
            }

            _known = count;
            if (_inFlight > _known)
            {
                _inFlight = (int)_known;
            }

            Apply();
        }

        /// <summary>Cards about to fly here: they stay uncounted until each one <see cref="Arrive"/>s.</summary>
        public void Expect(int cards)
        {
            _inFlight += cards;
            Apply();
            _root.schedule.Execute(() =>
            {
                if (_inFlight > 0)
                {
                    _inFlight = 0;
                    Apply();
                }
            }).StartingIn(SettleMs);
        }

        /// <summary>A card landed: the count climbs and pops.</summary>
        public void Arrive()
        {
            if (_inFlight == 0)
            {
                return;
            }

            _inFlight--;
            Apply();
            _count.AddToClassList("discard__count--bump");
            _count.schedule.Execute(() => _count.RemoveFromClassList("discard__count--bump")).StartingIn(BumpMs);
        }

        private void Apply()
        {
            var shown = Shown;
            _count.text = shown.ToString();
            _root.style.display = _known > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _stack.EnableInClassList("discard__stack--empty", shown <= 0);
        }
    }
}
