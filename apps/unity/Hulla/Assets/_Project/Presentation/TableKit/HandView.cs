using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The player's fanned hand (GDD 14.4): at least a 22 px strip per card, rows when it must wrap,
    /// legal cards lit and the rest dimmed on your turn. Tap to raise, tap again or flick up to play;
    /// <see cref="OneTap"/> plays on the first tap. Dragging along the fan magnifies the card under
    /// the finger and releasing raises it. A lone legal card raises itself.
    /// </summary>
    public sealed class HandView
    {
        public const float NormalCardWidth = 150f;
        public const float LargeCardWidth = 180f;

        /// <summary>Space kept at each side of the hand for the tilted end cards.</summary>
        private const float ArcMargin = 36f;
        private const float FlickPixels = 40f;

        private readonly VisualElement _hand;
        private readonly List<CardElement> _cards = new List<CardElement>();
        private IReadOnlyList<string> _shown = Array.Empty<string>();
        private Func<string, bool> _legal = _ => false;
        private bool _myTurn;
        private string? _raised;
        private CardElement? _pressed;
        private float _pressY;
        private float _width;

        public HandView(VisualElement hand)
        {
            _hand = hand;
            _hand.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (Math.Abs(evt.newRect.width - _width) > 1f)
                {
                    _width = evt.newRect.width;
                    Rebuild();
                }
            });
            _hand.RegisterCallback<PointerMoveEvent>(OnMove);
            _hand.RegisterCallback<PointerLeaveEvent>(_ => Hover(null));
        }

        /// <summary>Raised when the player plays a card.</summary>
        public event Action<string>? Played;

        /// <summary>Raised when the player taps a card that can't be played now.</summary>
        public event Action<string>? Refused;

        public bool OneTap { get; set; }

        /// <summary>Must match the card width the theme gives (.card, or .cards--large .card).</summary>
        public float CardWidth { get; private set; } = NormalCardWidth;

        public void SetLargeCards(bool large)
        {
            var width = large ? LargeCardWidth : NormalCardWidth;
            if (System.Math.Abs(width - CardWidth) > 0.1f)
            {
                CardWidth = width;
                Rebuild();
            }
        }

        public void Show(IReadOnlyList<string> cards, Func<string, bool> legal, bool myTurn)
        {
            _shown = cards;
            _legal = legal;
            _myTurn = myTurn;
            if (_raised != null && (!myTurn || !legal(_raised)))
            {
                _raised = null;
            }

            if (myTurn && _raised == null)
            {
                string? only = null;
                var count = 0;
                foreach (var card in cards)
                {
                    if (legal(card))
                    {
                        only = card;
                        count++;
                    }
                }

                if (count == 1)
                {
                    _raised = only;
                }
            }

            Rebuild();
        }

        private void Rebuild()
        {
            _hand.Clear();
            _cards.Clear();
            // Leave room for the arc: the end cards tilt outwards past their slot.
            var width = (_width > 0f ? _width : 1000f) - 2f * ArcMargin;
            var fan = HandFan.For(_shown.Count, width, CardWidth);
            VisualElement? row = null;
            for (var i = 0; i < _shown.Count; i++)
            {
                if (i % fan.PerRow == 0)
                {
                    row?.AddToClassList("hand__row--stacked");
                    row = new VisualElement();
                    row.AddToClassList("hand__row");
                    _hand.Add(row);
                }

                var card = _shown[i];
                var element = new CardElement().Show(card);
                var legal = _myTurn && _legal(card);
                element.EnableInClassList("card--legal", legal);
                element.EnableInClassList("card--dim", _myTurn && !legal);
                element.EnableInClassList("card--raised", card == _raised);
                if (i % fan.PerRow != 0)
                {
                    element.style.marginLeft = fan.Strip - CardWidth;
                }

                // A gentle arc, as a hand is held: cards tilt away from the middle and sit lower.
                var inRow = Math.Min(fan.PerRow, _shown.Count - (i / fan.PerRow) * fan.PerRow);
                var offset = (i % fan.PerRow) - (inRow - 1) / 2f;
                var spread = Math.Min(3f, 30f / Math.Max(inRow, 1));
                element.style.rotate = new Rotate(offset * spread);
                element.style.marginTop = Math.Min(offset * offset * spread * 0.9f, 40f);

                element.RegisterCallback<PointerDownEvent>(evt =>
                {
                    _pressed = element;
                    _pressY = evt.position.y;
                });
                element.RegisterCallback<PointerUpEvent>(evt => OnUp(element, evt.position.y));
                _cards.Add(element);
                row!.Add(element);
            }
        }

        private void OnUp(CardElement element, float y)
        {
            var card = element.Card;
            var flick = _pressed != null && _pressY - y > FlickPixels;
            var dragged = _pressed != null && _pressed != element;
            _pressed = null;
            Hover(null);
            if (!_myTurn || !_legal(card))
            {
                Refused?.Invoke(card);
                return;
            }

            if (flick || OneTap || (!dragged && card == _raised))
            {
                _raised = null;
                Played?.Invoke(card);
                return;
            }

            _raised = card;
            foreach (var other in _cards)
            {
                other.EnableInClassList("card--raised", other.Card == card);
            }
        }

        private void OnMove(PointerMoveEvent evt)
        {
            if (_pressed == null)
            {
                return;
            }

            Hover(evt.target as CardElement ?? (evt.target as VisualElement)?.GetFirstAncestorOfType<CardElement>());
        }

        private void Hover(CardElement? over)
        {
            foreach (var card in _cards)
            {
                card.EnableInClassList("card--hover", card == over);
            }
        }
    }
}
