using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The player's own face and name in a row (GDD 15.6), tagged "You" so the seat reads as theirs at a
    /// glance. Their turn shows on the badge like an opponent's (GDD 14.5): the gold ring, the draining
    /// timer around the face, and the crown while they lead. No "your turn" text is needed.
    /// </summary>
    public sealed class PlayerBadge : VisualElement
    {
        private readonly VisualElement _avatar = new VisualElement();
        private readonly Label _initial = new Label();
        private readonly Label _you = new Label(Text.UiText.You);
        private readonly Label _crown = new Label(Text.UiText.Crown);
        private readonly TimerRing _timer = new TimerRing();
        private readonly Label _name = new Label();
        private readonly Label _state = new Label();

        public PlayerBadge()
        {
            AddToClassList("seat");
            AddToClassList("me-badge");
            _avatar.AddToClassList("seat__avatar");
            _timer.AddToClassList("seat__timer");
            _timer.style.display = DisplayStyle.None;
            _avatar.Add(_timer);
            _initial.AddToClassList("seat__initial");
            _avatar.Add(_initial);
            _crown.AddToClassList("seat__crown");
            _crown.style.display = DisplayStyle.None;
            _avatar.Add(_crown);
            _you.AddToClassList("seat__you");
            _you.style.display = DisplayStyle.None;
            _avatar.Add(_you);
            _name.AddToClassList("seat__name");
            _state.AddToClassList("seat__state");
            _state.style.display = DisplayStyle.None;
            Add(_avatar);
            Add(_name);
            Add(_state);
        }

        /// <summary>The state line under the name, such as "Out · watching"; empty when there is none.</summary>
        public string State => _state.style.display == DisplayStyle.None ? "" : _state.text;

        public string Name => _name.text;

        public bool ShowsYou => _you.style.display == DisplayStyle.Flex;

        public bool HasImage => _avatar.ClassListContains(AvatarArt.ImageClass);

        public bool ShowsCrown => _crown.style.display == DisplayStyle.Flex;

        public bool ShowsTimer => _timer.style.display == DisplayStyle.Flex;

        /// <param name="turn">It is this player's turn: the ring lights up.</param>
        /// <param name="leader">This player leads the trick: the crown (GDD 14.5).</param>
        /// <param name="state">A line under the name, such as "Out · watching" once the hand is empty (R25).</param>
        public void Show(string name, string? avatar, bool me, bool turn, bool leader = false, string state = "")
        {
            _name.text = name;
            _state.text = state;
            _state.style.display = state.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _state.EnableInClassList("seat__state--out", state.Length > 0);
            EnableInClassList("seat--out", state.Length > 0);
            AvatarArt.Apply(_avatar, _initial, avatar, name);
            _you.style.display = me ? DisplayStyle.Flex : DisplayStyle.None;
            _crown.style.display = leader ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("seat--me", me);
            EnableInClassList("seat--turn", turn);
            EnableInClassList("seat--leader", leader);
            if (!turn)
            {
                HideTimer();
            }
        }

        /// <summary>The timer ring around the face while it is this player's turn.</summary>
        public void ShowTimer(float fraction, float secondsLeft, float pulse)
        {
            _timer.style.display = DisplayStyle.Flex;
            _timer.Show(fraction, secondsLeft);
            _timer.style.scale = new Scale(new Vector2(pulse, pulse));
        }

        public void HideTimer() => _timer.style.display = DisplayStyle.None;
    }
}
