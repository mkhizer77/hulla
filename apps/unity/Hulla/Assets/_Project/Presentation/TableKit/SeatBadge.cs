using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// An opponent at the table (GDD 14.3): a round avatar with their initial, a glowing ring on their
    /// turn, a card-count chip with a card back, their name and state (leads, out, away).
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
        private readonly Label _bubble = new Label();
        private float _bubbleLeft;

        public SeatBadge()
        {
            AddToClassList("seat");
            _avatar.AddToClassList("seat__avatar");
            _initial.AddToClassList("seat__initial");
            _avatar.Add(_initial);
            _chip.AddToClassList("seat__chip");
            var back = new VisualElement();
            back.AddToClassList("seat__back");
            _chip.Add(back);
            _count.AddToClassList("seat__count");
            _chip.Add(_count);
            _avatar.Add(_chip);
            _name.AddToClassList("seat__name");
            _state.AddToClassList("seat__state");
            Add(_avatar);
            Add(_name);
            Add(_state);
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

        public void Tick(float deltaSeconds)
        {
            if (_bubbleLeft <= 0f)
            {
                return;
            }

            _bubbleLeft -= deltaSeconds;
            if (_bubbleLeft <= 0f)
            {
                _bubble.style.display = DisplayStyle.None;
            }
        }

        public void Show(string name, long cards, bool turn, string state)
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
            EnableInClassList("seat--turn", turn);
            _state.EnableInClassList("seat__state--lead", state == Text.UiText.Lead);
            _state.EnableInClassList("seat__state--away", state == Text.UiText.SeatAwayAutoPlay || state == Text.UiText.SeatAutoPlay);
            _state.EnableInClassList("seat__state--out", state == Text.UiText.Escaped);
            EnableInClassList("seat--out", state == Text.UiText.Escaped);
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
