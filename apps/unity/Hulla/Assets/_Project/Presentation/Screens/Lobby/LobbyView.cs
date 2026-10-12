using System;
using Hulla.Application.Services;
using Hulla.Application.Table;
using UnityEngine;
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
        private readonly Button _shareButton;
        private readonly Func<string, Awaitable<Domain.Result<bool>>>? _sendText;
        private readonly VisualElement _chatLog;
        private readonly TextField _chatInput;
        private readonly Label _chatStatus;
        private readonly bool _canShare;
        private readonly Label _rules;
        private readonly Label _rulesChanged;
        private readonly Button _editRules;
        private readonly Func<string>? _rulesSummary;
        private readonly Action? _openEditor;

        /// <param name="share">Shares the room code; returns false when it was copied instead of shared.</param>
        public LobbyView(VisualElement root, TableClient client, Func<long> nowMs, Action leave, Func<string, bool>? share = null, Func<string, Awaitable<Domain.Result<bool>>>? sendText = null, Func<string>? rulesSummary = null, Action? editRules = null)
        {
            _client = client;
            _sendText = sendText;
            _rulesSummary = rulesSummary;
            _openEditor = editRules;
            _rules = root.Q<Label>("rules");
            _rulesChanged = root.Q<Label>("rules-changed");
            _editRules = root.Q<Button>("edit-rules");
            _editRules.text = UiText.EditRules;
            _editRules.clicked += () => _openEditor?.Invoke();
            _chatLog = root.Q<VisualElement>("chat-log");
            _chatInput = root.Q<TextField>("chat-input");
            _chatInput.maxLength = ITableChat.MaxLength;
            _chatStatus = root.Q<Label>("chat-status");
            var send = root.Q<Button>("chat-send");
            send.text = UiText.Send;
            send.clicked += SendTyped;
            root.Q<VisualElement>("chat").style.display = sendText == null ? DisplayStyle.None : DisplayStyle.Flex;
            _shareButton = root.Q<Button>("share");
            _shareButton.text = UiText.ShareCode;
            _shareButton.clicked += () =>
            {
                var code = _client.Model.Code.Length > 0 ? _client.Model.Code : _client.Ticket.Code;
                if (share != null && !share(code))
                {
                    _shareButton.text = UiText.Copied;
                }
            };
            _canShare = share != null;
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

        /// <summary>A free-text line on the table channel, newest first, the latest five only.</summary>
        public void ShowText(TextLine line)
        {
            var name = line.SenderName;
            foreach (var seat in _client.Model.Seats)
            {
                if (seat.PlayerId == line.SenderId)
                {
                    name = seat.Seat == _client.Model.MySeat ? UiText.You : seat.DisplayName ?? name;
                }
            }

            var entry = new Label(name + ": " + line.Text);
            entry.AddToClassList("chat-log__line");
            _chatLog.Insert(0, entry);
            while (_chatLog.childCount > 5)
            {
                _chatLog.RemoveAt(_chatLog.childCount - 1);
            }
        }

        private async void SendTyped()
        {
            var text = _chatInput.value;
            if (_sendText == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _chatStatus.text = "";
            var sent = await _sendText(text);
            if (sent.IsOk)
            {
                _chatInput.value = "";
                return;
            }

            _chatStatus.text = sent.Error.Code == "network.rate_limited" ? UiText.ChatTooFast : UiText.ChatRefused;
        }

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

            _rules.text = _rulesSummary?.Invoke() ?? "";
            _editRules.style.display = model.IsHost && !matchmade && _openEditor != null ? DisplayStyle.Flex : DisplayStyle.None;
            var changedBy = model.RulesChangedBy;
            _rulesChanged.text = changedBy == null || changedBy == model.MySeat
                ? ""
                : string.Format(UiText.RulesChangedBy, changedBy < model.Seats.Count ? model.Seats[(int)changedBy.Value].DisplayName ?? "?" : "?");
            _ready.text = MeReady() ? UiText.NotReady : UiText.Ready;
            _ready.style.display = matchmade ? DisplayStyle.None : DisplayStyle.Flex;
            _shareButton.style.display = matchmade || !_canShare ? DisplayStyle.None : DisplayStyle.Flex;
            _start.style.display = model.IsHost && !matchmade ? DisplayStyle.Flex : DisplayStyle.None;
            _status.text = model.LastErrorCode != null ? UiText.Error(model.LastErrorCode) : seated < 3 ? UiText.Waiting : "";
        }
    }
}
