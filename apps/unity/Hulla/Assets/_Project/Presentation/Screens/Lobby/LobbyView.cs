using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Social;
using Hulla.Application.Table;
using UnityEngine;
using Hulla.Presentation.Text;
using Hulla.Presentation.Theme;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Lobby
{
    /// <summary>
    /// The private room before a round (docs/design/basant-lobby.md): Leave and the rulebook, the room
    /// code with Share and copy, the seats on the rug, the rules line, the chat strip with the mic and
    /// chat, and one yellow button: READY, or START for a ready host. Drawn on a 1080 x 1936 stage.
    /// </summary>
    public sealed class LobbyView : IDisposable
    {
        public const float StageHeight = 1936f;

        /// <summary>The seat count the rug shows while the server has not sent the table yet.</summary>
        public const int DefaultSeats = 6;

        /// <summary>The seat's box on the stage; the art is 200 wide inside it.</summary>
        public const float SeatSize = 200f;

        private readonly TableClient _client;
        private readonly Label _code;
        private readonly Label _codeLabel;
        private readonly VisualElement _seats;
        private readonly List<LobbySeat> _slots = new List<LobbySeat>();
        private readonly Button _ready;
        private readonly Label _readyLabel;
        private readonly Button _start;
        private readonly Label _startLabel;
        private readonly Label _status;
        private readonly Label _countdown;
        private readonly Func<long> _nowMs;
        private long _shownSeconds = -1;
        private readonly Button _shareButton;
        private readonly Func<string, Awaitable<Domain.Result<bool>>>? _sendText;
        private readonly VisualElement _chatSheet;
        private readonly VisualElement _chatLog;
        private readonly Label _chatLast;
        private readonly TextField _chatInput;
        private readonly Label _chatStatus;
        private readonly bool _canShare;
        private readonly Label _rules;
        private readonly Label _rulesChanged;
        private readonly Button _editRules;
        private readonly Func<string>? _rulesSummary;
        private readonly Action? _openEditor;
        private readonly VisualElement _root;
        private readonly Button _copyButton;
        private readonly Button _mic;
        private readonly VisualElement _micIcon;
        private readonly VisualElement _watchers;
        private readonly HashSet<string> _seen = new HashSet<string>();
        private bool _firstRefresh = true;
        private Func<Awaitable<IReadOnlyList<FriendEntry>>>? _friends;
        private Func<string, Awaitable<string?>>? _invite;

        /// <param name="share">Shares the room code; returns false when it was copied instead of shared.</param>
        /// <param name="toggleMic">Voice in the lobby; null hides the mic.</param>
        public LobbyView(VisualElement root, TableClient client, Func<long> nowMs, Action leave, Func<string, bool>? share = null, Func<string, Awaitable<Domain.Result<bool>>>? sendText = null, Func<string>? rulesSummary = null, Action? editRules = null, Action? openRulebook = null, Action? toggleMic = null)
        {
            _client = client;
            _sendText = sendText;
            _rulesSummary = rulesSummary;
            _openEditor = editRules;
            _root = root;
            Stage.Fit(root.Q<VisualElement>("stage-holder"), root.Q<VisualElement>("stage"), Stage.Width, StageHeight);
            root.Q<Label>("title").text = UiText.PrivateTable;
            var rulebook = root.Q<Button>("rulebook");
            rulebook.text = "";
            rulebook.tooltip = Strings.Text("rulebook.title");
            // One book top right (design: Lobby): the host's opens the rules editor, a guest's the rulebook.
            rulebook.clicked += () =>
            {
                if (_client.Model.IsHost && _client.Model.IsPrivate && _openEditor != null)
                {
                    _openEditor();
                    return;
                }

                openRulebook?.Invoke();
            };
            rulebook.style.display = openRulebook == null && editRules == null ? DisplayStyle.None : DisplayStyle.Flex;
            _rules = root.Q<Label>("rules");
            _rulesChanged = root.Q<Label>("rules-changed");
            _editRules = root.Q<Button>("edit-rules");
            _editRules.text = "";
            _editRules.tooltip = UiText.EditRules;
            _editRules.clicked += () => _openEditor?.Invoke();

            _chatSheet = root.Q<VisualElement>("chat-sheet");
            _chatSheet.style.display = DisplayStyle.None;
            _chatLog = root.Q<VisualElement>("chat-log");
            _chatLast = root.Q<Label>("chat-last");
            _chatInput = root.Q<TextField>("chat-input");
            _chatInput.maxLength = ITableChat.MaxLength;
            _chatStatus = root.Q<Label>("chat-status");
            var send = root.Q<Button>("chat-send");
            send.text = UiText.Send;
            send.clicked += SendTyped;
            var chatOpen = root.Q<Button>("chat-open");
            chatOpen.text = "";
            chatOpen.tooltip = UiText.Chat;
            chatOpen.clicked += OpenChat;
            _chatLast.pickingMode = PickingMode.Position;
            _chatLast.RegisterCallback<ClickEvent>(_ => OpenChat());
            _chatLast.text = UiText.ChatPlaceholder;
            _chatLast.AddToClassList("lobby__strip-text--empty");
            root.Q<VisualElement>("chat").style.display = sendText == null ? DisplayStyle.None : DisplayStyle.Flex;
            _mic = root.Q<Button>("mic");
            _mic.text = "";
            _micIcon = root.Q<VisualElement>("mic-icon");
            _toggleMic = toggleMic;
            _mic.clicked += () => _toggleMic?.Invoke();
            // The mic always sits in the chat bar (design: Lobby); without voice it is shown off and inert.
            _mic.SetEnabled(toggleMic != null);
            SetMic(false);

            _shareButton = root.Q<Button>("share");
            _shareButton.text = "";
            root.Q<Label>("share-label").text = UiText.Share;
            _shareButton.clicked += () =>
            {
                var code = _client.Model.Code.Length > 0 ? _client.Model.Code : _client.Ticket.Code;
                if (share != null && !share(code))
                {
                    ShowCopied();
                }
            };
            _canShare = share != null;
            _copyButton = root.Q<Button>("copy");
            _copyButton.text = "";
            _copyButton.tooltip = UiText.Copy;
            _copyButton.clicked += () =>
            {
                GUIUtility.systemCopyBuffer = _client.Model.Code.Length > 0 ? _client.Model.Code : _client.Ticket.Code;
                ShowCopied();
            };
            _nowMs = nowMs;
            _countdown = root.Q<Label>("countdown");
            _countdown.style.display = DisplayStyle.None;
            _codeLabel = root.Q<Label>("code-label");
            _codeLabel.text = UiText.RoomCode;
            _code = root.Q<Label>("code");
            _seats = root.Q<VisualElement>("seats");
            _watchers = root.Q<VisualElement>("watchers");
            _ready = root.Q<Button>("ready");
            _ready.text = "";
            _readyLabel = root.Q<Label>("ready-label");
            _start = root.Q<Button>("start");
            _start.text = "";
            _startLabel = root.Q<Label>("start-label");
            _status = root.Q<Label>("status");
            var leaveButton = root.Q<Button>("leave");
            leaveButton.text = "";
            root.Q<Label>("leave-label").text = UiText.Leave;
            leaveButton.clicked += leave;
            _ready.clicked += ToggleReady;
            _start.clicked += () =>
            {
                // The server deals once every seat is ready, the host's included: Start readies the host too.
                if (!MeReady())
                {
                    _client.SetReady(true);
                }

                _client.StartRound();
            };
            _client.Model.Changed += Refresh;
            Refresh();
        }

        public void Dispose()
        {
            _client.Model.Changed -= Refresh;
            _client.Model.Chat -= OnChatLine;
        }

        private IChatCatalog? _catalog;

        /// <summary>Phrases and emotes for the chat sheet's tabs; their lines join the log (design: Chat).</summary>
        public void UseChat(IChatCatalog catalog)
        {
            _catalog = catalog;
            _client.Model.Chat += OnChatLine;
        }

        private void OnChatLine(ChatLine line)
        {
            var model = _client.Model;
            if (line.Seat >= model.Seats.Count)
            {
                return;
            }

            var seat = model.Seats[(int)line.Seat];
            if (IsMuted?.Invoke(seat.PlayerId) == true)
            {
                return;
            }

            var emote = line.PhraseId == null && !string.IsNullOrEmpty(line.EmoteId) ? line.EmoteId : null;
            var text = line.PhraseId != null ? _catalog?.TextFor(line.PhraseId) ?? line.PhraseId : UiText.EmoteLabel(line.EmoteId ?? "");
            AddLine(seat.PlayerId, seat.Seat == model.MySeat ? UiText.You : seat.DisplayName ?? "", text, seat.Seat == model.MySeat, emote);
        }

        /// <summary>The host seats a watcher (seat_spectator); null hides the Seat buttons.</summary>
        public Action<string>? SeatSpectator { get; set; }

        /// <summary>Which emote packs the player owns (GDD 11.3); null allows only the free pack.</summary>
        public Func<string, bool>? OwnsEmotePack { get; set; }

        /// <summary>Muted players' lines are not shown (GDD 11.6).</summary>
        public Func<string?, bool>? IsMuted { get; set; }

        private Action? _toggleMic;

        /// <summary>Voice for the table: the bar's mic toggles it.</summary>
        public void UseMic(Action toggle, bool on)
        {
            _toggleMic = toggle;
            _mic.SetEnabled(true);
            SetMic(on);
        }

        /// <summary>The mic icon follows the voice state.</summary>
        public void SetMic(bool on)
        {
            _micIcon.EnableInClassList("lobby__icon--mic", on);
            _micIcon.EnableInClassList("lobby__icon--mic-off", !on);
        }

        /// <summary>The copy button shows a tick for two seconds.</summary>
        private void ShowCopied()
        {
            _copyButton.EnableInClassList("lobby__copy--done", true);
            _copyButton.schedule.Execute(() => _copyButton.EnableInClassList("lobby__copy--done", false)).StartingIn(2000);
        }

        /// <summary>A free-text line on the table channel: the latest on the strip, the last five in the sheet.</summary>
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

            AddLine(line.SenderId, name, line.Text, line.SenderId == _client.Model.UserId);
        }

        private void AddLine(string? senderId, string name, string text, bool mine, string? emote = null)
        {
            _chatLast.text = name + ": " + text;
            _chatLast.RemoveFromClassList("lobby__strip-text--empty");
            _lines.Add((senderId, name, text, mine, emote));
            while (_lines.Count > 30)
            {
                _lines.RemoveAt(0);
            }

            if (_sheetLog != null)
            {
                _sheetLog.Q(className: "chat-sheet__empty")?.RemoveFromHierarchy();
                _sheetLog.Add(ChatBubble(name, text, mine, AvatarOf(senderId), emote));
                _sheetLog.schedule.Execute(() => _sheetLog.scrollOffset = new Vector2(0, float.MaxValue)).StartingIn(30);
            }
        }

        /// <summary>The face a sender shows at this table, if they are seated.</summary>
        private string? AvatarOf(string? playerId)
        {
            foreach (var seat in _client.Model.Seats)
            {
                if (playerId != null && seat.PlayerId == playerId)
                {
                    return seat.Avatar;
                }
            }

            return null;
        }

        private readonly List<(string? Id, string Name, string Text, bool Mine, string? Emote)> _lines = new List<(string?, string, string, bool, string?)>();
        private ScrollView? _sheetLog;
        private TextField? _sheetInput;
        private Label? _sheetStatus;

        /// <summary>
        /// The chat sheet (design: Chat, lobby form): the lines as bubbles, mine on the right, the field
        /// and the send arrow; Return sends, a tap on the dim closes it.
        /// </summary>
        private void OpenChat()
        {
            if (_sendText == null)
            {
                return;
            }

            var panel = Navigation.Sheet.Open(_root, null, () =>
            {
                _sheetLog = null;
                _sheetInput = null;
                _sheetStatus = null;
            });
            panel.AddToClassList("chat-sheet");
            // Chat, Phrases and Emotes, as at the table (design: Chat).
            var tabs = new VisualElement();
            tabs.AddToClassList("segment");
            tabs.AddToClassList("chat-sheet__tabs");
            panel.Add(tabs);
            var picks = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            picks.AddToClassList("chat-sheet__picks");
            var chipsBox = new VisualElement();
            chipsBox.AddToClassList("chips");
            picks.Add(chipsBox);
            _sheetLog = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _sheetLog.AddToClassList("chat-sheet__log");
            if (_lines.Count == 0)
            {
                _sheetLog.Add(Navigation.Sheet.Line(UiText.ChatPlaceholder, "chat-sheet__empty"));
            }

            foreach (var (id, name, text, mine, emote) in _lines)
            {
                _sheetLog.Add(ChatBubble(name, text, mine, AvatarOf(id), emote));
            }

            panel.Add(_sheetLog);
            panel.Add(picks);
            var compose = new VisualElement();
            compose.AddToClassList("chat-sheet__compose");
            _sheetInput = new TextField { maxLength = ITableChat.MaxLength };
            _sheetInput.AddToClassList("chat-sheet__input");
            _sheetInput.textEdition.placeholder = UiText.ChatPlaceholder;
            _sheetInput.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.character == '\n')
                {
                    SendFromSheet();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            compose.Add(_sheetInput);
            var send = new Button(SendFromSheet) { text = "", tooltip = UiText.Send };
            send.AddToClassList("chat-sheet__send");
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("chat-sheet__send-icon");
            send.Add(arrow);
            compose.Add(send);
            panel.Add(compose);
            _sheetStatus = new Label();
            _sheetStatus.AddToClassList("chat-sheet__status");
            panel.Add(_sheetStatus);
            var log = _sheetLog;
            var status = _sheetStatus;
            void Show(string tab)
            {
                foreach (var item in tabs.Children())
                {
                    item.EnableInClassList("segment__item--on", item.name == "lobby-chat-" + tab);
                }

                var chat = tab == "chat";
                log.style.display = chat ? DisplayStyle.Flex : DisplayStyle.None;
                compose.style.display = chat ? DisplayStyle.Flex : DisplayStyle.None;
                status.style.display = chat ? DisplayStyle.Flex : DisplayStyle.None;
                picks.style.display = chat ? DisplayStyle.None : DisplayStyle.Flex;
                chipsBox.Clear();
                if (tab == "phrases" && _catalog != null)
                {
                    foreach (var phrase in _catalog.Phrases)
                    {
                        var id = phrase.Id;
                        var chip = new Button(() => { _client.SendQuickChat(id); Show("chat"); }) { text = phrase.Text, tooltip = phrase.Translation };
                        chip.AddToClassList("chip");
                        chipsBox.Add(chip);
                    }
                }
                else if (tab == "emotes" && _catalog != null)
                {
                    foreach (var emote in _catalog.Emotes)
                    {
                        var id = emote;
                        var pack = _catalog.EmotePack(id) ?? EmoteArt.FreePack;
                        var owned = OwnsEmotePack?.Invoke(pack) ?? pack == EmoteArt.FreePack;
                        chipsBox.Add(EmoteArt.Tile(id, UiText.EmoteLabel(id), !owned, () =>
                        {
                            if (owned)
                            {
                                _client.SendEmote(id);
                                Show("chat");
                            }
                            else
                            {
                                EmoteArt.ShowLockNote(chipsBox, Strings.Format(UiText.EmotePackLocked, ItemText.Name(pack)));
                            }
                        }));
                    }
                }
            }

            foreach (var (tabName, label) in new[] { ("chat", UiText.Chat), ("phrases", UiText.Phrases), ("emotes", UiText.Emotes) })
            {
                var tab = tabName;
                var item = new Button(() => Show(tab)) { name = "lobby-chat-" + tab, text = label };
                item.AddToClassList("segment__item");
                item.style.display = tab == "chat" || _catalog != null ? DisplayStyle.Flex : DisplayStyle.None;
                tabs.Add(item);
            }

            Show("chat");
            _sheetLog.schedule.Execute(() => _sheetLog!.scrollOffset = new Vector2(0, float.MaxValue)).StartingIn(30);
            _sheetInput.schedule.Execute(() => _sheetInput?.Focus()).StartingIn(50);
        }

        /// <summary>One chat line as a bubble; an emote line shows its sticker instead of words (GDD 11.3).</summary>
        public static VisualElement ChatBubble(string name, string text, bool mine, string? avatar = null, string? emote = null)
        {
            var row = new VisualElement();
            row.AddToClassList("chat-bubble__row");
            row.EnableInClassList("chat-bubble__row--mine", mine);
            if (!mine)
            {
                var who = new Label(name);
                who.AddToClassList("chat-bubble__name");
                row.Add(who);
            }

            var bubble = new Label(emote != null ? "" : text) { tooltip = text };
            bubble.AddToClassList("chat-bubble");
            bubble.EnableInClassList("chat-bubble--mine", mine);
            if (emote != null)
            {
                EmoteArt.Apply(bubble, emote);
                bubble.AddToClassList("chat-bubble--emote");
            }
            if (mine)
            {
                row.Add(bubble);
                return row;
            }

            // Someone else's line: their face beside the bubble (design: Chat).
            var line = new VisualElement();
            line.AddToClassList("chat-bubble__line");
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("seat__avatar");
            face.AddToClassList("chat-bubble__face");
            var initial = new Label { pickingMode = PickingMode.Ignore };
            initial.AddToClassList("seat__initial");
            face.Add(initial);
            Hulla.Presentation.TableKit.AvatarArt.Apply(face, initial, avatar, name);
            line.Add(face);
            line.Add(bubble);
            row.Add(line);
            return row;
        }

        private async void SendFromSheet()
        {
            var input = _sheetInput;
            if (_sendText == null || input == null || string.IsNullOrWhiteSpace(input.value))
            {
                return;
            }

            var text = input.value;
            input.value = "";
            var sent = await _sendText(text);
            if (!sent.IsOk && _sheetStatus != null)
            {
                _sheetStatus.text = UiText.ChatProblem(sent.Error.Code);
                input.value = text;
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

                _countdown.style.display = DisplayStyle.None;
                return;
            }

            _countdown.style.display = DisplayStyle.Flex;
            var seconds = Math.Max(0, (startsAt.Value - _nowMs() + 999) / 1000);
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _countdown.text = Strings.Format(UiText.StartsIn, seconds);
            }
        }

        private bool MeReady()
        {
            var model = _client.Model;
            return model.MySeat != null && model.MySeat < model.Seats.Count && model.Seats[(int)model.MySeat].Ready;
        }

        private void ToggleReady() => _client.SetReady(!MeReady());

        /// <summary>An empty seat invites; a taken seat does nothing here (the player sheet is the table's).</summary>
        private void OnSeatTapped(long seat)
        {
            var model = _client.Model;
            if (seat < model.Seats.Count && model.Seats[(int)seat].Occupied)
            {
                return;
            }

            if (_friends != null && _invite != null)
            {
                OpenInvites();
            }
            else if (_canShare)
            {
                using var click = NavigationSubmitEvent.GetPooled();
                click.target = _shareButton;
                _shareButton.SendEvent(click);
            }
        }

        /// <summary>The seats around the rug, the player's own at the bottom and the rest clockwise from it.</summary>
        private void RefreshSeats(TableModel model, out int seated)
        {
            seated = 0;
            var count = model.Seats.Count > 0 ? model.Seats.Count : DefaultSeats;
            while (_slots.Count < count)
            {
                var slot = new LobbySeat();
                var index = _slots.Count;
                slot.clicked += () => OnSeatTapped(SeatAt(index, _client.Model));
                _slots.Add(slot);
                _seats.Add(slot);
            }

            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                slot.style.display = i < count ? DisplayStyle.Flex : DisplayStyle.None;
                if (i >= count)
                {
                    continue;
                }

                var (x, y) = LobbySeat.Centre(i, count);
                slot.style.left = x - SeatSize / 2f;
                slot.style.top = y - SeatSize / 2f;
                var seat = SeatAt(i, model);
                var info = seat < model.Seats.Count ? model.Seats[(int)seat] : default;
                if (!info.Occupied)
                {
                    slot.ShowEmpty();
                    continue;
                }

                seated++;
                slot.Show(info.DisplayName ?? "", info.Seat == model.HostSeat, info.Ready, info.Avatar, seat == model.MySeat, info.Bot);
                if (info.PlayerId != null && _seen.Add(info.PlayerId) && !_firstRefresh)
                {
                    slot.AddToClassList("seat--enter");
                    slot.schedule.Execute(() => slot.RemoveFromClassList("seat--enter")).StartingIn(16);
                }
            }

            _firstRefresh = false;
        }

        /// <summary>Slot 0 is the player's own seat (or seat 0 while watching); the rest follow in table order.</summary>
        private static long SeatAt(int slot, TableModel model)
        {
            var count = model.Seats.Count > 0 ? model.Seats.Count : DefaultSeats;
            return (slot + (model.MySeat ?? 0)) % count;
        }

        private void Refresh()
        {
            var model = _client.Model;
            RefreshSeats(model, out var seated);
            // The seats sit on the player's own equipped table (GDD 12.11).
            var mine = model.MySeat is long own && own < model.Seats.Count ? model.Seats[(int)own].TableSurface : null;
            var rug = _root.Q<VisualElement>("rug");
            if (rug != null)
            {
                CosmeticArt.Apply(rug, mine);
            }

            var matchmade = !model.IsPrivate && model.Seats.Count > 0;
            _code.text = matchmade ? UiText.QuickPlayTable : model.Code.Length > 0 ? model.Code : _client.Ticket.Code;
            _codeLabel.style.display = matchmade ? DisplayStyle.None : DisplayStyle.Flex;
            // A Quick Play table has no code to share: its name goes in the title and the code card steps aside.
            _root.Q<Label>("title").text = matchmade ? UiText.QuickPlayTable : UiText.PrivateTable;
            var card = _root.Q<VisualElement>("code-card");
            if (card != null)
            {
                card.style.visibility = matchmade ? Visibility.Hidden : Visibility.Visible;
            }
            var unready = 0;
            foreach (var seat in model.Seats)
            {
                if (seat.Occupied && !seat.Ready && seat.Seat != model.MySeat)
                {
                    unready++;
                }
            }

            var canStart = seated >= 3 && unready == 0;
            _start.SetEnabled(canStart);
            _startLabel.text = Upper(seated < 3 ? Strings.Format(UiText.SeatedOfThree, seated) : unready > 0 ? Strings.Format(UiText.WaitingReady, unready) : UiText.Start);
            _startLabel.EnableInClassList("lobby__play-label--small", !canStart);
            _rules.text = _rulesSummary?.Invoke() ?? "";
            _watchers.Clear();
            foreach (var watcher in model.Spectators)
            {
                var row = new VisualElement();
                row.AddToClassList("lobby__watcher");
                var label = new Label(watcher.Name + " · " + UiText.Watchers + (watcher.SeatNext ? " (" + UiText.SeatsNext + ")" : ""));
                label.AddToClassList("lobby__watcher-name");
                row.Add(label);
                if (model.IsHost && SeatSpectator != null && !watcher.SeatNext)
                {
                    var id = watcher.UserId;
                    var seat = new Button(() => SeatSpectator?.Invoke(id)) { text = UiText.SeatThem };
                    seat.AddToClassList("chip");
                    seat.AddToClassList("lobby__seat-them");
                    row.Add(seat);
                }

                _watchers.Add(row);
            }

            _editRules.style.display = DisplayStyle.None;
            var changedBy = model.RulesChangedBy;
            _rulesChanged.text = changedBy == null || changedBy == model.MySeat
                ? ""
                : Strings.Format(UiText.RulesChangedBy, changedBy < model.Seats.Count ? model.Seats[(int)changedBy.Value].DisplayName ?? "?" : "?");

            // One yellow button: READY for a player, START for a host who is ready (three or more seated).
            var ready = MeReady();
            var hostStarts = model.IsHost && !matchmade && !model.Spectating;
            _readyLabel.text = Upper(ready ? UiText.NotReady : UiText.Ready);
            // The host gets ready like everyone (design: Lobby), then the same button starts the round.
            _ready.style.display = matchmade || model.Spectating || (hostStarts && ready) ? DisplayStyle.None : DisplayStyle.Flex;
            _start.style.display = hostStarts && ready ? DisplayStyle.Flex : DisplayStyle.None;
            _shareButton.style.display = matchmade || !_canShare ? DisplayStyle.None : DisplayStyle.Flex;
            _copyButton.style.display = matchmade ? DisplayStyle.None : DisplayStyle.Flex;

            var host = model.HostSeat is long h && h < model.Seats.Count ? model.Seats[(int)h].DisplayName ?? "" : "";
            _status.text = model.LastErrorCode != null ? UiText.Error(model.LastErrorCode)
                : model.Spectating ? UiText.WatchingLobby
                : matchmade ? (seated < 3 ? UiText.Waiting : "")
                : model.IsHost ? (ready ? UiText.LobbyHintHost : UiText.LobbyHintHostReady)
                : Strings.Format(UiText.LobbyHint, host);
        }

        private static string Upper(string text) => text.ToUpper(Strings.Culture);

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

        /// <summary>
        /// The invite sheet (design: Invite sheet): friends online first, one Invite per row that turns to
        /// "Sent ✓"; Share link is the yellow button with the code beside it to copy.
        /// </summary>
        private async void OpenInvites()
        {
            if (_friends == null || _invite == null)
            {
                return;
            }

            var panel = Navigation.Sheet.Open(_root, UiText.InviteGroup);
            panel.AddToClassList("invite-sheet");
            var list = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            list.AddToClassList("invite-sheet__list");
            panel.Add(list);
            var bottom = new VisualElement();
            bottom.AddToClassList("sheet__answers");
            var code = _client.Model.Code.Length > 0 ? _client.Model.Code : _client.Ticket.Code;
            if (_canShare)
            {
                var share = Navigation.Sheet.Button(UiText.ShareLink, () =>
                {
                    using var click = NavigationSubmitEvent.GetPooled();
                    click.target = _shareButton;
                    _shareButton.SendEvent(click);
                }, "yes");
                // Orange with the share arrow before the words (concept: Invite your group).
                share.AddToClassList("invite-sheet__share");
                var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
                arrow.AddToClassList("invite-sheet__icon");
                arrow.AddToClassList("invite-sheet__icon--share");
                share.text = "";
                share.Add(arrow);
                share.Add(new Label(UiText.ShareLink) { pickingMode = PickingMode.Ignore });
                bottom.Add(share);
            }

            var copy = Navigation.Sheet.Button(code, () =>
            {
                GUIUtility.systemCopyBuffer = code;
                ShowCopied();
            });
            copy.AddToClassList("invite-sheet__code");
            var copyIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            copyIcon.AddToClassList("invite-sheet__icon");
            copyIcon.AddToClassList("invite-sheet__icon--copy");
            copy.text = "";
            copy.tooltip = UiText.Copy;
            copy.Add(copyIcon);
            copy.Add(new Label(code) { pickingMode = PickingMode.Ignore });
            bottom.Add(copy);
            panel.Add(bottom);

            var friends = new List<FriendEntry>(await _friends());
            friends.Sort((a, b) => a.Online == b.Online ? string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase) : a.Online ? -1 : 1);
            if (friends.Count == 0)
            {
                list.Add(Navigation.Sheet.Line(UiText.NoFriendsToInvite));
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
                row.AddToClassList("invite-row");
                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                face.AddToClassList("friend-row__face");
                var initial = new Label();
                initial.AddToClassList("seat__initial");
                face.Add(initial);
                TableKit.AvatarArt.Apply(face, initial, TableKit.AvatarArt.ForAccount(friend.UserId), friend.Name);
                row.Add(face);
                var text = new VisualElement();
                text.AddToClassList("invite-row__text");
                var name = new Label(friend.Name);
                name.AddToClassList("invite-row__name");
                text.Add(name);
                var state = new Label(friend.Online ? UiText.Online : UiText.Offline);
                state.AddToClassList("invite-row__state");
                state.EnableInClassList("invite-row__state--online", friend.Online);
                text.Add(state);
                row.Add(text);
                var id = friend.UserId;
                var at = seated.Contains(id);
                var button = Navigation.Sheet.Button(at ? UiText.AtTable : UiText.Invite, () => { }, at ? "" : "teal");
                button.AddToClassList("invite-row__button");
                button.SetEnabled(!at);
                button.clicked += async () =>
                {
                    button.SetEnabled(false);
                    var problem = await _invite(id);
                    button.text = problem == null ? UiText.SentTick : UiText.InviteProblem(problem);
                    button.RemoveFromClassList("sheet__answer--teal");
                };
                row.Add(button);
                list.Add(row);
            }
        }
    }
}
