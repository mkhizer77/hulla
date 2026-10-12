using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>An opponent at the table: name, cards left and state (turn, out, away).</summary>
    public sealed class SeatBadge : VisualElement
    {
        private readonly Label _name = new Label();
        private readonly Label _count = new Label();
        private readonly Label _state = new Label();

        public SeatBadge()
        {
            AddToClassList("seat");
            _name.AddToClassList("seat__name");
            _count.AddToClassList("seat__count");
            _state.AddToClassList("seat__state");
            Add(_name);
            Add(_count);
            Add(_state);
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
