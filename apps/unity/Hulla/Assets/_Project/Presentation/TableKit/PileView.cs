using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The open trick (GDD 14.3, 14.5): cards fan in, and a card added to the trick slides from the
    /// seat that played it (220 ms); with reduce-motion it fades in instead. Only the new card is
    /// added, so a redraw never restarts a flight.
    /// </summary>
    public sealed class PileView
    {
        private readonly VisualElement _pile;
        private readonly List<string> _shown = new List<string>();
        private bool _shownFaded;

        public PileView(VisualElement pile)
        {
            _pile = pile;
        }

        public bool ReduceMotion { get; set; }

        /// <param name="origin">Where each card comes from, in panel space, or null for no flight.</param>
        public void Show(IReadOnlyList<(string Card, string Owner, long Seat)> plays, bool faded, Func<long, Rect?> origin)
        {
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

        private void Add((string Card, string Owner, long Seat) play, bool faded, Rect? from)
        {
            _shown.Add(play.Card);
            var slot = new VisualElement();
            slot.AddToClassList("pile__slot");
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
