using System;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Lobby
{
    /// <summary>
    /// One seat on the lobby rug (docs/design/basant-lobby.md): the face in its rim with the host's crown
    /// and the ready tick, the name plate under it and the status tag; an empty seat is the dashed
    /// circle with Invite on the plate, and a tap on it invites.
    /// </summary>
    public sealed class LobbySeat : Button
    {
        private readonly VisualElement _avatar;
        private readonly Label _initial;
        private readonly VisualElement _frame;
        private readonly VisualElement _crown;
        private readonly VisualElement _tick;
        private readonly Label _name;
        private readonly Label _tag;

        public LobbySeat()
        {
            text = "";
            AddToClassList("lobby__seat");
            _avatar = new VisualElement { pickingMode = PickingMode.Ignore };
            _avatar.AddToClassList("seat__avatar");
            _avatar.AddToClassList("lobby__avatar");
            _initial = new Label { pickingMode = PickingMode.Ignore };
            _initial.AddToClassList("seat__initial");
            _initial.AddToClassList("lobby__initial");
            _avatar.Add(_initial);
            Add(_avatar);
            _frame = new VisualElement { pickingMode = PickingMode.Ignore };
            _frame.AddToClassList("lobby__frame");
            Add(_frame);
            _crown = new VisualElement { pickingMode = PickingMode.Ignore };
            _crown.AddToClassList("lobby__crown");
            Add(_crown);
            _tick = new VisualElement { pickingMode = PickingMode.Ignore };
            _tick.AddToClassList("lobby__tick");
            Add(_tick);
            _name = new Label { pickingMode = PickingMode.Ignore };
            _name.AddToClassList("lobby__name");
            Add(_name);
            _tag = new Label { pickingMode = PickingMode.Ignore };
            _tag.AddToClassList("lobby__tag");
            Add(_tag);
        }

        /// <summary>The empty seat: dashed circle, Invite on the plate.</summary>
        public void ShowEmpty()
        {
            EnableInClassList("lobby__seat--empty", true);
            EnableInClassList("lobby__seat--me", false);
            AvatarArt.Clear(_avatar, _initial);
            _initial.text = "+";
            _name.text = UiText.Invite;
            _crown.style.display = DisplayStyle.None;
            _tick.style.display = DisplayStyle.None;
            _tag.style.display = DisplayStyle.None;
        }

        public void Show(string name, bool host, bool ready, string? avatar, bool me, bool bot = false)
        {
            EnableInClassList("lobby__seat--empty", false);
            EnableInClassList("lobby__seat--me", me);
            AvatarArt.Apply(_avatar, _initial, avatar, name);
            _name.text = me ? UiText.You : name;
            _crown.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            _tick.style.display = ready ? DisplayStyle.Flex : DisplayStyle.None;
            _tag.style.display = DisplayStyle.Flex;
            _tag.text = bot ? UiText.BotTag : host ? UiText.Host : ready ? UiText.Ready : UiText.NotReady;
            _tag.EnableInClassList("lobby__tag--host", host);
            _tag.EnableInClassList("lobby__tag--ready", !host && ready);
            _tag.EnableInClassList("lobby__tag--waiting", !host && !ready);
        }

        /// <summary>
        /// Where seat <paramref name="index"/> of <paramref name="count"/> sits on the rug, the player's
        /// own seat at the bottom and the rest clockwise; a rounded square so six seats reach the corners.
        /// </summary>
        public static (float X, float Y) Centre(int index, int count)
        {
            const float cx = 540f, cy = 876f, rx = 300f, ry = 272f;
            var angle = (90f + 360f * index / Math.Max(1, count)) * Math.PI / 180.0;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var x = cx + rx * Math.Sign(cos) * Math.Pow(Math.Abs(cos), 0.6);
            var y = cy + ry * Math.Sign(sin) * Math.Pow(Math.Abs(sin), 0.6);
            return ((float)x, (float)y);
        }
    }
}
