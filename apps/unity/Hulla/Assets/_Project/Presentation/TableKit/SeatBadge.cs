using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>An opponent at the table: name, cards left and state (turn, out, away).</summary>
    public sealed class SeatBadge : VisualElement
    {
        private readonly Label _name = new Label();
        private readonly Label _count = new Label();
        private readonly Label _state = new Label();
        private readonly Label _bubble = new Label();
        private float _bubbleLeft;

        public SeatBadge()
        {
            AddToClassList("seat");
            _name.AddToClassList("seat__name");
            _count.AddToClassList("seat__count");
            _state.AddToClassList("seat__state");
            Add(_name);
            Add(_count);
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
            _count.text = cards.ToString();
            _state.text = state;
            _state.style.display = state.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            EnableInClassList("seat--turn", turn);
        }
    }
}
