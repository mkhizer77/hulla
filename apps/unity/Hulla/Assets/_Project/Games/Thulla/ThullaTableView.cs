using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
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
        private const float DiscardStartSeconds = 0.3f;

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
        private readonly PileView _pile;
        private readonly DiscardView _discard;
        private readonly Label _pileNote;
        private readonly HandView _hand;
        private readonly Label _hint;
        private readonly Label _info;
        private readonly Button _takeHand;
        private readonly Label _toast;
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
        private readonly Func<string, Awaitable<Domain.Result<bool>>>? _sendText;
        private VisualElement _chatLog = null!;
        private TextField _chatInput = null!;
        private Label _chatStatus = null!;

        /// <summary>The chat log keeps the latest lines only.</summary>
        private const int ChatLogLines = 30;

        /// <param name="oneTap">Play on the first tap instead of tap-to-raise (a setting, GDD 14.4).</param>
        /// <param name="hideRecapForGood">Remembers "don't show again"; null when the player already chose it.</param>
        /// <param name="sounds">The table's sounds; null for silence (tests).</param>
        /// <param name="autoPlaySingle">A lone legal card plays itself after a beat (R44); off in the tutorial.</param>
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
            _results = new ThullaResults(root.Q<VisualElement>("results"), NameOf, AvatarOf, VoteAgain);
            var settingsButton = root.Q<Button>("settings");
            settingsButton.text = UiText.Settings;
            settingsButton.clicked += () => openSettings?.Invoke();
            settingsButton.style.display = openSettings == null ? DisplayStyle.None : DisplayStyle.Flex;
            var rulesButton = root.Q<Button>("rules");
            rulesButton.text = RulebookText.Title;
            rulesButton.clicked += () => openRules?.Invoke(null);
            rulesButton.style.display = openRules == null ? DisplayStyle.None : DisplayStyle.Flex;
            var leaveButton = root.Q<Button>("leave");
            leaveButton.text = UiText.Leave;
            leaveButton.clicked += leave;
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
            chatButton.text = UiText.Chat;
            chatButton.clicked += () => SetChatOpen(_chatPanel.style.display != DisplayStyle.Flex);
            var closeChat = root.Q<Button>("chat-close");
            closeChat.text = UiText.Close;
            closeChat.clicked += () => SetChatOpen(false);
            BuildChatPanel(root.Q<VisualElement>("chat-phrases"), root.Q<VisualElement>("chat-emotes"));
            RefreshStrip();
            _chatLog = root.Q<VisualElement>("chat-log");
            _chatStatus = root.Q<Label>("chat-status");
            _chatInput = root.Q<TextField>("chat-input");
            _chatInput.maxLength = ITableChat.MaxLength;
            _chatInput.label = "";
            var send = root.Q<Button>("chat-send");
            send.text = UiText.Send;
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
            if (id == null)
            {
                return;
            }

            var name = NameOf(seat);
            var muted = _isMuted?.Invoke(id) == true;
            _sheetTitle.text = name;
            _sheetStatus.text = "";
            _sheetActions.Clear();
            var tray = model.CardMemoryAssist ? model.PickedUp((int)seat) : Array.Empty<string>();
            if (tray.Count > 0)
            {
                _sheetStatus.text = string.Format(UiText.PickedUpTray, name, string.Join(" ", TrayGlyphs(tray)));
            }

            SheetButton(muted ? UiText.Unmute : UiText.Mute, () =>
            {
                _setMuted(id, !muted);
                _sheetStatus.text = muted ? "" : string.Format(UiText.MutedNote, name);
                CloseSheetSoon();
            });
            if (_block != null)
            {
                SheetButton(UiText.Block, () =>
                {
                    _block(id);
                    _sheetStatus.text = string.Format(UiText.Blocked, name);
                    CloseSheetSoon();
                });
            }

            if (_report != null)
            {
                SheetButton(UiText.Report, () => ShowReasons(id, name));
            }

            SheetButton(UiText.Close, () => _sheet.style.display = DisplayStyle.None);
            _sheet.style.display = DisplayStyle.Flex;
        }

        private static IEnumerable<string> TrayGlyphs(IReadOnlyList<string> cards)
        {
            foreach (var card in cards)
            {
                yield return (card[0] == 'T' ? "10" : card.Substring(0, 1)) + CardElement.SuitGlyph(card[1]);
            }
        }

        private void ShowReasons(string id, string name)
        {
            _sheetTitle.text = string.Format(UiText.ReportWhy, name);
            _sheetActions.Clear();
            foreach (var (reason, label) in UiText.ReportReasons)
            {
                var chosen = reason;
                SheetButton(label, () => _report?.Invoke(id, chosen, ok =>
                {
                    _sheetActions.Clear();
                    _sheetStatus.text = ok ? UiText.ReportThanks : UiText.Error("social.unavailable");
                    CloseSheetSoon();
                }));
            }

            SheetButton(UiText.Close, () => _sheet.style.display = DisplayStyle.None);
        }

        private void SheetButton(string text, Action click)
        {
            var button = new Button(click) { text = text };
            button.AddToClassList("chip");
            _sheetActions.Add(button);
        }

        private void CloseSheetSoon() => _sheet.schedule.Execute(() => _sheet.style.display = DisplayStyle.None).StartingIn(2500);

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
                    return 0.9f;
                case RoundStart _:
                    _voted = false;
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
                ShowCoach(string.Format(TutorialText.ExplainAsk, name, cards), string.Format(UiText.AskNamed, name), () =>
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
                _hint.text = string.Format(UiText.AskedWaiting, NameOf(asked.Target));
                _hint.style.display = DisplayStyle.Flex;
                _hintLeft = SaySeconds;
                return;
            }

            _ring.BadgeFor(asked.Asker)?.Say(string.Format(UiText.AsksForHand, NameOf(asked.Target)), SaySeconds);
        }

        private void PresentAnswer(HandAnswered answered)
        {
            var model = _client.Model;
            if (_askSheetOpen)
            {
                _askSheetOpen = false;
                _sheet.style.display = DisplayStyle.None;
            }

            if (answered.Accepted || answered.Reason != HandAnsweredPayloadReason.Declined)
            {
                return;
            }

            if (answered.Asker == model.MySeat)
            {
                _hint.text = string.Format(UiText.KeepsCards, NameOf(answered.Target));
                _hint.style.display = DisplayStyle.Flex;
                _hintLeft = SaySeconds;
                return;
            }

            _ring.BadgeFor(answered.Target)?.Say(UiText.KeepPlaying, SaySeconds);
        }

        /// <summary>The asked player decides (R33, R34): hand everything over and sit out safe, or keep playing.</summary>
        private void ShowAskSheet(HandAsked asked)
        {
            _sheetTitle.text = string.Format(UiText.AskSheetTitle, NameOf(asked.Asker));
            _sheetStatus.text = string.Format(UiText.AskSheetBody, asked.CardCount);
            _sheetActions.Clear();
            SheetButton(UiText.HandOver, () => AnswerAsk(true));
            SheetButton(UiText.KeepPlaying, () => AnswerAsk(false));
            _askSheetOpen = true;
            _sheet.style.display = DisplayStyle.Flex;
        }

        private void AnswerAsk(bool accept)
        {
            _askSheetOpen = false;
            _sheet.style.display = DisplayStyle.None;
            _client.AnswerHand(accept);
        }

        /// <summary>Per frame: bubbles, the recap, the stamp, the pick-up flight, the strip and the turn timers.</summary>
        public void Tick(float deltaSeconds)
        {
            foreach (var badge in _ring.Badges)
            {
                badge.Tick(deltaSeconds);
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
            _hand.SetLargeCards(largeCards);
            _reduceMotion = reduceMotion;
            _pile.ReduceMotion = reduceMotion;
            _haptics = haptics;
            _hideRecapForGood = hideRecapForGood;
        }

        public void Dispose()
        {
            _client.Model.Changed -= Refresh;
            _client.Model.Chat -= OnChat;
        }

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
                    text = string.Format(UiText.MustFollow, CardElement.SuitGlyph(model.LedSuit ?? 'S'));
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
                var button = new Button(() => Say(() => _client.SendEmote(id))) { text = UiText.EmoteLabel(id) };
                button.AddToClassList("chip");
                button.AddToClassList("chip--emote");
                emotes.Add(button);
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

            _strip.style.display = DisplayStyle.Flex;
            foreach (var phrase in picks)
            {
                var id = phrase.Id;
                var chip = new Button(() => _client.SendQuickChat(id)) { text = phrase.Text, tooltip = phrase.Translation };
                chip.AddToClassList("chip");
                chip.AddToClassList("quick-strip__chip");
                _strip.Add(chip);
            }

            var more = new Button(() => SetChatOpen(true)) { text = UiText.More };
            more.AddToClassList("chip");
            more.AddToClassList("quick-strip__more");
            _strip.Add(more);
        }

        private void Say(Action send)
        {
            send();
            SetChatOpen(false);
        }

        private void SetChatOpen(bool open) => _chatPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

        private void OnChat(ChatLine line)
        {
            if (Muted(line.Seat))
            {
                return;
            }

            var text = line.PhraseId != null
                ? _chat.TextFor(line.PhraseId) ?? line.PhraseId
                : UiText.EmoteLabel(line.EmoteId ?? "") + (line.TargetSeat == null ? "" : " → " + NameOf(line.TargetSeat.Value));
            if (line.Seat == _client.Model.MySeat)
            {
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

        /// <summary>Your equipped table surface colours the felt (GDD 12.11) until its art lands.</summary>
        private void RefreshSurface()
        {
            var model = _client.Model;
            var mine = model.MySeat is long seat && seat < model.Seats.Count ? model.Seats[(int)seat].TableSurface : null;
            var colour = CosmeticLook.TableColour(mine);
            _felt.style.backgroundColor = colour is UnityEngine.Color c ? new StyleColor(c) : new StyleColor(StyleKeyword.Null);
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
                return;
            }

            var info = model.Seats[(int)mine];
            _me.style.display = DisplayStyle.Flex;
            var escaped = model.IsEscaped((int)mine);
            // Out of cards: the seat stays for watching, chat and voice (R25), and says so.
            var state = model.InRound && escaped ? UiText.OutWatching : "";
            _me.Show(info.DisplayName ?? UiText.You, info.Avatar, true, model.InRound && model.TurnSeat == mine, model.InRound && model.LeaderSeat == mine && !escaped, state);
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
            var text = model.IsPrivate && model.Code.Length > 0 ? model.Code : UiText.QuickPlayTable;
            if (model.Spectating)
            {
                text = UiText.Watching + " · " + text;
            }
            if (model.InRound && model.FairDealCommitted)
            {
                text += " · " + UiText.FairDeal;
            }

            _info.text = text;
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
                var state = model.IsEscaped((int)seat)
                    ? UiText.Escaped
                    : info.Occupied && !info.Connected
                        ? UiText.SeatAwayAutoPlay
                    : info.AutoPlay
                        ? UiText.SeatAutoPlay
                        : "";
                var leader = model.InRound && model.LeaderSeat == seat && !model.IsEscaped((int)seat);
                var voids = model.InRound && model.CardMemoryAssist ? model.KnownVoids((int)seat) : null;
                _ring.Badges[i].Show(NameOf(seat), model.HandCount((int)seat), model.TurnSeat == seat && model.InRound, state, leader, voids, info.Avatar);
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
            _pileNote.text = showingLast && model.LastPicker != null ? string.Format(UiText.PickedUp, NameOf(model.LastPicker.Value)) : "";
        }

        /// <summary>Where a card played by <paramref name="seat"/> starts its flight: the badge, or the hand for the player.</summary>
        private Rect? SeatOrigin(long seat)
        {
            if (seat == _client.Model.MySeat)
            {
                return HandTarget();
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

            _takeHand.style.display = model.MyTurn && model.CanTakeHand && model.PendingAsk == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshResults() => _results.Show(_client.Model, _voted);

        public void ShowReward(string? text) => _results.ShowReward(text);

        /// <summary>Shows Share on the results.</summary>
        public void UseShare(Action share) => _results.Share = share;

        /// <summary>Shows the fair-deal chip on the results (ADR-0009).</summary>
        public void UseFairness(Action<Fairness> explain) => _results.ShowFairness = explain;

        /// <summary>Lets the host change the rules from the results (design §8).</summary>
        public void UseChangeRules(Action open) => _results.ChangeRules = open;

        /// <summary>Lets the results offer "Add friend" for co-players who aren't friends or pending yet.</summary>
        public void SetFriendActions(Func<string, bool> canAdd, Action<string> addFriend)
        {
            _results.CanAdd = canAdd;
            _results.AddFriend = addFriend;
            RefreshResults();
        }
    }
}
