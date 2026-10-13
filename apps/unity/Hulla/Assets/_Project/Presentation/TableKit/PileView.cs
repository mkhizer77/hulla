using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The open trick (GDD 14.3, 14.5, 14.6): cards fan in, and a card added to the trick slides from
    /// the seat that played it (220 ms); with reduce-motion it fades in instead. Only the new card is
    /// added, so a redraw never restarts a flight. When a trick is picked up the cards fly to the
    /// picker 40 ms apart; a discarded trick fades out.
    /// </summary>
    public sealed class PileView
    {
        public const int StaggerMs = 40;
        public const int PickupMs = 400;

        private readonly VisualElement _pile;
        private readonly List<string> _shown = new List<string>();
        private bool _shownFaded;
        private bool _leaving;

        public PileView(VisualElement pile)
        {
            _pile = pile;
        }

        public bool ReduceMotion { get; set; }

        /// <summary>Raised for each card as it reaches the picker during a pick-up flight.</summary>
        public event Action? CardArrived;

        /// <param name="origin">Where each card comes from, in panel space, or null for no flight.</param>
        public void Show(IReadOnlyList<(string Card, string Owner, long Seat)> plays, bool faded, Func<long, Rect?> origin)
        {
            if (_leaving)
            {
                // The last trick is on its way to the picker; the next trick starts from an empty pile.
                if (faded)
                {
                    return;
                }

                _pile.Clear();
                _shown.Clear();
                _leaving = false;
            }

            var extends = !faded && !_shownFaded && plays.Count == _shown.Count + 1;
            for (var i = 0; extends && i < _shown.Count; i++)
            {
                extends = _shown[i] == plays[i].Card;
            }

            var same = plays.Count == _shown.Count && faded == _shownFaded;
            for (var i = 0; same && i < _shown.Count; i++)
            {
                same = _shown[i] == plays[i].Card;
            }

            if (same)
            {
                return;
            }

            if (!extends)
            {
                _pile.Clear();
                _shown.Clear();
                foreach (var play in plays)
                {
                    Add(play, faded, null);
                }
            }
            else
            {
                var last = plays[plays.Count - 1];
                Add(last, false, origin(last.Seat));
            }

            _shownFaded = faded;
        }

        /// <summary>
        /// The pick-up (GDD 14.6): the cards on the pile lift and fly to <paramref name="target"/> one
        /// after another, each raising <see cref="CardArrived"/> as it lands. With reduce-motion they fade.
        /// The pile then stays empty until the next trick draws.
        /// </summary>
        public void FlyTo(Rect target)
        {
            if (_pile.childCount == 0)
            {
                return;
            }

            _leaving = true;
            var index = 0;
            foreach (var slot in _pile.Children())
            {
                var delay = index * StaggerMs;
                index++;
                var card = slot;
                if (ReduceMotion)
                {
                    card.schedule.Execute(() =>
                    {
                        card.AddToClassList("pile__slot--gone");
                        CardArrived?.Invoke();
                    }).StartingIn(delay);
                    continue;
                }

                card.schedule.Execute(() =>
                {
                    var delta = target.center - card.worldBound.center;
                    card.AddToClassList("pile__slot--pickup");
                    card.style.translate = new Translate(delta.x, delta.y);
                    card.style.scale = new Scale(new Vector2(0.5f, 0.5f));
                    card.style.opacity = 0f;
                }).StartingIn(delay);
                card.schedule.Execute(() => CardArrived?.Invoke()).StartingIn(delay + PickupMs);
            }
        }

        /// <summary>A trick nobody picked up fades to the discard.</summary>
        public void Discard()
        {
            if (_pile.childCount == 0)
            {
                return;
            }

            _leaving = true;
            foreach (var slot in _pile.Children())
            {
                slot.AddToClassList("pile__slot--gone");
            }
        }

        private void Add((string Card, string Owner, long Seat) play, bool faded, Rect? from)
        {
            _shown.Add(play.Card);
            var slot = new VisualElement();
            slot.AddToClassList("pile__slot");

            // Cards land a little askew, as on a real table; the angle depends only on the seat and
            // the position, so a redraw lays the trick out the same way.
            slot.style.rotate = new Rotate(((play.Seat * 7 + _shown.Count * 11) % 17) - 8);
            var card = new CardElement().Show(play.Card);
            card.EnableInClassList("card--faded", faded);
            slot.Add(card);
            var owner = new Label(play.Owner);
            owner.AddToClassList("pile__owner");
            slot.Add(owner);
            _pile.Add(slot);
            if (from == null)
            {
                return;
            }

            if (ReduceMotion)
            {
                slot.AddToClassList("pile__slot--fade-start");
                slot.schedule.Execute(() =>
                {
                    slot.AddToClassList("pile__slot--fade");
                    slot.RemoveFromClassList("pile__slot--fade-start");
                }).StartingIn(16);
                return;
            }

            void Fly(GeometryChangedEvent evt)
            {
                slot.UnregisterCallback<GeometryChangedEvent>(Fly);
                var delta = from.Value.center - slot.worldBound.center;
                slot.style.translate = new Translate(delta.x, delta.y);
                slot.schedule.Execute(() =>
                {
                    slot.AddToClassList("pile__slot--fly");
                    slot.style.translate = new Translate(0f, 0f);
                }).StartingIn(16);
            }

            slot.RegisterCallback<GeometryChangedEvent>(Fly);
        }
    }
}
