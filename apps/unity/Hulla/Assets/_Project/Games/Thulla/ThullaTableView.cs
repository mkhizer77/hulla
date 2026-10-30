using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Application.Voice;
using Hulla.Presentation.Audio;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using Hulla.Presentation.Theme;
using Hulla.Protocol;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The Thulla table (GDD 14.3 to 14.7) on the UI Toolkit table kit: opponents on the seat arc with
    /// the crown, timer rings and void chips, the pile fanning in from each seat and flying to the
    /// picker, the fanned hand with the server's legal cards lit and a one-line reason on an illegal
    /// tap, the turn timer with its amber and red phases, take-hand, the contextual quick-chat strip,
    /// the thulla moment with its sounds and haptics, the first-round rules recap and the results
    /// overlay with the rematch vote. It renders from the model; the scheduler only decides how long
    /// each event holds the stage.
    /// </summary>
    public sealed class ThullaTableView : IEventPresenter, IDisposable
    {
        /// <summary>The thulla moment never delays the next turn by more than this (GDD 14.6).</summary>
        private const float ThullaMomentSeconds = 1.2f;

        /// <summary>When the pile starts flying to the picker, and when the picker's sting plays (GDD 14.6).</summary>
        private const float PickupStartSeconds = 0.45f;
        private const float StingSeconds = 0.9f;

        /// <summary>A trick nobody picked up rests this long before it slides to the discard.</summary>
        private const float DiscardStartSeconds = 0.55f;

        /// <summary>A pile this big gets the louder stamp and the big-pick-up phrases (GDD 14.6).</summary>
        private const int BigPickup = 5;

        /// <summary>A lone legal card plays itself this long after it rises (R44, OQ-036).</summary>
        public const float SingleLegalSeconds = 0.7f;

        /// <summary>The first round opens with a short rules recap that a player can turn off (GDD 9.5).</summary>
        private const float RecapSeconds = 3f;

        /// <summary>How long a phrase, an emote or a play hint stays on screen.</summary>
        private const float SaySeconds = 3f;

        private readonly TableClient _client;
        private readonly Func<long> _nowMs;
        private readonly OpponentRing _ring;
        private readonly PlayerBadge _me = new PlayerBadge();
        private readonly VisualElement _root;
        private readonly Action _leave;
        private readonly Action<string?>? _openRules;
        private readonly Action? _openSettings;
        private readonly Label _watchCount;
        private readonly VisualElement _watchIcon;
        private readonly Label _statusText;
        private readonly Label _statusSecs;
        private readonly VisualElement _status;
        private readonly VisualElement _dim;
        private Action<Fairness>? _explainFairness;
        private readonly Dictionary<string, int> _bhabhiTally = new Dictionary<string, int>();
        private readonly HashSet<string> _talliedRounds = new HashSet<string>();
        private int _roundsPlayed;
        private readonly PileView _pile;
        private readonly DiscardView _discard;
        private readonly Label _pileNote;
        private readonly HandView _hand;
        private readonly Label _hint;
        private readonly Label _info;
        private readonly Button _takeHand;
        private readonly Label _toast;
        private readonly Dictionary<string, Button> _emoteTiles = new Dictionary<string, Button>();
        private Func<string, bool>? _ownsPack;
        private readonly VisualElement _burst;
        private readonly VisualElement _rail;
        private readonly ThullaResults _results;
        private readonly IChatCatalog _chat;
        private readonly VisualElement _chatPanel;
        private readonly VisualElement _strip;
        private readonly VisualElement _handArea;
        private readonly Label _mySay;
        private readonly StampTimeline _stamp = new StampTimeline();
        private readonly VisualElement _felt;
        private readonly SoundPlayer? _sounds;
        private readonly bool _autoPlaySingle;
        private bool _reduceMotion;
        private bool _askSheetOpen;
        private bool _haptics;
        private readonly VisualElement _recap;
        private Action? _hideRecapForGood;
        private readonly List<(string Card, string Owner, long Seat)> _plays = new List<(string Card, string Owner, long Seat)>();
        private readonly List<string> _handCards = new List<string>();
        private readonly HashSet<string> _handBefore = new HashSet<string>();
        private float _mySayLeft;
        private float _hintLeft;
        private bool _recapShown;
        private float _recapLeft;
        private long _shownSeconds = -1;
        private long _turnStartMs;
        private long _turnDeadlineMs;
        private long _turnLengthMs;
        private long? _opponentTurnSeat;
        private long _opponentTurnStartMs;
        private float _pulseElapsed;
        private long _tickedAt = -1;
        private float _flightLeft;
        private PileView.Flight? _flight;
        private PileView.Flight _landing;
        private Func<string, Rect?> _flightTarget = _ => null;
        private Rect _flightFallback;
        private long _discardShown;
        private float _stingLeft;
        private bool _stampSounded;
        private float _singleLegalLeft;
        private string? _singleLegal;
        private int _escapes;
        private string _context = QuickStrip.Any;
        private int _stripSeed;
        private bool _voted;
        private VisualElement _coach = null!;
        private Label _coachText = null!;
        private Button _coachButton = null!;
        private Action? _coachAction;
        private ThullaHints? _hints;
        private Func<string?, bool>? _isMuted;
        private Action<string, bool>? _setMuted;
        private Action<string>? _block;
        private Action<string, string, Action<bool>>? _report;
        private VisualElement _sheet = null!;
        private Label _sheetTitle = null!;
        private VisualElement _sheetActions = null!;
        private Label _sheetStatus = null!;
        private bool _thullaJustPlayed;
        private Rect? _playedFrom;
        private readonly Func<string, Awaitable<Domain.Result<bool>>>? _sendText;
        private VisualElement _chatLog = null!;
        private TextField _chatInput = null!;
        private Label _chatStatus = null!;
        private readonly Button _mic;
        private readonly Button _talk;
        private readonly Label _voicePill;
        private readonly Button _speaker;
        private Func<string, int>? _peerVolume;
        private Action<string, int>? _setPeerVolume;
        private VoiceController? _voice;
        private float _voiceWhyLeft;

        /// <summary>How long the line beside the microphone prompt stays.</summary>
        private const float VoiceWhySeconds = 4f;

        /// <summary>The chat log keeps the latest lines only.</summary>
        private const int ChatLogLines = 30;

        /// <param name="oneTap">Play on the first tap instead of tap-to-raise (a setting, GDD 14.4).</param>
        /// <param name="hideRecapForGood">Remembers "don't show again"; null when the player already chose it.</param>
        /// <param name="sounds">The table's sounds; null for silence (tests).</param>
        /// <param name="autoPlaySingle">A lone legal card plays itself after a beat (R44); off in the tutorial.</param>
        /// <summary>Players who dropped this round, by seat; a bot plays for them until they are back (R44).</summary>
        private readonly Dictionary<long, string> _leftThisRound = new Dictionary<long, string>();

        public ThullaTableView(
            VisualElement root,
            TableClient client,
            IChatCatalog chat,
            Func<long> nowMs,
            Action leave,
            bool reduceMotion = false,
            Action? hideRecapForGood = null,
            bool oneTap = false,
            Action? openSettings = null,
            Action<string?>? openRules = null,
            Func<string, Awaitable<Domain.Result<bool>>>? sendText = null,
            SoundPlayer? sounds = null,
            bool autoPlaySingle = true)
        {
            _sendText = sendText;
            _client = client;
            _chat = chat;
            _nowMs = nowMs;
            _sounds = sounds;
            _autoPlaySingle = autoPlaySingle;
            _reduceMotion = reduceMotion;
            _hideRecapForGood = hideRecapForGood;
            _felt = root.Q<VisualElement>("felt");
            _rail = root.Q<VisualElement>("rail");
            _ring = new OpponentRing(root.Q<VisualElement>("opponents"));
            root.Q<VisualElement>("me")?.Add(_me);
            _ring.Tapped += OnBadgeTapped;
            _pile = new PileView(root.Q<VisualElement>("pile")) { ReduceMotion = reduceMotion };
            _pile.CardArrived += OnPileCardArrived;
            _discard = new DiscardView(root.Q<VisualElement>("discard"), UiText.DiscardPile);
            _pileNote = root.Q<Label>("pile-note");
            _handArea = root.Q<VisualElement>("hand");
            _hand = new HandView(_handArea) { OneTap = oneTap };
            _hand.Played += card =>
            {
                _singleLegal = null;
                _playedFrom = _hand.SlotOf(card);
                _client.PlayCard(card);
            };
            _hand.Refused += ShowHint;
            _hand.Touched += () => _singleLegal = null;
            _hint = root.Q<Label>("hint");
            _hint.RegisterCallback<ClickEvent>(_ => openRules?.Invoke(ThullaRulebook.WhyNotSection));
            _info = root.Q<Label>("info");
            _takeHand = root.Q<Button>("take-hand");
            _takeHand.text = UiText.TakeHand;
            _takeHand.clicked += AskForHand;
            _toast = root.Q<Label>("toast");
            // The thulla giver's celebration (GDD 12.11) bursts behind the stamp, so it sits just before it.
            _burst = new VisualElement { name = "stamp-burst", pickingMode = PickingMode.Ignore };
            _burst.AddToClassList("stamp-burst");
            _burst.style.display = DisplayStyle.None;
            _toast.parent.Insert(_toast.parent.IndexOf(_toast), _burst);
            _results = new ThullaResults(root.Q<VisualElement>("results"), NameOf, AvatarOf, VoteAgain);
            _results.WhoLeft = () => new List<string>(_leftThisRound.Values);
            _results.Leave = leave;
            var settingsButton = root.Q<Button>("settings");
            settingsButton.text = UiText.Settings;
            settingsButton.clicked += () => openSettings?.Invoke();
            settingsButton.style.display = DisplayStyle.None;
            var rulesButton = root.Q<Button>("rules");
            rulesButton.text = RulebookText.Title;
            rulesButton.clicked += () => openRules?.Invoke(null);
            rulesButton.style.display = DisplayStyle.None;
            var leaveButton = root.Q<Button>("leave");
            leaveButton.text = UiText.Leave;
            _root = root;
            _leave = leave;
            _openRules = openRules;
            _openSettings = openSettings;
            leaveButton.clicked += AskLeave;
            var moreButton = root.Q<Button>("more");
            moreButton.text = "";
            moreButton.tooltip = UiText.More;
            moreButton.clicked += OpenMenu;
            var plate = root.Q<VisualElement>("info-plate");
            plate.RegisterCallback<ClickEvent>(_ => OpenFairDeal());
            _watchCount = root.Q<Label>("watch-count");
            _watchIcon = root.Q<VisualElement>("watch-icon");
            foreach (var eye in new VisualElement[] { _watchCount, _watchIcon })
            {
                eye.pickingMode = PickingMode.Position;
                eye.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    OpenWatchers();
                });
            }

            _statusText = root.Q<Label>("status-text");
            _statusSecs = root.Q<Label>("status-secs");
            _status = root.Q<VisualElement>("status");
            _dim = root.Q<VisualElement>("sheet-dim");
            _dim.RegisterCallback<PointerDownEvent>(_ => CloseSheets());
            _recap = root.Q<VisualElement>("recap");
            root.Q<Label>("recap-title").text = UiText.RecapTitle;
            root.Q<Label>("recap-text").text = UiText.Recap;
            var hideRecap = root.Q<Button>("recap-hide");
            hideRecap.text = UiText.RecapHide;
            hideRecap.clicked += () =>
            {
                _hideRecapForGood?.Invoke();
                _recapLeft = 0f;
                _recap.style.display = DisplayStyle.None;
            };
            _mySay = root.Q<Label>("my-say");
            _chatPanel = root.Q<VisualElement>("chat-panel");
            _strip = root.Q<VisualElement>("quick-strip");
            var chatButton = root.Q<Button>("chat");
            chatButton.text = "";
            chatButton.tooltip = UiText.Chat;
            chatButton.clicked += () => SetChatOpen(_chatPanel.style.display != DisplayStyle.Flex);
            BuildChatTabs(root.Q<VisualElement>("chat-tabs"));
            var closeChat = root.Q<Button>("chat-close");
            closeChat.text = UiText.Close;
            closeChat.clicked += () => SetChatOpen(false);
            BuildChatPanel(root.Q<VisualElement>("chat-phrases"), root.Q<VisualElement>("chat-emotes"));
            RefreshStrip();
            _chatLog = root.Q<VisualElement>("chat-log");
            _chatStatus = root.Q<Label>("chat-status");
            _chatInput = root.Q<TextField>("chat-input");
            _chatInput.textEdition.placeholder = UiText.ChatPlaceholder;
            _chatInput.maxLength = ITableChat.MaxLength;
            _chatInput.label = "";
            var send = root.Q<Button>("chat-send");
            send.text = "";
            send.tooltip = UiText.Send;
            send.AddToClassList("chat-sheet__send");
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("chat-sheet__send-icon");
            send.Add(arrow);
            send.clicked += SendTyped;
            _chatInput.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    SendTyped();
                }
            });
            var compose = root.Q<VisualElement>(className: "chat-panel__compose");
            compose.style.display = sendText == null ? DisplayStyle.None : DisplayStyle.Flex;
            _mic = root.Q<Button>("mic");
            _mic.style.display = DisplayStyle.None;
            _talk = root.Q<Button>("talk");
            _talk.style.display = DisplayStyle.None;
            _talk.text = UiText.HoldToTalk;
            _talk.RegisterCallback<PointerDownEvent>(_ => _voice?.SetTalking(true), TrickleDown.TrickleDown);
            _talk.RegisterCallback<PointerUpEvent>(_ => _voice?.SetTalking(false), TrickleDown.TrickleDown);
            _talk.RegisterCallback<PointerLeaveEvent>(_ => _voice?.SetTalking(false));
            _talk.RegisterCallback<PointerCancelEvent>(_ => _voice?.SetTalking(false));
            _voicePill = root.Q<Label>("voice-pill");
            _speaker = root.Q<Button>("voice-speaker");
            _speaker.clicked += () => _voice?.ToggleSpeaker();
            _sheet = root.Q<VisualElement>("player-sheet");
            _sheetTitle = root.Q<Label>("sheet-title");
            _sheetActions = root.Q<VisualElement>("sheet-actions");
            _sheetStatus = root.Q<Label>("sheet-status");
            _coach = root.Q<VisualElement>("coach");
            _coachText = root.Q<Label>("coach-text");
            _coachButton = root.Q<Button>("coach-next");
            _coachButton.clicked += () => _coachAction?.Invoke();
            _client.Model.Changed += Refresh;
            _client.Model.Chat += OnChat;
            _escapes = _client.Model.EscapeOrder.Count;
            foreach (var card in _client.Model.Hand)
            {
                _handBefore.Add(card);
            }

            Refresh();
        }

        /// <summary>Raised when the player taps a card that can't be played (the tutorial counts these).</summary>
        public event Action<string>? CardRefused;

        /// <summary>The quick-chat context the strip is showing (`gave_thulla`, `any`…); read by tests.</summary>
        public string StripContext => _context;

        /// <summary>The tutorial coach's bubble over the table; a button when the step waits for the player.</summary>
        public void ShowCoach(string text, string? button = null, Action? onButton = null)
        {
            _coachText.text = text;
            _coachAction = onButton;
            _coachButton.text = button ?? "";
            _coachButton.style.display = button == null ? DisplayStyle.None : DisplayStyle.Flex;
            _coach.style.display = DisplayStyle.Flex;
        }

        public void HideCoach() => _coach.style.display = DisplayStyle.None;

        /// <summary>Hides muted players' text, quick chat and emotes, and offers Mute on a tapped badge (GDD 11.6).</summary>
        public void UseMutes(Func<string?, bool> isMuted, Action<string, bool> setMuted)
        {
            _isMuted = isMuted;
            _setMuted = setMuted;
        }

        /// <summary>Block and report from the player sheet (GDD 10.11); both run against the server.</summary>
        public void UseModeration(Action<string> block, Action<string, string, Action<bool>> report)
        {
            _block = block;
            _report = report;
        }

        /// <summary>The player sheet for a tapped badge: Mute or Unmute, Block, Report.</summary>
        private void OnBadgeTapped(long seat)
        {
            var model = _client.Model;
            if (_setMuted == null || seat >= model.Seats.Count)
            {
                return;
            }

            var id = model.Seats[(int)seat].PlayerId;
            if (id == null || model.Seats[(int)seat].Bot)
            {
                return;
            }

            ShowPlayer(id, NameOf(seat), model.Seats[(int)seat].Avatar, seat);
        }

        /// <summary>The player sheet for anyone at the table, seated or watching (seat null for a watcher).</summary>
        private void ShowPlayer(string id, string name, string? avatar, long? seat)
        {
            var model = _client.Model;
            if (_setMuted == null)
            {
                return;
            }

            var muted = _isMuted?.Invoke(id) == true;
            _sheetTitle.style.display = DisplayStyle.None;
            _sheetStatus.text = "";
            _sheetActions.Clear();

            // The player sheet (concept: Player sheet at the table): face, name, handle and title, one stats
            // line, cards picked up this round, Add friend, their voice level, then Mute · Block · Report.
            var head = new VisualElement();
            head.AddToClassList("psheet__head");
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("seat__avatar");
            face.AddToClassList("psheet__face");
            var initial = new Label { pickingMode = PickingMode.Ignore };
            initial.AddToClassList("seat__initial");
            face.Add(initial);
            AvatarArt.Apply(face, initial, avatar, name);
            head.Add(face);
            var lines = new VisualElement();
            lines.AddToClassList("psheet__lines");
            lines.Add(Line(name, "psheet__name"));
            var who = Line("", "psheet__line");
            var stats = Line("", "psheet__line");
            lines.Add(who);
            lines.Add(stats);
            head.Add(lines);
            _sheetActions.Add(head);
            if (LookUpProfile != null)
            {
                FillProfile(id, who, stats);
            }

            if (model.InRound && seat is long seated)
            {
                var picked = model.PickedUp((int)seated);
                var strip = Line(Strings.Get("ui.picked_up_count", picked.Count), "psheet__strip");
                if (model.CardMemoryAssist && picked.Count > 0)
                {
                    strip.text += "\n" + string.Join(" ", TrayGlyphs(picked));
                }

                _sheetActions.Add(strip);
            }

            if (_addFriend != null && _canAddFriend?.Invoke(id) == true)
            {
                Button add = null!;
                add = new Button(() =>
                {
                    _addFriend(id);
                    add.text = UiText.RequestSent;
                    add.SetEnabled(false);
                }) { text = UiText.AddFriend };
                add.AddToClassList("psheet__add");
                _sheetActions.Add(add);
            }

            if (!muted && _peerVolume != null && _setPeerVolume != null && _voice != null && _voice.State != VoiceState.Hidden)
            {
                // Their voice level on this phone only (GDD 11.4).
                var level = new SliderInt(UiText.Voice, 0, 100) { value = _peerVolume(id), tooltip = UiText.VoiceVolumeLabel };
                level.AddToClassList("sheet__level");
                level.AddToClassList("psheet__voice");
                var setLevel = _setPeerVolume;
                level.RegisterValueChangedCallback(evt => setLevel(id, evt.newValue));
                _sheetActions.Add(level);
            }

            var links = new VisualElement();
            links.AddToClassList("psheet__links");
            _sheetActions.Add(links);
            void Link(string text, Action click)
            {
                if (links.childCount > 0)
                {
                    links.Add(Line(UiText.Dot, "psheet__dot"));
                }

                var link = new Button(click) { text = text };
                link.AddToClassList("psheet__link");
                links.Add(link);
            }

            Link(muted ? UiText.Unmute : UiText.Mute, () =>
            {
                _setMuted(id, !muted);
                _sheetStatus.text = muted ? "" : Strings.Format(UiText.MutedNote, name);
                CloseSheetSoon();
            });
            if (_block != null)
            {
                Link(UiText.Block, () =>
                {
                    _block(id);
                    _sheetStatus.text = Strings.Format(UiText.Blocked, name);
                    CloseSheetSoon();
                });
            }

            if (_report != null)
            {
                Link(UiText.Report, () => ShowReasons(id, name));
            }

            OpenPlayerSheet();
        }

        /// <summary>Looks up a player's public profile for the sheet's handle, title and stats; null hides them.</summary>
        public Func<string, Awaitable<Profile?>>? LookUpProfile { get; set; }

        private async void FillProfile(string id, Label who, Label stats)
        {
            var profile = LookUpProfile == null ? null : await LookUpProfile(id);
            if (profile == null)
            {
                return;
            }

            who.text = string.IsNullOrEmpty(profile.Title)
                ? Strings.Format(UiText.PlayerLine, profile.Handle)
                : Strings.Format(UiText.PlayerLineTitle, profile.Handle, UiText.TitleName(profile.Title!));
            if (profile.Stats != null)
            {
                stats.text = Strings.Format(UiText.PlayerStats, Strings.Get("ui.rounds_count", profile.Stats.Rounds), Strings.Get("ui.times_bhabhi", profile.Stats.Bhabhis));
            }
        }

        private static Label Line(string text, string cls)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            return label;
        }

        private void OpenPlayerSheet()
        {
            _sheet.style.display = DisplayStyle.Flex;
            _dim.style.display = DisplayStyle.Flex;
        }

        private static IEnumerable<string> TrayGlyphs(IReadOnlyList<string> cards)
        {
            foreach (var card in cards)
            {
                yield return (card[0] == 'T' ? "10" : card.Substring(0, 1)) + CardElement.SuitGlyph(card[1]);
            }
        }

        /// <summary>
        /// Report (design: Report and block): six reasons as a single-choice list, "Also block" on by
        /// default, then Send report; the thanks line replaces the sheet.
        /// </summary>
        private void ShowReasons(string id, string name)
        {
            _sheetTitle.style.display = DisplayStyle.Flex;
            _sheetTitle.text = Strings.Format(UiText.ReportWhy, name);
            _sheetActions.Clear();
            _sheetStatus.text = "";
            string? chosen = null;
            var rows = new List<Button>();
            var list = new VisualElement();
            list.AddToClassList("report__list");
            foreach (var (reason, label) in UiText.ReportReasons)
            {
                var value = reason;
                var row = new Button { text = label };
                row.AddToClassList("report__reason");
                row.clicked += () =>
                {
                    chosen = value;
                    foreach (var other in rows)
                    {
                        other.EnableInClassList("report__reason--on", other == row);
                    }
                };
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("report__radio");
                row.Add(dot);
                rows.Add(row);
                list.Add(row);
            }

            _sheetActions.Add(list);
            var block = new Toggle(Strings.Format(UiText.AlsoBlock, name)) { value = _block != null };
            block.AddToClassList("report__block");
            block.style.display = _block != null ? DisplayStyle.Flex : DisplayStyle.None;
            _sheetActions.Add(block);
            var send = new Button(() =>
            {
                if (chosen == null)
                {
                    return;
                }

                if (block.value)
                {
                    _block?.Invoke(id);
                }

                _report?.Invoke(id, chosen, ok =>
                {
                    _sheetActions.Clear();
                    _sheetStatus.text = ok ? UiText.ReportThanks : UiText.Error("social.unavailable");
                    CloseSheetSoon();
                });
            }) { text = UiText.SendReport };
            send.AddToClassList("report__send");
            _sheetActions.Add(send);
        }

        private void SheetButton(string text, Action click)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("chip");
            _sheetActions.Add(button);
        }

        private void CloseSheetSoon() => _sheet.schedule.Execute(() =>
        {
            _sheet.style.display = DisplayStyle.None;
            _dim.style.display = _chatPanel.style.display == DisplayStyle.Flex ? DisplayStyle.Flex : DisplayStyle.None;
        }).StartingIn(2500);

        private bool Muted(long seat)
        {
            var model = _client.Model;
            return _isMuted != null && seat < model.Seats.Count && _isMuted(model.Seats[(int)seat].PlayerId);
        }

        /// <summary>Turns on the first-rounds hints for a live table (never for the tutorial).</summary>
        public void UseHints(ThullaHints hints)
        {
            _hints = hints;
            ShowDueHint();
        }

        private void ShowDueHint()
        {
            var hint = _hints?.Due(_client.Model, _thullaJustPlayed);
            _thullaJustPlayed = false;
            if (hint != null)
            {
                ShowCoach(hint, TutorialText.GotIt, HideCoach);
            }
        }

        public float Present(ServerMessage message)
        {
            var model = _client.Model;
            switch (message.Payload)
            {
                case MoveApplied applied when applied.Thulla == true:
                    // The slam (GDD 14.6): the shake and the slap now, the stamp 120 ms behind.
                    _sounds?.Play(Sound.Slam);
                    ShowStamp(model.Trick.Count >= BigPickup ? UiText.ThullaBig : UiText.Thulla, applied.Seat);
                    if (applied.Seat == model.MySeat)
                    {
                        Buzz(Haptic.ThullaGiven);
                        SetContext("gave_thulla");
                    }
                    else
                    {
                        _ring.BadgeFor(applied.Seat)?.Flag(UiText.ThullaFlag, ThullaMomentSeconds);
                    }

                    _thullaJustPlayed = true;
                    ShowDueHint();
                    return ThullaMomentSeconds;
                case MoveApplied applied:
                    _sounds?.Play(Sound.CardTap, SoundKit.TrickPitch(Math.Max(0, model.Trick.Count - 1)));
                    if (applied.Seat == model.MySeat)
                    {
                        Buzz(Haptic.CardPlay);
                    }

                    return 0.2f;
                case TrickResolved resolved when resolved.Picker != null:
                    // The pick-up: the pile flies to the picker, the count bumps as each card lands;
                    // when the picker is me each card glides into the slot the hand made for it.
                    var picker = resolved.Picker.Value;
                    var mine = picker == model.MySeat;
                    var landing = mine ? HandTarget() : _ring.BadgeFor(picker)?.worldBound;
                    if (landing != null)
                    {
                        _flight = mine ? PileView.Flight.SlotIn : PileView.Flight.Pickup;
                        _flightTarget = mine ? card => _hand.SlotOf(card) : (Func<string, Rect?>)(_ => null);
                        _flightFallback = landing.Value;
                        _flightLeft = PickupStartSeconds;
                    }

                    _sounds?.Play(Sound.Pickup, resolved.Cards.Count >= BigPickup ? 0.8f : 1f);
                    if (picker == model.MySeat)
                    {
                        Buzz(Haptic.ThullaReceived);
                        _stingLeft = StingSeconds;
                        SetContext("received_thulla");
                    }
                    else if (resolved.Cards.Count >= BigPickup)
                    {
                        SetContext("big_pickup");
                    }

                    return resolved.ThullaBy != null ? ThullaMomentSeconds : 0.8f;
                case TrickResolved _:
                    // Nobody picked up: the trick rests a moment, then slides onto the discard stack.
                    _flight = PileView.Flight.Discard;
                    _flightTarget = _ => null;
                    _flightFallback = _discard.Target;
                    _flightLeft = DiscardStartSeconds;
                    return 1.3f;
                case RoundStart _:
                    _voted = false;
                    _leftThisRound.Clear();
                    _escapes = 0;
                    _sounds?.Play(Sound.Deal);
                    SetContext(QuickStrip.Any);
                    if (!_recapShown && _hideRecapForGood != null)
                    {
                        _recapShown = true;
                        _recapLeft = RecapSeconds;
                        _recap.style.display = DisplayStyle.Flex;
                    }

                    return 0.4f;
                case RoundEnd end:
                    if (end.BhabhiSeat == model.MySeat)
                    {
                        Buzz(Haptic.Bhabhi);
                        SetContext("round_end_loser");
                    }
                    else
                    {
                        SetContext("round_end");
                    }

                    return 0f;
                case HandAsked asked:
                    PresentAsk(asked);
                    return 0f;
                case HandAnswered answered:
                    PresentAnswer(answered);
                    return 0f;
                case Presence presence:
                    PresentPresence(presence);
                    return 0f;
                default:
                    return 0f;
            }
        }

        /// <summary>The next player's name and card count for the ask (R32): the nearest active seat after me.</summary>
        private (string Name, long Cards)? AskTarget()
        {
            var model = _client.Model;
            if (model.MySeat == null)
            {
                return null;
            }

            var count = model.Seats.Count;
            for (var step = 1; step < count; step++)
            {
                var seat = (int)((model.MySeat.Value + step) % count);
                if (!model.IsEscaped(seat) && model.HandCount(seat) > 0)
                {
                    return (NameOf(seat), model.HandCount(seat));
                }
            }

            return null;
        }

        /// <summary>
        /// Ask for hand (R32 to R34): the first time ever, the ask is explained first and the button on
        /// the explanation sends it; afterwards one tap asks. The asked player can refuse.
        /// </summary>
        private void AskForHand()
        {
            var target = AskTarget();
            if (target == null)
            {
                return;
            }

            if (_hints?.ExplainAsk() == true)
            {
                var (name, cards) = target.Value;
                ShowCoach(Strings.Format(TutorialText.ExplainAsk, name, cards), Strings.Format(UiText.AskNamed, name), () =>
                {
                    HideCoach();
                    _client.TakeHand();
                });
                return;
            }

            _client.TakeHand();
        }

        private void PresentAsk(HandAsked asked)
        {
            var model = _client.Model;
            if (asked.Target == model.MySeat)
            {
                ShowAskSheet(asked);
                return;
            }

            if (asked.Asker == model.MySeat)
            {
                _hint.text = Strings.Format(UiText.AskedWaiting, NameOf(asked.Target));
                _hint.style.display = DisplayStyle.Flex;
                _hintLeft = SaySeconds;
                return;
            }

            _ring.BadgeFor(asked.Asker)?.Say(Strings.Format(UiText.AsksForHand, NameOf(asked.Target)), SaySeconds);
        }

        /// <summary>
        /// A player who drops mid-round is played by the auto-play bot (R44): everyone is told who left
        /// and that a bot has their seat, and told again when they are back.
        /// </summary>
        private void PresentPresence(Presence presence)
        {
            var model = _client.Model;
            if (presence.Seat == null || presence.Seat == model.MySeat || !model.InRound)
            {
                return;
            }

            var seat = presence.Seat.Value;
            var name = presence.DisplayName ?? NameOf(seat);
            var back = presence.Status == Hulla.Protocol.Status.Reconnected;
            if (!back && presence.Status != Hulla.Protocol.Status.Disconnected && presence.Status != Hulla.Protocol.Status.AutoPlay)
            {
                return;
            }

            if (back)
            {
                _leftThisRound.Remove(seat);
            }
            else
            {
                _leftThisRound[seat] = name;
            }

            _hint.text = back ? Strings.Format(UiText.PlayerBack, name) : Strings.Format(UiText.BotPlaysFor, name);
            _hint.style.display = DisplayStyle.Flex;
            _hintLeft = SaySeconds;
            // The seat's own tag already says "Bot playing"; only the return gets a bubble.
            if (back)
            {
                _ring.BadgeFor(seat)?.Say(UiText.BackAtTable, SaySeconds);
            }
        }

        /// <summary>Shares the room code from the results so someone can take a seat a player left (private rooms).</summary>
        public Action? Invite
        {
            set => _results.Invite = value;
        }

        private void PresentAnswer(HandAnswered answered)
        {
            var model = _client.Model;
            if (_askSheetOpen)
            {
                _askSheetOpen = false;
                _sheet.style.display = DisplayStyle.None;
                _dim.style.display = DisplayStyle.None;
            }

            if (answered.Accepted || answered.Reason != HandAnsweredPayloadReason.Declined)
            {
                return;
            }

            if (answered.Asker == model.MySeat)
            {
                _hint.text = Strings.Format(UiText.KeepsCards, NameOf(answered.Target));
                _hint.style.display = DisplayStyle.Flex;
                _hintLeft = SaySeconds;
                return;
            }

            _ring.BadgeFor(answered.Target)?.Say(UiText.KeepPlaying, SaySeconds);
        }

        /// <summary>The asked player decides (R33, R34): hand everything over and sit out safe, or keep playing.</summary>
        private void ShowAskSheet(HandAsked asked)
        {
            _sheetTitle.text = Strings.Format(UiText.AskSheetTitle, NameOf(asked.Asker));
            _sheetStatus.text = Strings.Format(UiText.AskSheetBody, asked.CardCount);
            _sheetActions.Clear();
            SheetButton(UiText.HandOver, () => AnswerAsk(true));
            SheetButton(UiText.KeepPlaying, () => AnswerAsk(false));
            _askSheetOpen = true;
            OpenPlayerSheet();
        }

        private void AnswerAsk(bool accept)
        {
            _askSheetOpen = false;
            _sheet.style.display = DisplayStyle.None;
            _dim.style.display = DisplayStyle.None;
            _client.AnswerHand(accept);
        }

        /// <summary>Per frame: bubbles, the recap, the stamp, the pick-up flight, the strip and the turn timers.</summary>
        public void Tick(float deltaSeconds)
        {
            foreach (var badge in _ring.Badges)
            {
                badge.Tick(deltaSeconds);
            }

            if (_micTipLeft > 0f)
            {
                _micTipLeft -= deltaSeconds;
                if (_micTipLeft <= 0f)
                {
                    ShowVoice();
                }
            }

            if (_voiceWhyLeft > 0f)
            {
                _voiceWhyLeft -= deltaSeconds;
                if (_voiceWhyLeft <= 0f)
                {
                    ShowVoice();
                }
            }

            Countdown(ref _recapLeft, deltaSeconds, _recap);
            _results.Tick(_client.Model, _voted, _nowMs());
            Countdown(ref _mySayLeft, deltaSeconds, _mySay);
            Countdown(ref _hintLeft, deltaSeconds, _hint);
            if (_stamp.Active)
            {
                _stamp.Advance(deltaSeconds);
                ApplyStamp();
            }

            if (_flightLeft > 0f)
            {
                _flightLeft -= deltaSeconds;
                if (_flightLeft <= 0f && _flight != null)
                {
                    _landing = _flight.Value;
                    var fallback = _landing == PileView.Flight.Discard ? _discard.Target : _flightFallback;
                    _pile.FlyTo(_landing, _flightTarget, fallback);
                    _flight = null;
                }
            }

            if (_stingLeft > 0f)
            {
                _stingLeft -= deltaSeconds;
                if (_stingLeft <= 0f)
                {
                    _sounds?.Play(Sound.Sting);
                }
            }

            if (_singleLegal != null)
            {
                _singleLegalLeft -= deltaSeconds;
                if (_singleLegalLeft <= 0f)
                {
                    var card = _singleLegal;
                    _singleLegal = null;
                    if (_client.Model.MyTurn && _client.Model.IsLegal(card))
                    {
                        _playedFrom = _hand.SlotOf(card);
                        _client.PlayCard(card);
                    }
                }
            }

            TickTimers(deltaSeconds);
        }

        private void TickTimers(float deltaSeconds)
        {
            var model = _client.Model;
            _pulseElapsed += deltaSeconds;
            if (model.MyTurn)
            {
                var now = _nowMs();
                var leftMs = Math.Max(0, _turnDeadlineMs - now);
                var total = Math.Max(1, _turnDeadlineMs - _turnStartMs);
                var secondsLeft = leftMs / 1000f;
                _me.ShowTimer((float)leftMs / total, secondsLeft, TimerPhase.Pulse(secondsLeft, _pulseElapsed, _reduceMotion));
                var seconds = (leftMs + 999) / 1000;
                if (seconds != _shownSeconds)
                {
                    _shownSeconds = seconds;
                    _statusSecs.text = Strings.Format(UiText.SecondsShort, seconds);
                    _statusSecs.style.display = seconds <= 10 ? DisplayStyle.Flex : DisplayStyle.None;
                    if (TimerPhase.TickAt(seconds) && _tickedAt != seconds)
                    {
                        _tickedAt = seconds;
                        _sounds?.Play(Sound.Tick, seconds == 2 ? 1.3f : 1f);
                    }
                }

                return;
            }

            // Opponents' deadlines are theirs alone (turn_start goes to the acting seat), so the ring on
            // their badge drains over the length of this player's last turn: a close estimate at one table.
            if (!model.InRound || model.TurnSeat == null || _turnLengthMs <= 0)
            {
                return;
            }

            var badge = _ring.BadgeFor(model.TurnSeat.Value);
            if (badge == null)
            {
                return;
            }

            var elapsed = _nowMs() - _opponentTurnStartMs;
            var left = Math.Max(0, _turnLengthMs - elapsed);
            var fraction = (float)left / _turnLengthMs;
            var theirSeconds = left / 1000f;
            badge.ShowTimer(fraction, theirSeconds, TimerPhase.Pulse(theirSeconds, _pulseElapsed, _reduceMotion));
        }

        /// <summary>Applies the play settings live; the visual ones arrive as theme classes on the shell.</summary>
        public void ApplySettings(bool oneTap, bool reduceMotion, bool largeCards, bool haptics, Action? hideRecapForGood)
        {
            _hand.OneTap = oneTap;
            // The tutorial always shows the larger hand (concept: Tutorial).
            _hand.SetLargeCards(largeCards || _tutorialHeader != null);
            _reduceMotion = reduceMotion;
            _pile.ReduceMotion = reduceMotion;
            _haptics = haptics;
            _hideRecapForGood = hideRecapForGood;
        }

        public void Dispose()
        {
            _client.Model.Changed -= Refresh;
            _client.Model.Chat -= OnChat;
            if (_voice != null)
            {
                _voice.Changed -= ShowVoice;
                _voice.SetTalking(false);
            }
        }

        /// <summary>
        /// Voice at this table (GDD 11.4): the mic beside chat, hold-to-talk on push-to-talk tables, the
        /// voice pill and the speaking rings. Hidden when the build, the server or the table has no voice.
        /// </summary>
        public void UseVoice(VoiceController voice)
        {
            _voice = voice;
            _mic.clicked += OnMicTapped;
            // Push-to-talk lives on the mic itself (owner, 28 Oct): hold it to talk, a tip says so.
            _micTip = new Label { pickingMode = PickingMode.Ignore };
            _micTip.AddToClassList("table__mic-tip");
            _mic.Add(_micTip);
            _mic.RegisterCallback<PointerDownEvent>(_ => OnMicDown(), TrickleDown.TrickleDown);
            _mic.RegisterCallback<PointerUpEvent>(_ => OnMicUp(), TrickleDown.TrickleDown);
            _mic.RegisterCallback<PointerLeaveEvent>(_ => OnMicUp());
            _mic.RegisterCallback<PointerCancelEvent>(_ => OnMicUp());
            voice.Changed += ShowVoice;
            ShowVoice();
        }

        /// <summary>Each player's voice level for the player sheet, 0 to 100.</summary>
        public void UseVoiceLevels(Func<string, int> get, Action<string, int> set)
        {
            _peerVolume = get;
            _setPeerVolume = set;
        }

        /// <summary>The speaker toggle's text; empty when it is hidden. Read by tests.</summary>
        public string SpeakerNote => _speaker.style.display == DisplayStyle.Flex ? _speaker.text : "";

        /// <summary>The voice pill's text; empty when it is hidden. Read by tests.</summary>
        public string VoiceNote => _voicePill.style.display == DisplayStyle.Flex ? _voicePill.text : "";

        public bool ShowsMic => _mic.style.display == DisplayStyle.Flex;

        /// <summary>True while the mic offers hold-to-talk (its tip is armed). Read by tests.</summary>
        public bool ShowsTalk => _micTip != null && _micTip.ClassListContains("table__mic-tip--ready");

        private Label? _micTip;
        private bool _micHeld;
        private bool _micTalked;
        private float _micTipLeft;
        private const float MicTipSeconds = 4f;

        private bool PushReady => _voice is { } v && v.CanSpeak && v.MicOn && v.Mode == VoiceMode.PushToTalk && v.State == VoiceState.Live;

        private void OnMicDown()
        {
            if (!PushReady)
            {
                return;
            }

            _micHeld = true;
            _micTalked = true;
            _voice!.SetTalking(true);
            ShowVoice();
        }

        private void OnMicUp()
        {
            if (!_micHeld)
            {
                return;
            }

            _micHeld = false;
            _voice?.SetTalking(false);
            ShowVoice();
        }

        private void OnMicTapped()
        {
            if (_voice == null)
            {
                return;
            }

            // A hold that talked is not a tap: the mic stays on.
            if (_micTalked)
            {
                _micTalked = false;
                return;
            }

            if (!_voice.MicOn)
            {
                _voiceWhyLeft = VoiceWhySeconds;
            }

            _voice.ToggleMic();
            ShowVoice();
        }

        private void ShowVoice()
        {
            var voice = _voice;
            if (voice == null)
            {
                return;
            }

            var state = voice.State;
            var hidden = state == VoiceState.Hidden;
            // A watcher sees the mic too, off and inert (design: Watching a table): only seats speak.
            _mic.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            _mic.SetEnabled(voice.CanSpeak);
            _mic.EnableInClassList("table__mic--off", !voice.CanSpeak || !voice.MicOn);
            var retry = state == VoiceState.Failed || state == VoiceState.Paused;
            _mic.text = retry && voice.MicOn ? UiText.VoiceRetry : voice.MicOn ? UiText.MicOn : UiText.MicOff;
            _mic.EnableInClassList("table__mic--on", voice.MicOn && !retry);
            _mic.EnableInClassList("table__mic--retry", retry && voice.MicOn);
            var push = !hidden && voice.CanSpeak && voice.MicOn && voice.Mode == VoiceMode.PushToTalk && state == VoiceState.Live;
            _talk.style.display = DisplayStyle.None;
            if (_micTip != null)
            {
                if (push && !_micTip.ClassListContains("table__mic-tip--ready"))
                {
                    _micTipLeft = MicTipSeconds;
                }

                _micTip.EnableInClassList("table__mic-tip--ready", push);
                _micTip.text = voice.Talking ? UiText.Talking : UiText.HoldToTalk;
                _micTip.style.display = push && (voice.Talking || _micTipLeft > 0f) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _mic.EnableInClassList("table__mic--held", voice.Talking);
            _talk.text = voice.Talking ? UiText.Talking : UiText.HoldToTalk;
            _talk.EnableInClassList("table__talk--held", voice.Talking);
            var asking = _voiceWhyLeft > 0f && (state == VoiceState.Idle || state == VoiceState.Waiting || state == VoiceState.Joining);
            var note = hidden ? "" : asking ? UiText.VoiceWhy : VoiceText(voice);
            _voicePill.text = note;
            _voicePill.style.display = note.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            // The speaker: hear the table or not; with it off and the mic off, voice is left altogether.
            var speakerShown = !hidden && state != VoiceState.Suspended && (state == VoiceState.Live || !voice.SpeakerOn);
            _speaker.style.display = speakerShown ? DisplayStyle.Flex : DisplayStyle.None;
            _speaker.text = voice.SpeakerOn ? UiText.VoicesOn : UiText.VoicesOff;
            _speaker.EnableInClassList("voice-speaker--off", !voice.SpeakerOn);
            var model = _client.Model;
            _me.SetSpeaking(!hidden && voice.IsSpeaking(model.UserId));
            foreach (var seat in model.Seats)
            {
                _ring.BadgeFor(seat.Seat)?.SetSpeaking(!hidden && voice.IsSpeaking(seat.PlayerId));
            }
        }

        private static string VoiceText(VoiceController voice) => voice.State switch
        {
            VoiceState.Waiting => UiText.VoiceWaiting,
            VoiceState.Joining => UiText.VoiceJoining,
            VoiceState.Paused => UiText.VoicePaused,
            VoiceState.Failed => UiText.VoiceFailed,
            VoiceState.Suspended => UiText.VoiceSuspended,
            VoiceState.NoPermission => UiText.VoiceNoPermission,
            VoiceState.Live when !voice.CanSpeak => UiText.VoiceListening,
            _ => "",
        };

        private void Buzz(Haptic kind)
        {
            if (_haptics)
            {
                Haptics.Play(kind);
            }
        }

        private static void Countdown(ref float left, float deltaSeconds, VisualElement element)
        {
            if (left <= 0f)
            {
                return;
            }

            left -= deltaSeconds;
            if (left <= 0f)
            {
                element.style.display = DisplayStyle.None;
            }
        }

        private void ShowHint(string card)
        {
            CardRefused?.Invoke(card);
            var model = _client.Model;
            string text;
            switch (PlayHint.For(model, card))
            {
                case PlayHint.Reason.MustFollow:
                    text = Strings.Format(UiText.MustFollow, CardElement.SuitGlyph(model.LedSuit ?? 'S'));
                    break;
                case PlayHint.Reason.LeadAceOfSpades:
                    text = UiText.LeadAceOfSpades;
                    break;
                case PlayHint.Reason.NotYourTurn:
                    text = UiText.NotYourTurn;
                    break;
                case PlayHint.Reason.None:
                    return;
                default:
                    text = UiText.NotPlayableNow;
                    break;
            }

            _hint.text = text;
            _hint.style.display = DisplayStyle.Flex;
            _hintLeft = SaySeconds;
        }

        private void BuildChatPanel(VisualElement phrases, VisualElement emotes)
        {
            foreach (var phrase in _chat.Phrases)
            {
                var id = phrase.Id;
                var button = new Button(() => Say(() => _client.SendQuickChat(id))) { text = phrase.Text, tooltip = phrase.Translation };
                button.AddToClassList("chip");
                phrases.Add(button);
            }

            foreach (var emote in _chat.Emotes)
            {
                var id = emote;
                var tile = EmoteArt.Tile(id, UiText.EmoteLabel(id), !CanSend(id), () => TapEmote(id));
                _emoteTiles[id] = tile;
                emotes.Add(tile);
            }
        }

        /// <summary>Which emote packs the player owns (GDD 11.3); until set, only the free pack.</summary>
        public void UseEmotePacks(Func<string, bool> owns)
        {
            _ownsPack = owns;
            foreach (var pair in _emoteTiles)
            {
                EmoteArt.SetLocked(pair.Value, !CanSend(pair.Key));
            }
        }

        private bool CanSend(string emoteId)
        {
            var pack = _chat.EmotePack(emoteId) ?? EmoteArt.FreePack;
            return _ownsPack?.Invoke(pack) ?? pack == EmoteArt.FreePack;
        }

        /// <summary>An owned emote is sent; a locked one says which shop pack it comes from.</summary>
        private void TapEmote(string emoteId)
        {
            if (CanSend(emoteId))
            {
                Say(() => _client.SendEmote(emoteId));
                return;
            }

            var grid = _root.Q<VisualElement>("chat-emotes");
            if (grid != null)
            {
                EmoteArt.ShowLockNote(grid, Strings.Format(UiText.EmotePackLocked, ItemText.Name(_chat.EmotePack(emoteId) ?? "")));
            }
        }

        /// <summary>The strip follows the last beat at the table (design 7.6); each change rotates the picks.</summary>
        private void SetContext(string context)
        {
            _context = context;
            _stripSeed++;
            RefreshStrip();
        }

        private void RefreshStrip()
        {
            _strip.Clear();
            var picks = QuickStrip.Pick(_chat.Phrases, _context, _stripSeed);
            if (picks.Count == 0)
            {
                _strip.style.display = DisplayStyle.None;
                return;
            }

            // The strip steps aside in the tutorial and while Ask for hand fills the bar.
            _strip.style.display = _tutorialHeader != null || _root.ClassListContains("table--take-hand") ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (var phrase in picks)
            {
                var id = phrase.Id;
                var chip = new Button(() => _client.SendQuickChat(id)) { text = phrase.Text, tooltip = phrase.Translation };
                chip.AddToClassList("chip");
                chip.AddToClassList("quick-strip__chip");
                _strip.Add(chip);
            }

            var more = new Button(() => SetChatOpen(true, "phrases")) { text = UiText.More };
            more.AddToClassList("chip");
            more.AddToClassList("quick-strip__more");
            _strip.Add(more);
        }

        private void Say(Action send)
        {
            send();
            SetChatOpen(false);
        }

        private void SetChatOpen(bool open) => SetChatOpen(open, "chat");

        /// <summary>The chat sheet (design: Chat and quick-chat panel), on the tab asked for; "…" opens Phrases.</summary>
        private void SetChatOpen(bool open, string tab)
        {
            _chatPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            _dim.style.display = open || _sheet.style.display == DisplayStyle.Flex ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                ShowChatTab(tab);
            }
        }

        private string _chatTab = "chat";

        private void BuildChatTabs(VisualElement tabs)
        {
            foreach (var (name, label) in new[] { ("chat", UiText.Chat), ("phrases", UiText.Phrases), ("emotes", UiText.Emotes) })
            {
                var tab = name;
                var item = new Button(() => ShowChatTab(tab)) { name = "chat-tab-" + tab, text = label };
                item.AddToClassList("segment__item");
                tabs.Add(item);
            }
        }

        private void ShowChatTab(string tab)
        {
            _chatTab = tab;
            foreach (var item in _root.Q<VisualElement>("chat-tabs").Children())
            {
                item.EnableInClassList("segment__item--on", item.name == "chat-tab-" + tab);
            }

            var chat = tab == "chat";
            _root.Q<VisualElement>("chat-log-scroll").style.display = chat ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<VisualElement>(className: "chat-panel__compose").style.display = chat && _sendText != null ? DisplayStyle.Flex : DisplayStyle.None;
            _chatStatus.style.display = chat ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<VisualElement>("chat-picks").style.display = chat ? DisplayStyle.None : DisplayStyle.Flex;
            _root.Q<VisualElement>("chat-phrases").style.display = tab == "phrases" ? DisplayStyle.Flex : DisplayStyle.None;
            _root.Q<VisualElement>("chat-emotes").style.display = tab == "emotes" ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Closes whichever sheet is open on the table (a tap on the dim).</summary>
        private void CloseSheets()
        {
            if (_askSheetOpen)
            {
                return;
            }

            _chatPanel.style.display = DisplayStyle.None;
            _sheet.style.display = DisplayStyle.None;
            _dim.style.display = DisplayStyle.None;
        }

        /// <summary>Leaving mid-round costs the seat: one question first (design: Dialogs), straight out otherwise.</summary>
        private void AskLeave()
        {
            var model = _client.Model;
            var playing = model.InRound && !model.Spectating && model.MySeat is long mine && !model.IsEscaped((int)mine);
            if (!playing || _tutorialHeader != null)
            {
                _leave();
                return;
            }

            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, UiText.LeaveTableTitle);
            panel.Add(Hulla.Presentation.Navigation.Sheet.Line(UiText.LeaveTableBody));
            var answers = Hulla.Presentation.Navigation.Sheet.Answers(panel);
            answers.Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.Stay, () => Hulla.Presentation.Navigation.Sheet.Close(panel)));
            answers.Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.Leave, () =>
            {
                Hulla.Presentation.Navigation.Sheet.Close(panel);
                _leave();
            }, "danger"));
        }

        /// <summary>The ⋯ menu (design: Table): Rules, Score and Settings.</summary>
        private void OpenMenu()
        {
            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, null);
            panel.AddToClassList("menu-sheet");
            void Item(string text, Action action)
            {
                var button = Hulla.Presentation.Navigation.Sheet.Button(text, () =>
                {
                    Hulla.Presentation.Navigation.Sheet.Close(panel);
                    action();
                });
                button.AddToClassList("menu-sheet__item");
                panel.Add(button);
            }

            if (_openRules != null)
            {
                Item(RulebookText.Title, () => _openRules(null));
            }

            Item(UiText.Score, OpenScore);
            if (_openSettings != null)
            {
                Item(UiText.Settings, _openSettings);
            }
        }

        /// <summary>The scoreboard (design: Scoreboard): times each player was the Bhabhi at this table, fewest first.</summary>
        private void OpenScore()
        {
            var model = _client.Model;
            var rounds = _roundsPlayed;
            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, Strings.Format(UiText.ScoreTitle, Math.Max(1, rounds + (model.InRound ? 1 : 0))));
            panel.AddToClassList("score-sheet");
            // A header band, then one plate per player with the face, the name and Bhabhi · cards (concept: Score).
            var head = new VisualElement();
            head.AddToClassList("score__row");
            head.AddToClassList("score__row--head");
            head.Add(new Label(UiText.ScorePlayer) { name = "score-name" });
            head.Add(new Label(model.InRound ? UiText.ScoreBhabhiCards : UiText.ScoreBhabhi));
            panel.Add(head);
            var rows = new List<(string Name, int Count, long Cards, string? Avatar, bool Me)>();
            foreach (var seat in model.Seats)
            {
                if (seat.PlayerId == null)
                {
                    continue;
                }

                _bhabhiTally.TryGetValue(seat.PlayerId, out var count);
                rows.Add((seat.Seat == model.MySeat ? UiText.You : seat.DisplayName ?? "?", count, model.HandCount((int)seat.Seat), seat.Avatar, seat.Seat == model.MySeat));
            }

            rows.Sort((a, b) => a.Count != b.Count ? a.Count.CompareTo(b.Count) : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
            foreach (var (name, count, cards, avatar, me) in rows)
            {
                var row = new VisualElement();
                row.AddToClassList("score__row");
                row.AddToClassList("score__plate");
                row.EnableInClassList("score__row--me", me);
                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                face.AddToClassList("seat__avatar");
                face.AddToClassList("score__face");
                var initial = new Label { pickingMode = PickingMode.Ignore };
                initial.AddToClassList("seat__initial");
                face.Add(initial);
                AvatarArt.Apply(face, initial, avatar, name);
                row.Add(face);
                row.Add(new Label(name) { name = "score-name" });
                row.Add(new Label(model.InRound ? Strings.Format(UiText.ScoreValue, count, cards) : Strings.Format("{0}", count)) { name = "score-value" });
                panel.Add(row);
            }

            if (rounds > 0)
            {
                panel.Add(Hulla.Presentation.Navigation.Sheet.Line(Strings.Get("ui.score_rounds", rounds), "score__foot"));
            }
        }

        private void TallyRound()
        {
            var result = _client.Model.LastResult;
            if (result == null || !_talliedRounds.Add(result.RoundId))
            {
                return;
            }

            _roundsPlayed++;
            var seat = result.BhabhiSeat;
            if (seat is long bhabhi && bhabhi < _client.Model.Seats.Count && _client.Model.Seats[(int)bhabhi].PlayerId is string id)
            {
                _bhabhiTally.TryGetValue(id, out var count);
                _bhabhiTally[id] = count + 1;
            }
        }

        /// <summary>The tick on the code plate (design: Fair deal): the commitment now, the seed once the round ends.</summary>
        private void OpenFairDeal()
        {
            var fairness = _client.Model.LastResult?.Fairness;
            if (fairness != null && _explainFairness != null && !_client.Model.InRound)
            {
                _explainFairness(fairness);
                return;
            }

            // During the round (concept: Fair deal): the code the server committed to, a copy of it,
            // how it works, and the reminder that every round is dealt this way.
            var commitment = _client.Model.Commitment;
            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, UiText.FairDeal);
            panel.AddToClassList("fair-sheet");
            panel.Add(Hulla.Presentation.Navigation.Sheet.Line(commitment.Length > 0 ? Strings.Format(UiText.FairRoundBody, ShortCode(commitment)) : UiText.SeedLater, "fair-sheet__body"));
            var answers = Hulla.Presentation.Navigation.Sheet.Answers(panel);
            if (commitment.Length > 0)
            {
                answers.Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.CopyCommitment, () => GUIUtility.systemCopyBuffer = commitment));
            }

            answers.Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.FairHow, () =>
            {
                var how = Hulla.Presentation.Navigation.Sheet.Open(_root, UiText.FairHowTitle);
                how.Add(Hulla.Presentation.Navigation.Sheet.Line(UiText.FairHowBody));
                Hulla.Presentation.Navigation.Sheet.Answers(how).Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.Close, () => Hulla.Presentation.Navigation.Sheet.Close(how), "yes"));
            }));
            panel.Add(Hulla.Presentation.Navigation.Sheet.Line(UiText.FairEvery, "fair-sheet__foot"));
        }

        private static string ShortCode(string value) => value.Length > 12 ? value.Substring(0, 4) + "…" + value.Substring(value.Length - 4) : value;

        /// <summary>The Watching sheet (design: Watching a table): who watches, the free seats, Seat for the host.</summary>
        private void OpenWatchers()
        {
            var model = _client.Model;
            var free = 0;
            foreach (var seat in model.Seats)
            {
                if (!seat.Occupied)
                {
                    free++;
                }
            }

            foreach (var watcher in model.Spectators)
            {
                if (watcher.SeatNext)
                {
                    free--;
                }
            }

            free = Math.Max(0, free);
            // The Watching sheet (concept: Watching sheet): the count and the free seats on one line, a plate
            // per watcher with the face, Seated next round or the host's Seat, and a row tap for the player sheet.
            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, null);
            panel.AddToClassList("watch-sheet");
            var head = new VisualElement();
            head.AddToClassList("watch-sheet__head");
            head.Add(Line(Strings.Format(UiText.WatchingCount, model.Spectators.Count), "watch-sheet__title"));
            head.Add(Line(free > 0 ? Strings.Get("ui.seats_free_next", free) : UiText.WatchingNoSeats, "watch-sheet__free"));
            panel.Add(head);
            foreach (var watcher in model.Spectators)
            {
                var id = watcher.UserId;
                var watcherName = watcher.Name;
                var row = new Button(() =>
                {
                    Hulla.Presentation.Navigation.Sheet.Close(panel);
                    ShowPlayer(id, watcherName, AvatarArt.ForAccount(id), null);
                }) { text = "" };
                row.AddToClassList("watch-row");
                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                face.AddToClassList("seat__avatar");
                face.AddToClassList("watch-row__face");
                var initial = new Label { pickingMode = PickingMode.Ignore };
                initial.AddToClassList("seat__initial");
                face.Add(initial);
                AvatarArt.Apply(face, initial, AvatarArt.ForAccount(id), watcherName);
                row.Add(face);
                row.Add(Line(watcherName, "watch-row__name"));
                if (watcher.SeatNext)
                {
                    row.Add(Line(UiText.SeatedNext, "watch-row__tag"));
                }
                else if (model.IsHost && SeatSpectator != null)
                {
                    var seatButton = new Button(() =>
                    {
                        SeatSpectator?.Invoke(id);
                        Hulla.Presentation.Navigation.Sheet.Close(panel);
                    }) { text = UiText.SeatThem };
                    seatButton.AddToClassList("watch-row__seat");
                    seatButton.SetEnabled(free > 0);
                    row.Add(seatButton);
                }

                panel.Add(row);
            }

            panel.Add(Line(UiText.WatchingTapRow, "watch-sheet__foot"));
        }

        private Func<string>? _tutorialHeader;

        /// <summary>
        /// The tutorial's table (design: Tutorial): ✕ to skip, the step counter in the plate, no quick
        /// chat, mic, chat or menu, since the seats are scripted.
        /// </summary>
        public void UseTutorial(Func<string> header)
        {
            _tutorialHeader = header;
            _root.AddToClassList("table--tutorial");
            _root.AddToClassList("cards--large");
            _hand.SetLargeCards(true);
            _strip.style.display = DisplayStyle.None;
            var leave = _root.Q<Button>("leave");
            leave.text = "";
            leave.tooltip = UiText.Close;
            leave.AddToClassList("table__close");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("table__icon");
            icon.AddToClassList("table__icon--close");
            leave.Add(icon);
            RefreshInfo();
        }

        /// <summary>The host seats a watcher from the Watching sheet; null hides Seat.</summary>
        public Action<string>? SeatSpectator { get; set; }

        private void OnChat(ChatLine line)
        {
            if (Muted(line.Seat))
            {
                return;
            }

            if (line.PhraseId == null && !string.IsNullOrEmpty(line.EmoteId))
            {
                // The sticker shows where it lands: by the seat it was thrown at, else by the sender (GDD 11.3).
                var at = line.TargetSeat ?? line.Seat;
                if (at == _client.Model.MySeat)
                {
                    SeatBadge.ShowEmote(_mySay, line.EmoteId);
                    _mySay.style.display = DisplayStyle.Flex;
                    _mySayLeft = SaySeconds;
                }
                else
                {
                    _ring.BadgeFor(at)?.SayEmote(line.EmoteId!, SaySeconds);
                }

                return;
            }

            var text = _chat.TextFor(line.PhraseId ?? "") ?? line.PhraseId ?? "";
            if (line.Seat == _client.Model.MySeat)
            {
                SeatBadge.ShowEmote(_mySay, null);
                _mySay.text = text;
                _mySay.style.display = DisplayStyle.Flex;
                _mySayLeft = SaySeconds;
                return;
            }

            _ring.BadgeFor(line.Seat)?.Say(text, SaySeconds);
        }

        /// <summary>A free-text line from the table channel: into the log and onto the sender's seat.</summary>
        public void ShowText(TextLine line)
        {
            if (_isMuted?.Invoke(line.SenderId) == true)
            {
                return;
            }

            var model = _client.Model;
            long? seat = null;
            foreach (var info in model.Seats)
            {
                if (info.PlayerId == line.SenderId)
                {
                    seat = info.Seat;
                }
            }

            var name = seat != null ? NameOf(seat.Value) : line.SenderName;
            var entry = new Label(name + ": " + line.Text);
            entry.AddToClassList("chat-log__line");
            _chatLog.Insert(0, entry);
            while (_chatLog.childCount > ChatLogLines)
            {
                _chatLog.RemoveAt(_chatLog.childCount - 1);
            }

            if (seat == model.MySeat)
            {
                _mySay.text = line.Text;
                _mySay.style.display = DisplayStyle.Flex;
                _mySayLeft = SaySeconds;
            }
            else if (seat != null)
            {
                _ring.BadgeFor(seat.Value)?.Say(line.Text, SaySeconds);
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

        private void VoteAgain()
        {
            _voted = true;
            _client.VoteRematch(true);
            Refresh();
        }

        /// <summary>The stamp lands at one of three angles, chosen by the seat so a redraw agrees (GDD 14.6).</summary>
        private void ShowStamp(string text, long seat)
        {
            _toast.text = text;
            _toast.style.rotate = new Rotate(StampAngle(seat));
            var seats = _client.Model.Seats;
            CosmeticArt.Apply(_burst, seat < seats.Count ? seats[(int)seat].Celebration : null);
            _burst.style.rotate = new Rotate(-StampAngle(seat));
            _stampSounded = false;
            _stamp.Play(_reduceMotion);
            ApplyStamp();
        }

        public static float StampAngle(long seat) => (seat % 3) switch { 0 => -7f, 1 => 4f, _ => -2f };

        private void ApplyStamp()
        {
            _toast.style.display = _stamp.Visible ? DisplayStyle.Flex : DisplayStyle.None;
            _toast.style.opacity = _stamp.Opacity;
            _toast.style.scale = new Scale(new Vector2(_stamp.Scale, _stamp.Scale));
            _burst.style.display = _stamp.Visible && _burst.ClassListContains(CosmeticArt.Class) ? DisplayStyle.Flex : DisplayStyle.None;
            _burst.style.opacity = _stamp.Opacity;
            _burst.style.scale = new Scale(new Vector2(_stamp.Scale, _stamp.Scale));
            _felt.style.translate = new Translate(_stamp.ShakeX, 0f);
            if (_stamp.Visible && !_stampSounded)
            {
                // The stamp's own sound lands with it, 120 ms after the slap (GDD 14.6).
                _stampSounded = true;
                _sounds?.Play(Sound.Stamp);
            }
        }

        private void OnPileCardArrived(string card)
        {
            switch (_landing)
            {
                case PileView.Flight.Discard:
                    _discard.Arrive();
                    break;
                case PileView.Flight.SlotIn:
                    _hand.Reveal(card);
                    break;
                default:
                    var picker = _client.Model.LastPicker;
                    if (picker != null && picker != _client.Model.MySeat)
                    {
                        _ring.BadgeFor(picker.Value)?.Bump();
                    }

                    break;
            }
        }

        /// <summary>Where picked-up cards fly when they are mine: the top of the hand.</summary>
        private Rect HandTarget()
        {
            var bounds = _handArea.worldBound;
            return new Rect(bounds.center.x - 1f, bounds.center.y - 1f, 2f, 2f);
        }

        /// <summary>
        /// Your equipped cosmetics at the table (GDD 12.11): your table under the seats, your card back on the
        /// discard stack and your card face style on the cards; each opponent's fan shows their own back.
        /// Without a table theme the surface colours the felt instead.
        /// </summary>
        private void RefreshSurface()
        {
            var me = _client.Model.MySeat is long mine && mine < _client.Model.Seats.Count ? _client.Model.Seats[(int)mine] : default;
            CosmeticArt.Apply(_rail, me.TableSurface);
            _discard.SetBack(me.CardBack);
            CosmeticArt.ApplyFace(_root, me.CardFace);
            if (Skinned(_felt))
            {
                _felt.style.backgroundColor = new StyleColor(StyleKeyword.Null);
                return;
            }

            var colour = CosmeticLook.TableColour(me.TableSurface);
            _felt.style.backgroundColor = colour is UnityEngine.Color c ? new StyleColor(c) : new StyleColor(StyleKeyword.Null);
        }

        private static bool Skinned(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (e.ClassListContains(Hulla.Presentation.Theme.ThemeClasses.Skinned))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The seat's display name; the player's own seat is named too, tagged "You" where it is drawn.</summary>
        private string NameOf(long seat)
        {
            var model = _client.Model;
            var name = seat < model.Seats.Count ? model.Seats[(int)seat].DisplayName : null;
            return name ?? (seat == model.MySeat ? UiText.You : "?");
        }

        private string? AvatarOf(long seat)
        {
            var model = _client.Model;
            return seat < model.Seats.Count ? model.Seats[(int)seat].Avatar : null;
        }

        /// <summary>The player's own face and name in the bar (GDD 15.6); their turn and lead show on it, not as text.</summary>
        private void RefreshMe()
        {
            var model = _client.Model;
            if (model.Spectating || model.MySeat is not long mine || mine >= model.Seats.Count)
            {
                _me.style.display = DisplayStyle.None;
                RefreshStatus();
                return;
            }

            var info = model.Seats[(int)mine];
            _me.style.display = DisplayStyle.Flex;
            var escaped = model.IsEscaped((int)mine);
            // Out of cards: the seat stays for watching, chat and voice (R25), and says so.
            var state = model.InRound && escaped ? UiText.OutWatching : "";
            _me.Show(info.DisplayName ?? UiText.You, info.Avatar, true, model.InRound && model.TurnSeat == mine, model.InRound && model.LeaderSeat == mine && !escaped, state);
            RefreshStatus();
        }

        /// <summary>
        /// The status pill beside the player's own face (design: Table, own-seat strip): what to do now,
        /// whom we wait for, or that the player is out; the seconds join it in the last ten.
        /// </summary>
        private void RefreshStatus()
        {
            var model = _client.Model;
            string text;
            if (model.Spectating)
            {
                var host = model.HostSeat is long h && h < model.Seats.Count ? model.Seats[(int)h].DisplayName ?? "" : "";
                text = model.IsPrivate && host.Length > 0 ? Strings.Format(UiText.WatchingPill, host) : UiText.Watching;
            }
            else if (!model.InRound || model.MySeat is not long mine)
            {
                text = "";
            }
            else if (model.IsEscaped((int)mine))
            {
                text = UiText.OutWatching;
            }
            else if (model.MyTurn)
            {
                text = model.LedSuit is char suit
                    ? (model.HoldsSuit(suit) ? Strings.Format(UiText.TurnFollow, CardElement.SuitGlyph(suit)) : UiText.TurnAny)
                    : model.Trick.Count == 0 && model.DiscardCount == 0 && System.Linq.Enumerable.Contains(model.Hand, "AS") ? UiText.LeadAceOfSpades : UiText.TurnLead;
            }
            else if (model.TurnSeat is long turn)
            {
                text = Strings.Format(UiText.WaitingForSeat, NameOf(turn));
            }
            else
            {
                text = "";
            }

            _statusText.text = text;
            _root.EnableInClassList("table--watching", model.Spectating);
            // An empty pill keeps its room, so the mic and chat stay at the right edge.
            _status.style.visibility = text.Length == 0 ? UnityEngine.UIElements.Visibility.Hidden : UnityEngine.UIElements.Visibility.Visible;
            _status.EnableInClassList("table__status--mine", model.MyTurn);
            if (!model.MyTurn)
            {
                _statusSecs.style.display = DisplayStyle.None;
            }
        }

        private void Refresh()
        {
            if (_hints != null && _coach.style.display != DisplayStyle.Flex)
            {
                ShowDueHint();
            }

            NoticeEscapes();
            RefreshInfo();
            RefreshSurface();
            RefreshMe();
            RefreshOpponents();
            RefreshPile();
            RefreshHand();
            RefreshTurn();
            RefreshResults();
            ShowVoice();
        }

        /// <summary>An escape (GDD 14.5, 16.2): a chime, a success haptic for the escapee, and the strip follows.</summary>
        private void NoticeEscapes()
        {
            var model = _client.Model;
            if (model.EscapeOrder.Count <= _escapes)
            {
                _escapes = model.EscapeOrder.Count;
                return;
            }

            var mine = false;
            for (var i = _escapes; i < model.EscapeOrder.Count; i++)
            {
                mine |= model.EscapeOrder[i] == model.MySeat;
            }

            _escapes = model.EscapeOrder.Count;
            _sounds?.Play(Sound.Escape);
            if (mine)
            {
                Buzz(Haptic.Escape);
                SetContext("escaped");
                if (model.InRound)
                {
                    // Told plainly: safe for the round, still at the table for the chat and the voice.
                    ShowCoach(UiText.YouAreOut, TutorialText.GotIt, HideCoach);
                }
            }
            else
            {
                SetContext("escaped_other");
            }
        }

        private void RefreshInfo()
        {
            var model = _client.Model;
            var text = model.IsPrivate && model.Code.Length > 0 ? model.Code : model.IsRanked ? RankedText.Title : UiText.QuickPlayTable;
            // The code with the fair-deal tick; the eye and its count when someone watches (design: Table, Watching).
            if (model.FairDealCommitted || model.LastResult != null)
            {
                text += " ✓";
            }

            _info.text = _tutorialHeader?.Invoke() ?? text;
            var watchers = model.Spectators.Count;
            var showEye = watchers > 0;
            _watchIcon.style.display = showEye ? DisplayStyle.Flex : DisplayStyle.None;
            _watchCount.style.display = showEye ? DisplayStyle.Flex : DisplayStyle.None;
            _watchCount.text = Strings.Format("{0}", watchers);
            TallyRound();
        }

        private void RefreshOpponents()
        {
            var model = _client.Model;
            var count = model.SeatCount > 0 ? model.SeatCount : model.Seats.Count;
            _ring.Spectating = model.Spectating;
            _ring.Layout(count, model.MySeat ?? 0);
            var shown = model.Spectating ? count : count - 1;
            if (model.TurnSeat != _opponentTurnSeat)
            {
                _opponentTurnSeat = model.TurnSeat;
                _opponentTurnStartMs = _nowMs();
            }

            for (var i = 0; i < shown && i < _ring.Badges.Count; i++)
            {
                var seat = _ring.SeatAt(i);
                var info = seat < model.Seats.Count ? model.Seats[(int)seat] : default;
                var state = info.Bot
                    ? UiText.BotTag
                    : model.IsEscaped((int)seat)
                    ? UiText.Escaped
                    : info.Occupied && !info.Connected
                        ? UiText.SeatAwayAutoPlay
                    : info.AutoPlay
                        ? UiText.SeatAutoPlay
                        : "";
                var leader = model.InRound && model.LeaderSeat == seat && !model.IsEscaped((int)seat);
                var voids = model.InRound && model.CardMemoryAssist ? model.KnownVoids((int)seat) : null;
                _ring.Badges[i].Show(NameOf(seat), model.HandCount((int)seat), model.TurnSeat == seat && model.InRound, state, leader, voids, info.Avatar);
                _ring.Badges[i].ShowBack(info.CardBack);
            }
        }

        private void RefreshPile()
        {
            var model = _client.Model;
            var showingLast = model.Trick.Count == 0 && model.LastTrick.Count > 0;
            var source = showingLast ? model.LastTrick : model.Trick;
            _plays.Clear();
            foreach (var play in source)
            {
                _plays.Add((play.Card, NameOf(play.Seat), play.Seat));
            }

            _pile.Show(_plays, showingLast, SeatOrigin);
            var discarded = model.DiscardCount - _discardShown;
            if (discarded > 0 && showingLast && model.LastPicker == null && model.LastTrick.Count == discarded)
            {
                // The trick that just resolved is on its way; the stack counts each card as it lands.
                _discard.Expect((int)discarded);
            }

            _discardShown = model.DiscardCount;
            _discard.Show(model.DiscardCount);
            _pileNote.text = showingLast && model.LastPicker != null ? Strings.Format(UiText.PickedUp, NameOf(model.LastPicker.Value)) : "";
        }

        /// <summary>Where a card played by <paramref name="seat"/> starts its flight: the badge, or the hand for the player.</summary>
        private Rect? SeatOrigin(long seat)
        {
            if (seat == _client.Model.MySeat)
            {
                // From the slot the card held in the fan; auto-play and other devices start from the hand.
                var from = _playedFrom ?? HandTarget();
                _playedFrom = null;
                return from;
            }

            return _ring.BadgeFor(seat)?.worldBound;
        }

        private void RefreshHand()
        {
            var model = _client.Model;
            _handCards.Clear();
            var arrived = new List<string>();
            foreach (var card in model.Hand)
            {
                if (card != model.PendingCard)
                {
                    _handCards.Add(card);
                }

                if (!_handBefore.Contains(card))
                {
                    arrived.Add(card);
                }
            }

            _handBefore.Clear();
            foreach (var card in model.Hand)
            {
                _handBefore.Add(card);
            }

            _hand.Show(_handCards, model.IsLegal, model.MyTurn);
            if (arrived.Count > 1 && !_reduceMotion && model.LastPicker == model.MySeat && model.Trick.Count == 0 && arrived.TrueForAll(card => InLastTrick(model, card)))
            {
                // My pick-up: the cards keep their slots but stay hidden until each flies in from the table.
                _hand.Hold(arrived);
            }
            else if (arrived.Count > 1)
            {
                // More than one card at once is a pick-up or a hand-take; a single draw is not lit.
                _hand.MarkNew(arrived);
            }

            if (_autoPlaySingle && _hand.OnlyLegal != null && _hand.OnlyLegal != _singleLegal)
            {
                _singleLegal = _hand.OnlyLegal;
                _singleLegalLeft = SingleLegalSeconds;
            }
            else if (_hand.OnlyLegal == null)
            {
                _singleLegal = null;
            }
        }

        private static bool InLastTrick(TableModel model, string card)
        {
            foreach (var play in model.LastTrick)
            {
                if (play.Card == card)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshTurn()
        {
            var model = _client.Model;
            _shownSeconds = -1;
            if (model.MyTurn)
            {
                if (_turnDeadlineMs != model.DeadlineTs)
                {
                    _turnDeadlineMs = model.DeadlineTs;
                    _turnStartMs = _nowMs();
                    _turnLengthMs = Math.Max(0, _turnDeadlineMs - _turnStartMs);
                    _tickedAt = -1;
                    Buzz(Haptic.TurnStart);
                }
            }
            else
            {
                _me.HideTimer();
            }

            var canTake = model.MyTurn && model.CanTakeHand && model.PendingAsk == null;
            _takeHand.style.display = canTake ? DisplayStyle.Flex : DisplayStyle.None;
            // Ask for a hand takes the whole bar while it is offered, so it never mixes with the chips.
            if (_root.ClassListContains("table--take-hand") != canTake)
            {
                _root.EnableInClassList("table--take-hand", canTake);
                RefreshStrip();
            }
        }

        private bool _resultsShown;

        /// <summary>The results take the screen; a chat or player sheet left open at the last card closes with the round.</summary>
        private void RefreshResults()
        {
            var model = _client.Model;
            var showing = model.LastResult != null && !model.InRound;
            if (showing && !_resultsShown)
            {
                CloseSheets();
                Hulla.Presentation.Navigation.Sheet.Close(_root);
            }

            _resultsShown = showing;
            _results.Show(model, _voted);
        }

        public void ShowReward(string? text) => _results.ShowReward(text);

        public void ShowLevel(long level, float fraction) => _results.ShowLevel(level, fraction);

        /// <summary>Shows Share on the results.</summary>
        public void UseShare(Action share) => _results.Share = share;

        /// <summary>Highlights Share on this round's results for the night's biggest thulla.</summary>
        public void OfferBigThulla(string roundId) => _results.OfferBigThulla(roundId);

        /// <summary>Shows the fair-deal chip on the results (ADR-0009).</summary>
        public void UseFairness(Action<Fairness> explain)
        {
            _results.ShowFairness = explain;
            _explainFairness = explain;
        }

        /// <summary>Lets the host change the rules from the results (design §8).</summary>
        public void UseChangeRules(Action open) => _results.ChangeRules = open;

        /// <summary>Lets the results offer "Add friend" for co-players who aren't friends or pending yet.</summary>
        private Func<string, bool>? _canAddFriend;
        private Action<string>? _addFriend;

        public void SetFriendActions(Func<string, bool> canAdd, Action<string> addFriend)
        {
            _canAddFriend = canAdd;
            _addFriend = addFriend;
            _results.CanAdd = canAdd;
            _results.AddFriend = addFriend;
            RefreshResults();
        }
    }
}
