using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The open trick (GDD 14.3, 14.5, 14.6): cards fan in, and a card added to the trick slides from
    /// the seat that played it (220 ms); with reduce-motion it fades in instead. Only the new card is
    /// added, so a redraw never restarts a flight. When the trick resolves the cards leave 40 ms apart:
    /// to the picker's badge, into the slots the picker's own hand made for them, or to the discard.
    /// </summary>
    public sealed class PileView
    {
        /// <summary>Where a resolved trick goes.</summary>
        public enum Flight
        {
            /// <summary>Shrinks into an opponent's badge.</summary>
            Pickup,

            /// <summary>Glides at full size into the player's hand, each card to its own slot.</summary>
            SlotIn,

            /// <summary>Shrinks into the discard stack.</summary>
            Discard,
        }

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

        /// <summary>Raised with the card as it lands at the end of a flight.</summary>
        public event Action<string>? CardArrived;

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
        /// The trick leaves (GDD 14.6): the cards lift and fly one after another, each raising
        /// <see cref="CardArrived"/> as it lands. <paramref name="targetOf"/> gives a card its own
        /// destination (a slot in the hand), <paramref name="fallback"/> serves the rest. With
        /// reduce-motion they fade. The pile then stays empty until the next trick draws.
        /// </summary>
        public void FlyTo(Flight flight, Func<string, Rect?> targetOf, Rect fallback)
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
                var face = slot.Q<CardElement>();
                var id = face?.Card ?? "";
                if (ReduceMotion)
                {
                    card.schedule.Execute(() =>
                    {
                        card.AddToClassList("pile__slot--gone");
                        CardArrived?.Invoke(id);
                    }).StartingIn(delay);
                    continue;
                }

                card.schedule.Execute(() =>
                {
                    var target = targetOf(id) ?? fallback;
                    var from = face?.worldBound ?? card.worldBound;
                    var delta = target.center - from.center;
                    if (flight == Flight.SlotIn)
                    {
                        // The card keeps its size and straightens as it settles into the fan; the
                        // owner's tag stays behind.
                        card.Q<Label>(className: "pile__owner")?.RemoveFromHierarchy();
                        card.AddToClassList("pile__slot--slot-in");
                        card.style.rotate = new Rotate(0f);
                    }
                    else
                    {
                        card.AddToClassList("pile__slot--pickup");
                        var shrink = flight == Flight.Discard ? 0.45f : 0.5f;
                        card.style.scale = new Scale(new Vector2(shrink, shrink));
                    }

                    card.style.translate = new Translate(delta.x, delta.y);
                    card.style.opacity = 0f;
                }).StartingIn(delay);
                card.schedule.Execute(() => CardArrived?.Invoke(id)).StartingIn(delay + PickupMs);
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
