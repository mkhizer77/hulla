using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Social;
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
        private readonly VisualElement _root;
        private readonly Button _inviteButton;
        private Func<Awaitable<IReadOnlyList<FriendEntry>>>? _friends;
        private Func<string, Awaitable<string?>>? _invite;

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
            _root = root;
            _inviteButton = root.Q<Button>("invite");
            _inviteButton.text = UiText.InviteFriends;
            _inviteButton.clicked += OpenInvites;
            _inviteButton.style.display = DisplayStyle.None;
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
        /// <summary>The host seats a watcher (seat_spectator); null hides the Seat buttons.</summary>
        public Action<string>? SeatSpectator { get; set; }

        /// <summary>Muted players' lines are not shown (GDD 11.6).</summary>
        public Func<string?, bool>? IsMuted { get; set; }

        public void ShowText(TextLine line)
        {
            if (IsMuted?.Invoke(line.SenderId) == true)
            {
                return;
            }

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

            _chatStatus.text = UiText.ChatProblem(sent.Error.Code);
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
            foreach (var watcher in model.Spectators)
            {
                var row = new VisualElement();
                row.AddToClassList("lobby-seat");
                var label = new Label(watcher.Name + " · " + UiText.Watchers + (watcher.SeatNext ? " (" + UiText.SeatsNext + ")" : ""));
                label.AddToClassList("lobby-seat__name");
                row.Add(label);
                if (model.IsHost && SeatSpectator != null && !watcher.SeatNext)
                {
                    var id = watcher.UserId;
                    var seat = new Button(() => SeatSpectator?.Invoke(id)) { text = UiText.SeatThem };
                    seat.AddToClassList("chip");
                    row.Add(seat);
                }

                _seats.Add(row);
            }
            _editRules.style.display = model.IsHost && !matchmade && _openEditor != null ? DisplayStyle.Flex : DisplayStyle.None;
            var changedBy = model.RulesChangedBy;
            _rulesChanged.text = changedBy == null || changedBy == model.MySeat
                ? ""
                : string.Format(UiText.RulesChangedBy, changedBy < model.Seats.Count ? model.Seats[(int)changedBy.Value].DisplayName ?? "?" : "?");
            _ready.text = MeReady() ? UiText.NotReady : UiText.Ready;
            _ready.style.display = matchmade || model.Spectating ? DisplayStyle.None : DisplayStyle.Flex;
            _shareButton.style.display = matchmade || !_canShare ? DisplayStyle.None : DisplayStyle.Flex;
            _inviteButton.style.display = matchmade || _invite == null || model.Spectating ? DisplayStyle.None : DisplayStyle.Flex;
            _start.style.display = model.IsHost && !matchmade ? DisplayStyle.Flex : DisplayStyle.None;
            _status.text = model.LastErrorCode != null ? UiText.Error(model.LastErrorCode) : model.Spectating ? UiText.WatchingLobby : seated < 3 ? UiText.Waiting : "";
        }
    
        /// <summary>
        /// Shows "Invite friends" (GDD 10.5): the friend list, online first, each with Invite.
        /// <paramref name="invite"/> answers null when sent, or why not (a skip reason or an error code).
        /// </summary>
        public void UseInvites(Func<Awaitable<IReadOnlyList<FriendEntry>>> friends, Func<string, Awaitable<string?>> invite)
        {
            _friends = friends;
            _invite = invite;
            Refresh();
        }

        private async void OpenInvites()
        {
            if (_friends == null || _invite == null)
            {
                return;
            }

            _root.Q("invite-sheet")?.RemoveFromHierarchy();
            var sheet = new VisualElement { name = "invite-sheet" };
            sheet.AddToClassList("overlay");
            sheet.AddToClassList("results");
            sheet.style.display = DisplayStyle.Flex;
            var panel = new VisualElement();
            panel.AddToClassList("panel");
            panel.AddToClassList("invite-sheet");
            var title = new Label(UiText.InviteFriends);
            title.AddToClassList("title");
            title.AddToClassList("title--small");
            panel.Add(title);
            var list = new ScrollView();
            list.AddToClassList("invite-sheet__list");
            panel.Add(list);
            var close = new Button(() => sheet.RemoveFromHierarchy()) { text = UiText.Done };
            close.AddToClassList("button");
            panel.Add(close);
            sheet.Add(panel);
            _root.Add(sheet);

            var friends = new List<FriendEntry>(await _friends());
            friends.Sort((a, b) => a.Online == b.Online ? string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase) : a.Online ? -1 : 1);
            if (friends.Count == 0)
            {
                var none = new Label(UiText.NoFriendsToInvite);
                none.AddToClassList("status");
                none.style.whiteSpace = WhiteSpace.Normal;
                list.Add(none);
                return;
            }

            var seated = new HashSet<string>();
            foreach (var seat in _client.Model.Seats)
            {
                if (seat.PlayerId != null)
                {
                    seated.Add(seat.PlayerId);
                }
            }

            foreach (var friend in friends)
            {
                var row = new VisualElement();
                row.AddToClassList("lobby-seat");
                row.AddToClassList("friend-row");
                var name = new Label(friend.Name + (friend.Online ? "" : " · " + UiText.Offline));
                name.AddToClassList("lobby-seat__name");
                row.Add(name);
                var id = friend.UserId;
                var button = new Button { text = seated.Contains(id) ? UiText.AtTable : UiText.Invite };
                button.AddToClassList("chip");
                button.SetEnabled(!seated.Contains(id));
                button.clicked += async () =>
                {
                    button.SetEnabled(false);
                    var problem = await _invite(id);
                    button.text = problem == null ? UiText.Invited : UiText.InviteProblem(problem);
                };
                row.Add(button);
                list.Add(row);
            }
        }
}
}
