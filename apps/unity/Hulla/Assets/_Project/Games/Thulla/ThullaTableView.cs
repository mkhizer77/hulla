using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Application.Voice;
using Hulla.Games.Shared;
using Hulla.Presentation.Voice;
using Hulla.Presentation.Audio;
using Hulla.Presentation.Accessibility;
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
    public sealed class ThullaTableView : CardTableView, IEventPresenter, IDisposable
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

        private readonly Label _statusText;
        private readonly Label _statusSecs;
        private readonly VisualElement _status;
        private Action<Fairness>? _explainFairness;
        private readonly Dictionary<string, int> _bhabhiTally = new Dictionary<string, int>();
        private readonly HashSet<string> _talliedRounds = new HashSet<string>();
        private int _roundsPlayed;
        private readonly PileView _pile;
        private readonly DiscardView _discard;
        private readonly Label _pileNote;
        private readonly HandView _hand;
        private readonly Label _hint;
        private readonly Button _takeHand;
        private readonly Label _toast;
        private readonly VisualElement _burst;
        private readonly VisualElement _rail;
        private readonly ThullaResults _results;
        private readonly VisualElement _handArea;
        private readonly StampTimeline _stamp = new StampTimeline();
        private readonly VisualElement _felt;
        private readonly bool _autoPlaySingle;
        private bool _reduceMotion;
        private bool _askSheetOpen;
        private readonly VisualElement _recap;
        private Action? _hideRecapForGood;
        private readonly List<(string Card, string Owner, long Seat)> _plays = new List<(string Card, string Owner, long Seat)>();
        private readonly List<string> _handCards = new List<string>();
        private readonly HashSet<string> _handBefore = new HashSet<string>();
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
        private bool _voted;
        private VisualElement _coach = null!;
        private Label _coachText = null!;
        private Button _coachButton = null!;
        private Action? _coachAction;
        private ThullaHints? _hints;
        private bool _thullaJustPlayed;
        private Rect? _playedFrom;

        /// <summary>How long the line beside the microphone prompt stays.</summary>

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
            : base(root, client, chat, nowMs, leave, openSettings, openRules, sendText, sounds)
        {
            _autoPlaySingle = autoPlaySingle;
            _reduceMotion = reduceMotion;
            _hideRecapForGood = hideRecapForGood;
            _felt = root.Q<VisualElement>("felt");
            _rail = root.Q<VisualElement>("rail");
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
            _statusText = root.Q<Label>("status-text");
            _statusSecs = root.Q<Label>("status-secs");
            _status = root.Q<VisualElement>("status");
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
            _coach = root.Q<VisualElement>("coach");
            _coachText = root.Q<Label>("coach-text");
            _coachButton = root.Q<Button>("coach-next");
            _coachButton.clicked += () => _coachAction?.Invoke();
            _client.Model.Changed += Refresh;
            _escapes = _client.Model.EscapeOrder.Count;
            foreach (var card in _client.Model.Hand)
            {
                _handBefore.Add(card);
            }

            Refresh();
        }

        /// <summary>Raised when the player taps a card that can't be played (the tutorial counts these).</summary>
        public event Action<string>? CardRefused;

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

        /// <summary>The cards a seat picked up this round, on its player sheet (GDD 14.7).</summary>
        protected override void AddPlayerFacts(VisualElement sheet, long seat)
        {
            var model = _client.Model;
            var picked = model.PickedUp((int)seat);
            var strip = Line(Strings.Get("ui.picked_up_count", picked.Count), "psheet__strip");
            if (model.CardMemoryAssist && picked.Count > 0)
            {
                strip.text += "\n" + string.Join(" ", TrayGlyphs(picked));
            }

            sheet.Add(strip);
        }

        /// <summary>The request for my hand stays until it is answered (R33).</summary>
        protected override bool SheetsLocked => _askSheetOpen;

        /// <summary>An escaped player may leave without a question: they are safe for the round.</summary>
        protected override bool StillPlaying(long seat) => !_client.Model.IsEscaped((int)seat);

        protected override void AddMenuItems(Action<string, Action> item) => item(UiText.Score, OpenScore);

        /// <summary>After the round the seed is out, so the whole proof is shown (ADR-0009).</summary>
        protected override bool ExplainFinishedDeal()
        {
            var fairness = _client.Model.LastResult?.Fairness;
            if (fairness == null || _explainFairness == null || _client.Model.InRound)
            {
                return false;
            }

            _explainFairness(fairness);
            return true;
        }

        private static IEnumerable<string> TrayGlyphs(IReadOnlyList<string> cards)
        {
            foreach (var card in cards)
            {
                yield return (card[0] == 'T' ? "10" : card.Substring(0, 1)) + CardElement.SuitGlyph(card[1]);
            }
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
                    Speak(applied, true);
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
                    Speak(applied, false);
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
                    ScreenReader.Say(Speech.PickedUp(NameOf(picker), resolved.Cards.Count));
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
                    ScreenReader.Say(Speech.Bhabhi(NameOf(end.BhabhiSeat)));
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

            if (_micControl is { Stale: true })
            {
                ShowVoice();
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
            DisposeShared();
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
            if (_sounds != null)
            {
                _sounds.Set = me.SoundSet;
            }
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
        /// <summary>Speaks a played card for the screen reader (GDD 27: "Sara played King of Hearts").</summary>
        private void Speak(MoveApplied applied, bool thulla)
        {
            if (applied.Move?.Card != null)
            {
                ScreenReader.Say(Speech.Played(NameOf(applied.Seat), applied.Move.Card, thulla));
            }
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
                ScreenReader.Say(Speech.Escaped(NameOf(model.EscapeOrder[i])));
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
                    ScreenReader.Say(Speech.YourTurn);
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

        public void OfferDouble(string? text, Action? onTap) => _results.OfferDouble(text, onTap);

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

        public void SetFriendActions(Func<string, bool> canAdd, Action<string> addFriend)
        {
            UseFriendActions(canAdd, addFriend);
            _results.CanAdd = canAdd;
            _results.AddFriend = addFriend;
            RefreshResults();
        }
    }
}
