using System;
using Hulla.Application.Table;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Lobby
{
    /// <summary>The private room before a round: the code to share, who is seated and ready, and Start for the host.</summary>
    public sealed class LobbyView : IDisposable
    {
        private readonly TableClient _client;
        private readonly Label _code;
        private readonly VisualElement _seats;
        private readonly Button _ready;
        private readonly Button _start;
        private readonly Label _status;
        private readonly Label _countdown;
        private readonly Func<long> _nowMs;
        private long _shownSeconds = -1;

        public LobbyView(VisualElement root, TableClient client, Func<long> nowMs, Action leave)
        {
            _client = client;
            _nowMs = nowMs;
            _countdown = root.Q<Label>("countdown");
            root.Q<Label>("code-label").text = UiText.RoomCode;
            _code = root.Q<Label>("code");
            _seats = root.Q<VisualElement>("seats");
            _ready = root.Q<Button>("ready");
            _start = root.Q<Button>("start");
            _start.text = UiText.Start;
            _status = root.Q<Label>("status");
            var leaveButton = root.Q<Button>("leave");
            leaveButton.text = UiText.Leave;
            leaveButton.clicked += leave;
            _ready.clicked += ToggleReady;
            _start.clicked += _client.StartRound;
            _client.Model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _client.Model.Changed -= Refresh;

        /// <summary>Per frame: the auto-start countdown of a public table, redrawn when the second changes.</summary>
        public void Tick()
        {
            var startsAt = _client.Model.AutoStartAtTs;
            if (startsAt == null)
            {
                if (_shownSeconds != -1)
                {
                    _shownSeconds = -1;
                    _countdown.text = "";
                }

                return;
            }

            var seconds = Math.Max(0, (startsAt.Value - _nowMs() + 999) / 1000);
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _countdown.text = string.Format(UiText.StartsIn, seconds);
            }
        }

        private bool MeReady()
        {
            var model = _client.Model;
            return model.MySeat != null && model.MySeat < model.Seats.Count && model.Seats[(int)model.MySeat].Ready;
        }

        private void ToggleReady() => _client.SetReady(!MeReady());

        private void Refresh()
        {
            var model = _client.Model;
            _code.text = model.Code.Length > 0 ? model.Code : _client.Ticket.Code;
            _seats.Clear();
            var seated = 0;
            foreach (var seat in model.Seats)
            {
                if (!seat.Occupied)
                {
                    continue;
                }

                seated++;
                var row = new VisualElement();
                row.AddToClassList("lobby-seat");
                var name = new Label(seat.Seat == model.MySeat ? UiText.You : seat.DisplayName ?? "");
                name.AddToClassList("lobby-seat__name");
                row.Add(name);
                if (seat.Seat == model.HostSeat)
                {
                    var host = new Label(UiText.Host);
                    host.AddToClassList("tag");
                    row.Add(host);
                }

                var ready = new Label(seat.Ready ? UiText.Ready : UiText.NotReady);
                ready.AddToClassList(seat.Ready ? "tag--ok" : "tag--muted");
                row.Add(ready);
                _seats.Add(row);
            }

            var matchmade = !model.IsPrivate;
            if (matchmade)
            {
                _code.text = UiText.QuickPlayTable;
            }

            _ready.text = MeReady() ? UiText.NotReady : UiText.Ready;
            _ready.style.display = matchmade ? DisplayStyle.None : DisplayStyle.Flex;
            _start.style.display = model.IsHost && !matchmade ? DisplayStyle.Flex : DisplayStyle.None;
            _status.text = model.LastErrorCode != null ? UiText.Error(model.LastErrorCode) : seated < 3 ? UiText.Waiting : "";
        }
    }
}
