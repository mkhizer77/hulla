using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// An opponent at the table (GDD 14.3, 14.5, 14.7): a round avatar with their initial, a draining
    /// timer ring and a glowing border on their turn, a card-count chip that bumps as cards arrive, the
    /// crown when they lead, a short flag when they give a thulla, their name and state (out, away),
    /// and grey suit chips for the suits they are known to be void in.
    /// </summary>
    public sealed class SeatBadge : VisualElement
    {
        /// <summary>Avatar colours; a seat keeps its colour for as long as the name stays the same.</summary>
        private static readonly string[] Tints = { "seat__avatar--a", "seat__avatar--b", "seat__avatar--c", "seat__avatar--d", "seat__avatar--e", "seat__avatar--f" };

        private readonly VisualElement _avatar = new VisualElement();
        private readonly Label _initial = new Label();
        private readonly VisualElement _chip = new VisualElement();
        private readonly Label _name = new Label();
        private readonly Label _count = new Label();
        private readonly Label _state = new Label();
        private readonly Label _crown = new Label("♛");
        private readonly Label _flag = new Label();
        private readonly VisualElement _voids = new VisualElement();
        private readonly Label _bubble = new Label();
        private readonly TimerRing _timer = new TimerRing();
        private float _bubbleLeft;
        private float _flagLeft;
        private float _bumpLeft;
        private string _voidsShown = "";

        public SeatBadge()
        {
            AddToClassList("seat");
            _avatar.AddToClassList("seat__avatar");
            _timer.AddToClassList("seat__timer");
            _timer.style.display = DisplayStyle.None;
            _avatar.Add(_timer);
            _initial.AddToClassList("seat__initial");
            _avatar.Add(_initial);
            _crown.AddToClassList("seat__crown");
            _crown.style.display = DisplayStyle.None;
            _avatar.Add(_crown);
            _chip.AddToClassList("seat__chip");
            var back = new VisualElement();
            back.AddToClassList("seat__back");
            _chip.Add(back);
            _count.AddToClassList("seat__count");
            _chip.Add(_count);
            _avatar.Add(_chip);
            _name.AddToClassList("seat__name");
            _state.AddToClassList("seat__state");
            _flag.AddToClassList("seat__flag");
            _flag.style.display = DisplayStyle.None;
            _voids.AddToClassList("seat__voids");
            Add(_avatar);
            Add(_name);
            Add(_state);
            Add(_voids);
            Add(_flag);
            _bubble.AddToClassList("bubble");
            _bubble.style.display = DisplayStyle.None;
            Add(_bubble);
        }

        /// <summary>Shows a speech bubble under the badge for a few seconds.</summary>
        public void Say(string text, float seconds)
        {
            _bubble.text = text;
            _bubble.style.display = DisplayStyle.Flex;
            _bubbleLeft = seconds;
        }

        /// <summary>A short red flag on the seat ("THULLA") for the moment (GDD 14.5).</summary>
        public void Flag(string text, float seconds)
        {
            _flag.text = text;
            _flag.style.display = DisplayStyle.Flex;
            _flagLeft = seconds;
        }

        /// <summary>The count chip pops as a picked-up card lands (GDD 14.6).</summary>
        public void Bump()
        {
            _chip.AddToClassList("seat__chip--bump");
            _bumpLeft = 0.12f;
        }

        /// <summary>The timer ring on this seat's avatar while it is their turn; hidden otherwise.</summary>
        public void ShowTimer(float fraction, float secondsLeft, float pulse)
        {
            _timer.style.display = DisplayStyle.Flex;
            _timer.Show(fraction, secondsLeft);
            _timer.style.scale = new Scale(new Vector2(pulse, pulse));
        }

        public void HideTimer() => _timer.style.display = DisplayStyle.None;

        public void Tick(float deltaSeconds)
        {
            if (_bubbleLeft > 0f)
            {
                _bubbleLeft -= deltaSeconds;
                if (_bubbleLeft <= 0f)
                {
                    _bubble.style.display = DisplayStyle.None;
                }
            }

            if (_flagLeft > 0f)
            {
                _flagLeft -= deltaSeconds;
                if (_flagLeft <= 0f)
                {
                    _flag.style.display = DisplayStyle.None;
                }
            }

            if (_bumpLeft > 0f)
            {
                _bumpLeft -= deltaSeconds;
                if (_bumpLeft <= 0f)
                {
                    _chip.RemoveFromClassList("seat__chip--bump");
                }
            }
        }

        /// <param name="leader">Wears the crown (GDD 14.5).</param>
        /// <param name="voids">Suits the seat is known to be void in, or empty when the assist is off (GDD 14.7).</param>
        public void Show(string name, long cards, bool turn, string state, bool leader = false, IReadOnlyCollection<char>? voids = null)
        {
            _name.text = name;
            _initial.text = name.Length == 0 ? "?" : name.Substring(0, 1).ToUpperInvariant();
            var tint = Tints[(int)((uint)StableHash(name) % (uint)Tints.Length)];
            foreach (var t in Tints)
            {
                _avatar.EnableInClassList(t, t == tint);
            }

            _count.text = cards.ToString();
            _state.text = state;
            _state.style.display = state.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _crown.style.display = leader ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("seat--turn", turn);
            EnableInClassList("seat--leader", leader);
            _state.EnableInClassList("seat__state--away", state == Text.UiText.SeatAwayAutoPlay || state == Text.UiText.SeatAutoPlay);
            _state.EnableInClassList("seat__state--out", state == Text.UiText.Escaped);
            EnableInClassList("seat--out", state == Text.UiText.Escaped);
            if (!turn)
            {
                HideTimer();
            }

            ShowVoids(voids);
        }

        private void ShowVoids(IReadOnlyCollection<char>? voids)
        {
            var key = "";
            if (voids != null)
            {
                foreach (var suit in new[] { 'S', 'H', 'D', 'C' })
                {
                    if (voids.Contains(suit))
                    {
                        key += suit;
                    }
                }
            }

            if (key == _voidsShown)
            {
                return;
            }

            _voidsShown = key;
            _voids.Clear();
            _voids.style.display = key.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (var suit in key)
            {
                var chip = new Label(CardElement.SuitGlyph(suit));
                chip.AddToClassList("seat__void");
                _voids.Add(chip);
            }
        }

        /// <summary>The same on every run and platform, unlike string.GetHashCode.</summary>
        private static int StableHash(string text)
        {
            var hash = 17;
            foreach (var c in text)
            {
                hash = unchecked(hash * 31 + c);
            }

            return hash;
        }
    }
}
