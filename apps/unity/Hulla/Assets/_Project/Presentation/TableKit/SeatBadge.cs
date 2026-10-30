using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// A seat at the table (GDD 14.3, 14.5, 14.7): a round avatar (GDD 15.6, or their initial until it is
    /// known), a "You" tag on the player's own seat, a draining timer
    /// ring and a glowing border on their turn, a card-count chip that bumps as cards arrive, the crown
    /// when they lead, a short flag when they give a thulla, their name and state (out, away), and grey
    /// suit chips for the suits they are known to be void in. In the lobby (GDD 14.9) the same badge
    /// shows the host's crown, a Ready tick and dotted empty seats that invite.
    /// </summary>
    public sealed class SeatBadge : VisualElement
    {
        private readonly List<VisualElement> _backs = new List<VisualElement>();
        private readonly VisualElement _avatar = new VisualElement();
        private readonly Label _initial = new Label();
        private readonly Label _you = new Label(Text.UiText.You);
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

            // The theme's frame round the face (docs/design/table-themes.md), over the picture.
            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("seat__frame");
            _avatar.Add(frame);
            _you.AddToClassList("seat__you");
            _you.style.display = DisplayStyle.None;
            _avatar.Add(_you);
            _crown.AddToClassList("seat__crown");
            _crown.style.display = DisplayStyle.None;
            _avatar.Add(_crown);
            _chip.AddToClassList("seat__chip");
            // A small fan of card backs beside the face, the count on top: cards in hand at a glance.
            for (var k = 1; k <= 3; k++)
            {
                var back = new VisualElement { pickingMode = PickingMode.Ignore };
                back.AddToClassList("seat__back");
                back.AddToClassList("seat__back--" + k);
                _chip.Add(back);
                _backs.Add(back);
            }

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
            ShowEmote(_bubble, null);
            _bubble.text = text;
            _bubble.style.display = DisplayStyle.Flex;
            _bubbleLeft = seconds;
        }

        /// <summary>An emote's sticker by the badge for a few seconds (GDD 11.3): sent here, or thrown at this seat.</summary>
        public void SayEmote(string emoteId, float seconds)
        {
            ShowEmote(_bubble, emoteId);
            _bubble.style.display = DisplayStyle.Flex;
            _bubbleLeft = seconds;
        }

        /// <summary>Turns a speech bubble into an emote's sticker, or back into a text bubble for null.</summary>
        public static void ShowEmote(Label bubble, string? emoteId)
        {
            if (emoteId != null)
            {
                bubble.text = "";
            }

            Theme.EmoteArt.Apply(bubble, emoteId);
            bubble.EnableInClassList("bubble--emote", emoteId != null);
        }

        /// <summary>The emote the bubble shows, or null for text.</summary>
        public bool ShowsEmote(string emoteId) => _bubble.style.display == DisplayStyle.Flex && _bubble.ClassListContains(Theme.EmoteArt.Prefix + emoteId);

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
            _timer.Show(fraction, secondsLeft, pulse);
        }

        /// <summary>The fan beside the face shows the seat's own card back (GDD 12.11); null keeps the default.</summary>
        public void ShowBack(string? cardBack)
        {
            foreach (var back in _backs)
            {
                Theme.CosmeticArt.Apply(back, cardBack);
            }
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

        /// <summary>The name on the badge.</summary>
        public string Name => _name.text;

        /// <summary>Whether the badge carries the "You" tag.</summary>
        public bool ShowsYou => _you.style.display == DisplayStyle.Flex;

        /// <summary>Whether the circle shows a face from the avatar set rather than the tinted initial.</summary>
        /// <summary>A ring while this player's voice is heard (GDD 11.4).</summary>
        public void SetSpeaking(bool speaking) => EnableInClassList("seat--speaking", speaking);

        public bool ShowsSpeaking => ClassListContains("seat--speaking");

        public bool HasImage => _avatar.ClassListContains(AvatarArt.ImageClass);

        /// <param name="leader">Wears the crown (GDD 14.5).</param>
        /// <param name="voids">Suits the seat is known to be void in, or empty when the assist is off (GDD 14.7).</param>
        /// <param name="avatar">The seat's avatar id (GDD 15.6); null keeps the tinted initial.</param>
        /// <param name="me">The player's own seat: tagged "You".</param>
        public void Show(string name, long cards, bool turn, string state, bool leader = false, IReadOnlyCollection<char>? voids = null, string? avatar = null, bool me = false)
        {
            Identity(name, avatar, me);
            RemoveFromClassList("seat--empty");
            _chip.style.display = DisplayStyle.Flex;
            _count.text = cards.ToString();
            _state.text = state;
            _state.style.display = state.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _crown.style.display = leader ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("seat--turn", turn);
            EnableInClassList("seat--leader", leader);
            _state.EnableInClassList("seat__state--ready", false);
            _state.EnableInClassList("seat__state--away", state == Text.UiText.SeatAwayAutoPlay || state == Text.UiText.SeatAutoPlay);
            _state.EnableInClassList("seat__state--out", state == Text.UiText.Escaped);
            EnableInClassList("seat--out", state == Text.UiText.Escaped);
            if (!turn)
            {
                HideTimer();
            }

            ShowVoids(voids);
        }

        /// <summary>
        /// The lobby face of the seat (GDD 14.9): no cards, the host's crown, a Ready tick; an empty
        /// seat is a dotted circle that says Invite.
        /// </summary>
        public void ShowLobby(string? name, bool host, bool ready, string? avatar = null, bool me = false)
        {
            var empty = name == null;
            EnableInClassList("seat--empty", empty);
            EnableInClassList("seat--turn", false);
            EnableInClassList("seat--leader", host && !empty);
            EnableInClassList("seat--out", false);
            HideTimer();
            _chip.style.display = DisplayStyle.None;
            ShowVoids(null);
            if (empty)
            {
                AvatarArt.Clear(_avatar, _initial);
                _you.style.display = DisplayStyle.None;
                EnableInClassList("seat--me", false);
                _initial.text = "+";
                _name.text = Text.UiText.Invite;
                _crown.style.display = DisplayStyle.None;
                _state.style.display = DisplayStyle.None;
                return;
            }

            Identity(name!, avatar, me);
            _crown.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            _state.text = ready ? Text.UiText.Ready : "";
            _state.style.display = ready ? DisplayStyle.Flex : DisplayStyle.None;
            _state.EnableInClassList("seat__state--ready", ready);
            _state.EnableInClassList("seat__state--away", false);
            _state.EnableInClassList("seat__state--out", false);
        }

        private void Identity(string name, string? avatar, bool me)
        {
            _name.text = name;
            AvatarArt.Apply(_avatar, _initial, avatar, name);
            _you.style.display = me ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList("seat--me", me);
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
    }
}
