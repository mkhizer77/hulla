using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Application.Voice;
using Hulla.Games.Shared;
using Hulla.Presentation.Accessibility;
using Hulla.Presentation.Audio;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using Hulla.Presentation.Voice;
using Hulla.Protocol;
using UnityEngine.UIElements;

namespace Hulla.Games.Rung
{
    /// <summary>
    /// The Rung table (Rung design 9) on the shared table kit: me at the bottom, my partner at the top,
    /// the trick in the centre with the pile's count under it, the rung and the two teams' tricks in a
    /// bar on the felt. The call and the court choice open as a panel over the felt for the seat that
    /// must answer. It reads the model after every message and never decides anything itself.
    /// </summary>
    public sealed class RungTableView : CardTableView, IEventPresenter, IRoundResults, IDisposable
    {
        /// <summary>How long a won trick stays face up before the next card may show (seconds).</summary>
        public const float TrickHoldSeconds = 0.8f;

        private readonly PileView _pile;
        private readonly HandView _hand;
        private readonly Label _statusText;
        private readonly Label _statusSecs;
        private readonly Label _note;
        private readonly Label _toast;
        private float _stampLeft;

        /// <summary>How long a stamped word stays on the felt.</summary>
        public const float StampSeconds = 1.1f;
        private readonly VisualElement _bar = new VisualElement { name = "rung-bar" };
        private readonly VisualElement _trumpPlate = new VisualElement { name = "rung-medallion" };
        private readonly Label _trump = new Label { name = "rung-trump" };
        private readonly Label _usName = new Label { name = "rung-us" };
        private readonly Label _usCount = new Label { name = "rung-us-count" };
        private readonly Label _themName = new Label { name = "rung-them" };
        private readonly Label _themCount = new Label { name = "rung-them-count" };
        private readonly VisualElement _pileStack = new VisualElement { name = "rung-pile-cards" };
        private readonly VisualElement _resultStamp = new VisualElement { name = "rung-result-stamp" };
        private readonly Label _tricks = new Label { name = "rung-tricks" };
        private readonly Label _pileCount = new Label { name = "rung-pile" };
        private readonly Label _bazi = new Label { name = "rung-bazi" };
        private readonly VisualElement _ask = new VisualElement { name = "rung-ask" };
        private readonly Label _askTitle = new Label { name = "rung-ask-title" };
        private readonly VisualElement _askButtons = new VisualElement { name = "rung-ask-buttons" };
        private readonly VisualElement _result = new VisualElement { name = "rung-result" };
        private readonly Label _resultTitle = new Label { name = "rung-result-title" };
        private readonly Label _resultLine = new Label { name = "rung-result-line" };
        private readonly Label _resultBazi = new Label { name = "rung-result-bazi" };
        private readonly Button _again = new Button { name = "rung-again" };
        private readonly Button _switch = new Button { name = "rung-switch" };
        private readonly Button _resultLeave = new Button { name = "rung-leave" };
        private readonly VisualElement _resultFriends = new VisualElement { name = "rung-result-friends" };
        private RoundRewards _rewards = null!;
        private Func<string, bool>? _canAdd;
        private Action<string>? _addFriendChip;
        private string _friendsShownFor = "";
        private readonly List<(string Card, string Owner, long Seat)> _plays = new List<(string, string, long)>();
        private readonly List<string> _handCards = new List<string>();
        private string _askShown = "";
        private long _turnStartMs;
        private long _turnDeadlineMs;
        private long _turnLengthMs;
        private long? _timedSeat;
        private long _timedSeatStartMs;
        private float _pulse;
        private bool _voted;
        private string _votedRound = "";
        private bool _reduceMotion;

        public RungTableView(
            VisualElement root,
            TableClient client,
            IChatCatalog chat,
            Func<long> nowMs,
            Action leave,
            SoundPlayer? sounds = null,
            bool reduceMotion = false,
            bool oneTap = false,
            Action? openSettings = null,
            Action<string?>? openRules = null,
            Func<string, UnityEngine.Awaitable<Domain.Result<bool>>>? sendText = null)
            : base(root, client, chat, nowMs, leave, openSettings, openRules, sendText, sounds)
        {
            _reduceMotion = reduceMotion;
            _pile = new PileView(root.Q<VisualElement>("pile")) { ReduceMotion = reduceMotion };
            _hand = new HandView(root.Q<VisualElement>("hand")) { OneTap = oneTap };
            _hand.Played += card => _client.PlayCard(card);
            _statusText = root.Q<Label>("status-text");
            _statusSecs = root.Q<Label>("status-secs");
            _note = root.Q<Label>("pile-note");
            _toast = root.Q<Label>("toast");
            // Thulla's own controls on the shared layout: hand-taking, the discard, the recap, the coach and its results.
            foreach (var name in new[] { "take-hand", "discard", "recap", "coach", "results" })
            {
                var element = root.Q<VisualElement>(name);
                if (element != null)
                {
                    element.style.display = DisplayStyle.None;
                }
            }

            var felt = root.Q<VisualElement>("felt");
            // On the rail, not the felt: the felt clips to its round edge and the board sits on the rim.
            var rail = root.Q<VisualElement>("rail") ?? felt;
            BuildBar(rail);
            rail.RegisterCallback<GeometryChangedEvent>(_ => PlaceBar(rail));
            BuildAsk(felt);
            BuildResult(root, leave);
            _client.Model.Changed += Refresh;
            Refresh();
        }

        /// <summary>The panel's title while it is open; empty otherwise. Read by tests.</summary>
        public string AskTitle => _ask.style.display == DisplayStyle.Flex ? _askTitle.text : "";

        public string ResultTitle => _result.style.display == DisplayStyle.Flex ? _resultTitle.text : "";

        public string StatusText => _statusText.text;

        public string ScoreText => _tricks.text;

        /// <summary>The stamp's text while it shows; empty otherwise. Read by tests.</summary>
        public string StampText => _toast.style.display == DisplayStyle.Flex ? _toast.text : "";

        /// <summary>A word stamped on the felt for a moment (kaat, court), in the skin's stamp style.</summary>
        private void Stamp(string text)
        {
            _toast.text = text;
            _toast.style.display = DisplayStyle.Flex;
            _stampLeft = StampSeconds;
        }

        /// <summary>The bazi so far, from the menu: points, deals and courts by team.</summary>
        protected override void AddMenuItems(Action<string, Action> item) => item(UiText.Score, OpenScore);

        private void OpenScore()
        {
            var model = _client.Model;
            var team = RungModel.TeamOf(model.MySeat ?? 0);
            var teams = model.Rung.Teams;
            var panel = Hulla.Presentation.Navigation.Sheet.Open(_root, RungText.ScoreTitle);
            long Of(System.Collections.Generic.IReadOnlyList<long>? values, int side) => values != null && values.Count >= 2 ? values[side] : 0;
            panel.Add(Hulla.Presentation.Navigation.Sheet.Line(Strings.Format(RungText.Bazi, Of(teams?.Points, team), Of(teams?.Points, 1 - team))));
            panel.Add(Hulla.Presentation.Navigation.Sheet.Line(Strings.Format(RungText.ScoreLine, Of(teams?.DealsWon, team), Of(teams?.DealsWon, 1 - team))));
            Hulla.Presentation.Navigation.Sheet.Answers(panel).Add(Hulla.Presentation.Navigation.Sheet.Button(UiText.Close, () => Hulla.Presentation.Navigation.Sheet.Close(panel), "yes"));
        }

        public void ApplySettings(bool oneTap, bool reduceMotion, bool largeCards, bool haptics = true)
        {
            _haptics = haptics;
            _hand.OneTap = oneTap;
            _hand.SetLargeCards(largeCards);
            _reduceMotion = reduceMotion;
            _pile.ReduceMotion = reduceMotion;
        }

        /// <summary>The model is already current; this only says how long the table holds the picture.</summary>
        public float Present(ServerMessage message)
        {
            switch (message.Payload)
            {
                case RungCardPlayed played:
                    _sounds?.Play(played.Cut ? Sound.Slam : Sound.CardTap, SoundKit.TrickPitch(_client.Model.Rung.Trick.Count - 1));
                    ScreenReader.Say(Speech.Played(NameOf(played.Seat), played.Card, false));
                    if (played.Cut)
                    {
                        // The kaat (design 9.3): a rung on a suit the seat could not follow.
                        Stamp(RungText.Kaat);
                        Buzz(played.Seat == _client.Model.MySeat ? Haptic.ThullaGiven : Haptic.CardPlay);
                        SetContext("good_play");
                    }
                    else if (played.Seat == _client.Model.MySeat)
                    {
                        Buzz(Haptic.CardPlay);
                    }

                    return 0f;
                case RungTrickWon won:
                    ScreenReader.Say(Strings.Format(RungText.WonTrick, NameOf(won.Winner)));
                    return _reduceMotion ? 0.3f : TrickHoldSeconds;
                case RungRoundEnd end:
                    if (end.Kind != RungOutcomeKind.Win)
                    {
                        Stamp(end.Kind == RungOutcomeKind.Court52 ? RungText.Court52 : RungText.CourtStamp);
                        _sounds?.Play(Sound.Stamp);
                    }

                    return 0f;
                case TurnStart turn when turn.Seat == _client.Model.MySeat:
                    Buzz(Haptic.TurnStart);
                    ScreenReader.Say(Speech.YourTurn);
                    return 0f;
                case RungPileTaken taken:
                    _sounds?.Play(Sound.Pickup);
                    Say(Strings.Format(RungText.PileTaken, NameOf(taken.Seat)));
                    return _reduceMotion ? 0.3f : TrickHoldSeconds;
                case RungTrumpCalled called:
                    _sounds?.Play(Sound.Stamp);
                    ScreenReader.Say(Strings.Format(RungText.Called, NameOf(called.Seat), RungText.SuitName(called.Suit.ToString()[0])));
                    return 0.6f;
                case RungRedealt redealt:
                    Say(Strings.Format(RungText.Redealt, NameOf(redealt.Seat)));
                    return 0.6f;
                case RungRoundStart _:
                    _sounds?.Play(Sound.Deal);
                    return 0f;
                default:
                    return 0f;
            }
        }

        public void Tick(float deltaSeconds)
        {
            TickShared(deltaSeconds);
            if (_stampLeft > 0f)
            {
                _stampLeft -= deltaSeconds;
                if (_stampLeft <= 0f)
                {
                    _toast.style.display = DisplayStyle.None;
                }
            }

            _pulse += deltaSeconds;
            var model = _client.Model;
            if (model.MyTurn)
            {
                var left = Math.Max(0, _turnDeadlineMs - _nowMs());
                var total = Math.Max(1, _turnDeadlineMs - _turnStartMs);
                var seconds = left / 1000f;
                _me.ShowTimer((float)left / total, seconds, TimerPhase.Pulse(seconds, _pulse, _reduceMotion));
                var whole = (left + 999) / 1000;
                _statusSecs.text = Strings.Format(UiText.SecondsShort, whole);
                _statusSecs.style.display = whole <= 10 ? DisplayStyle.Flex : DisplayStyle.None;
                return;
            }

            _statusSecs.style.display = DisplayStyle.None;
            if (!model.InRound || model.TurnSeat == null || _turnLengthMs <= 0)
            {
                return;
            }

            // Another seat's deadline is its own; its ring drains over the length of my last turn.
            var theirs = Math.Max(0, _turnLengthMs - (_nowMs() - _timedSeatStartMs));
            _ring.BadgeFor(model.TurnSeat.Value)?.ShowTimer((float)theirs / _turnLengthMs, theirs / 1000f, TimerPhase.Pulse(theirs / 1000f, _pulse, _reduceMotion));
        }

        public void Dispose()
        {
            _client.Model.Changed -= Refresh;
            DisposeShared();
        }

        private void BuildBar(VisualElement felt)
        {
            // The scoreboard (two-games concept): my pair's plate, the rung in a medallion, the other pair's plate;
            // under it the pile with its count and the bazi score.
            _bar.AddToClassList("rung-bar");
            _bar.pickingMode = PickingMode.Ignore;
            var board = new VisualElement { name = "rung-board" };
            board.AddToClassList("rung-bar__board");
            _trumpPlate.AddToClassList("rung-bar__medallion");
            _trump.AddToClassList("rung-bar__trump");
            _trumpPlate.Add(_trump);
            _tricks.AddToClassList("rung-bar__tricks");
            board.Add(TeamPlate("rung-bar__team--us", _usName, _usCount));
            board.Add(TeamPlate("rung-bar__team--them", _themName, _themCount));
            board.Add(_trumpPlate);
            board.Add(_tricks);
            var under = new VisualElement { name = "rung-under" };
            under.AddToClassList("rung-bar__under");
            _pileStack.AddToClassList("rung-bar__pile-cards");
            _pileCount.AddToClassList("rung-bar__pile");
            _pileStack.Add(_pileCount);
            _bazi.AddToClassList("rung-bar__bazi");
            under.Add(_pileStack);
            under.Add(_bazi);
            _bar.Add(board);
            _bar.Add(under);
            felt.Add(_bar);
        }

        /// <summary>How far above the table's bottom edge the board starts, in canvas pixels.</summary>
        public const float BoardLift = 96f;

        /// <summary>
        /// The board straddles the bottom edge of the table, on my side: the partner's seat covers the top
        /// edge on a phone. The table is a square centred in the rail, as wide as the rail's shorter side.
        /// </summary>
        private void PlaceBar(VisualElement rail)
        {
            var width = rail.resolvedStyle.width;
            var height = rail.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || height <= 0f)
            {
                return;
            }

            var side = Math.Min(width, height);
            _bar.style.top = (height / 2f) + (side / 2f) - BoardLift;
        }

        private static VisualElement TeamPlate(string side, Label name, Label count)
        {
            var plate = new VisualElement();
            plate.AddToClassList("rung-bar__team");
            plate.AddToClassList(side);
            name.AddToClassList("rung-bar__team-name");
            count.AddToClassList("rung-bar__team-count");
            plate.Add(name);
            plate.Add(count);
            return plate;
        }

        private void BuildAsk(VisualElement felt)
        {
            _ask.AddToClassList("rung-ask");
            _ask.AddToClassList("panel");
            _askTitle.AddToClassList("rung-ask__title");
            _askButtons.AddToClassList("rung-ask__buttons");
            _ask.Add(_askTitle);
            _ask.Add(_askButtons);
            _ask.style.display = DisplayStyle.None;
            felt.Add(_ask);
        }

        private void BuildResult(VisualElement root, Action leave)
        {
            _result.AddToClassList("rung-result");
            var panel = new VisualElement();
            panel.AddToClassList("panel");
            panel.AddToClassList("rung-result__panel");
            _resultTitle.AddToClassList("title");
            _resultTitle.AddToClassList("title--small");
            _resultLine.AddToClassList("turn");
            _resultBazi.AddToClassList("rung-result__bazi");
            foreach (var button in new[] { _again, _switch, _resultLeave })
            {
                button.AddToClassList("button");
            }

            _again.AddToClassList("button--primary");
            _switch.AddToClassList("button--quiet");
            _switch.AddToClassList("button--small");
            _resultLeave.AddToClassList("button--quiet");
            _resultLeave.AddToClassList("button--small");
            _again.clicked += VoteNext;
            _switch.text = Strings.Format(RungText.SwitchTo, UiText.GameThulla);
            _switch.clicked += () => _client.ChangeGame(GameId.Thulla);
            _resultLeave.text = UiText.Leave;
            _resultLeave.clicked += leave;
            _resultStamp.AddToClassList("rung-result__stamp");
            _resultStamp.pickingMode = PickingMode.Ignore;
            panel.Add(_resultStamp);
            panel.Add(_resultTitle);
            panel.Add(_resultLine);
            panel.Add(_resultBazi);
            // What the deal paid, the 2x offer, the level bar and Add chips: the same rows as Thulla's result.
            _rewards = RoundRewards.AddTo(panel, "rung-");
            _resultFriends.AddToClassList("rung-result__friends");
            panel.Add(_resultFriends);
            panel.Add(_again);
            panel.Add(_switch);
            panel.Add(_resultLeave);
            _result.Add(panel);
            _result.style.display = DisplayStyle.None;
            root.Add(_result);
        }

        private void Refresh()
        {
            var model = _client.Model;
            var rung = model.Rung;
            _info.text = (model.IsPrivate && model.Code.Length > 0 ? model.Code : UiText.QuickPlayTable) + (model.FairDealCommitted ? " ✓" : "");
            var watchers = model.Spectators.Count;
            _watchIcon.style.display = watchers > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _watchCount.style.display = watchers > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _watchCount.text = Strings.Format("{0}", watchers);
            RefreshSeats(model, rung);
            RefreshBar(model, rung);
            RefreshTrick(rung);
            RefreshHand(model, rung);
            RefreshTurn(model, rung);
            RefreshAsk(model, rung);
            RefreshResult(model, rung);
            ShowVoice();
        }

        private void RefreshSeats(TableModel model, RungModel rung)
        {
            var mySeat = model.MySeat ?? 0;
            _ring.Spectating = model.Spectating;
            _ring.Layout(RungModel.Seats, mySeat);
            var shown = model.Spectating ? RungModel.Seats : RungModel.Seats - 1;
            for (var i = 0; i < shown && i < _ring.Badges.Count; i++)
            {
                var seat = _ring.SeatAt(i);
                var info = seat < model.Seats.Count ? model.Seats[(int)seat] : default;
                var partner = !model.Spectating && seat == RungModel.PartnerOf(mySeat);
                var state = info.Bot ? UiText.BotTag : info.Occupied && !info.Connected ? UiText.SeatAwayAutoPlay : info.AutoPlay ? UiText.SeatAutoPlay : partner ? RungText.Partner : "";
                // The crown marks the seat whose run is alive: one more trick takes the pile (RG17).
                _ring.Badges[i].Show(NameOf(seat), rung.HandCount((int)seat), model.InRound && model.TurnSeat == seat, state, model.InRound && rung.RunSeat == seat, null, info.Avatar);
                _ring.Badges[i].EnableInClassList("seat--partner", partner);
                _ring.Badges[i].ShowBack(info.CardBack);
            }

            if (model.MySeat != null)
            {
                var me = model.Seats.Count > mySeat ? model.Seats[(int)mySeat] : default;
                _me.Show(NameOf(mySeat), me.Avatar, true, model.MyTurn, model.InRound && rung.RunSeat == mySeat);
                if (!model.MyTurn)
                {
                    _me.HideTimer();
                }
            }
        }

        private void RefreshBar(TableModel model, RungModel rung)
        {
            var team = RungModel.TeamOf(model.MySeat ?? 0);
            _trump.text = rung.Trump == null ? "" : CardElement.SuitGlyph(rung.Trump.Value);
            _trump.EnableInClassList("rung-bar__trump--red", rung.Trump == 'H' || rung.Trump == 'D');
            _trump.style.display = rung.Trump == null ? DisplayStyle.None : DisplayStyle.Flex;
            // The whole line stays for the screen reader and other skins; the plates carry it in Basant.
            _tricks.text = Strings.Format(RungText.TricksLine, rung.Counts[team], rung.Counts[1 - team]);
            _usName.text = RungText.Us;
            _themName.text = RungText.Them;
            _usCount.text = Strings.Format("{0}", rung.Counts[team]);
            _themCount.text = Strings.Format("{0}", rung.Counts[1 - team]);
            _pileCount.text = rung.Pile > 0 ? Strings.Format("{0}", rung.Pile) : "";
            _pileCount.style.display = rung.Pile > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _pileStack.style.display = rung.Pile > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var points = rung.Teams?.Points;
            var has = points != null && points.Count >= 2;
            _bazi.text = has ? Strings.Format(RungText.Bazi, points![team], points[1 - team]) : "";
            _bazi.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshTrick(RungModel rung)
        {
            var showingLast = rung.Trick.Count == 0 && rung.LastTrick.Count > 0;
            var source = showingLast ? rung.LastTrick : rung.Trick;
            _plays.Clear();
            foreach (var play in source)
            {
                _plays.Add((play.Card, NameOf(play.Seat), play.Seat));
            }

            _pile.Show(_plays, showingLast, SeatOrigin);
        }

        private UnityEngine.Rect? SeatOrigin(long seat) =>
            seat == _client.Model.MySeat ? _me.worldBound : _ring.BadgeFor(seat)?.worldBound;

        private void RefreshHand(TableModel model, RungModel rung)
        {
            _handCards.Clear();
            foreach (var card in rung.Hand)
            {
                if (card != rung.PendingCard)
                {
                    _handCards.Add(card);
                }
            }

            _hand.Show(_handCards, rung.IsLegal, model.MyTurn && rung.Phase == RungPhase.Play);
        }

        private void RefreshTurn(TableModel model, RungModel rung)
        {
            if (model.TurnSeat != _timedSeat)
            {
                _timedSeat = model.TurnSeat;
                _timedSeatStartMs = _nowMs();
                foreach (var badge in _ring.Badges)
                {
                    badge.HideTimer();
                }
            }

            if (model.MyTurn && model.DeadlineTs != _turnDeadlineMs)
            {
                _turnDeadlineMs = model.DeadlineTs;
                _turnStartMs = _nowMs();
                _turnLengthMs = Math.Max(1000, _turnDeadlineMs - _turnStartMs);
            }

            if (!model.InRound)
            {
                _statusText.text = "";
            }
            else if (model.MyTurn)
            {
                _statusText.text = RungText.YourTurn;
            }
            else if (model.TurnSeat != null)
            {
                _statusText.text = Strings.Format(RungText.TurnOf, NameOf(model.TurnSeat.Value));
            }

            var note = rung.Chasing != null ? RungText.Chasing : rung.RunSeat != null && rung.Pile > 0 && model.InRound ? RungText.OneMore : "";
            _note.text = note;
        }

        /// <summary>A line under the trick for a moment (a pile taken, a redeal).</summary>
        private void Say(string text)
        {
            _note.text = text;
            ScreenReader.Say(text);
        }

        private void RefreshAsk(TableModel model, RungModel rung)
        {
            var mine = model.MyTurn;
            var key = !model.InRound ? "" : rung.Phase == RungPhase.Call ? (mine ? "call:" + rung.CallableSuits.Count + rung.CanRedeal : "calling:" + rung.CallerSeat) : rung.Phase == RungPhase.CourtChoice && mine ? "court" : "";
            // The rung is named but the rest of the deal has not arrived: nothing to ask.
            if (rung.Phase == RungPhase.Call && rung.Trump != null)
            {
                key = "";
            }

            if (key == _askShown)
            {
                return;
            }

            _askShown = key;
            _askButtons.Clear();
            // The panel covers the middle of the felt, so the bar steps aside while it is open.
            _bar.style.display = key.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (key.Length == 0)
            {
                _ask.style.display = DisplayStyle.None;
                return;
            }

            _ask.style.display = DisplayStyle.Flex;
            if (key.StartsWith("calling:", StringComparison.Ordinal))
            {
                _askTitle.text = Strings.Format(RungText.Choosing, NameOf(rung.CallerSeat));
                return;
            }

            if (key == "court")
            {
                _askTitle.text = RungText.CourtTitle;
                AddAskButton(RungText.CourtStop, "court-stop", false, () => _client.CourtChoice(false));
                AddAskButton(RungText.CourtPlayOn, "court-play-on", true, () => _client.CourtChoice(true));
                return;
            }

            _askTitle.text = RungText.Choose;
            foreach (var suit in rung.CallableSuits)
            {
                var named = suit;
                var button = AddAskButton(CardElement.SuitGlyph(suit), "call-" + suit, false, () => _client.CallTrump(named));
                button.AddToClassList("rung-ask__suit");
                button.EnableInClassList("rung-ask__suit--red", suit == 'H' || suit == 'D');
            }

            if (rung.CanRedeal)
            {
                AddAskButton(RungText.Redeal, "call-redeal", false, () => _client.Redeal()).AddToClassList("button--quiet");
            }
        }

        private Button AddAskButton(string text, string name, bool primary, Action onTap)
        {
            var button = new Button { text = text, name = name };
            button.AddToClassList("button");
            button.AddToClassList("button--small");
            if (primary)
            {
                button.AddToClassList("button--primary");
            }

            button.clicked += () =>
            {
                _sounds?.Play(Sound.Ui);
                onTap();
            };
            _askButtons.Add(button);
            return button;
        }

        private void RefreshResult(TableModel model, RungModel rung)
        {
            var result = rung.LastResult;
            if (result == null || model.InRound)
            {
                _result.style.display = DisplayStyle.None;
                return;
            }

            if (_votedRound != result.RoundId)
            {
                _votedRound = result.RoundId;
                _voted = false;
                _rewards.Clear();
            }

            ShowFriendChips(model, result.RoundId);

            _result.style.display = DisplayStyle.Flex;
            var team = RungModel.TeamOf(model.MySeat ?? 0);
            var won = result.WinnerTeam == team;
            var finished = rung.BaziFinished;
            if (model.Spectating)
            {
                _resultTitle.text = Strings.Format(RungText.DealSeen, NameOf(result.WinnerTeam), NameOf(result.WinnerTeam + 2));
            }
            else if (finished)
            {
                _resultTitle.text = won ? RungText.BaziWon : RungText.BaziLost;
            }
            else
            {
                _resultTitle.text = result.Kind == RungOutcomeKind.Court52 ? RungText.Court52 : result.Kind == RungOutcomeKind.Court ? RungText.Court : won ? RungText.DealWon : RungText.DealLost;
            }

            var court = !finished && !model.Spectating && result.Kind != RungOutcomeKind.Win;
            _resultStamp.style.display = court ? DisplayStyle.Flex : DisplayStyle.None;
            _resultStamp.EnableInClassList("rung-result__stamp--52", result.Kind == RungOutcomeKind.Court52);
            _resultLine.text = Strings.Format(RungText.TricksLine, result.Counts[team], result.Counts[1 - team]);
            var points = rung.Teams?.Points;
            _resultBazi.text = points != null && points.Count >= 2 ? Strings.Format(RungText.Bazi, points[team], points[1 - team]) : "";
            var votes = model.RematchVotes.Count;
            _again.text = _voted || votes > 0
                ? Strings.Format(RungText.NextDealVotes, votes, RungModel.Seats)
                : finished ? RungText.NewBazi : RungText.NextDeal;
            _again.SetEnabled(!_voted && !model.Spectating);
            _switch.style.display = model.IsHost && model.IsPrivate ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The deal's XP and coins (docs/design/two-games.md), once the server has paid it.</summary>
        public void ShowReward(string? text) => _rewards.ShowReward(text);

        public void ShowLevel(long level, float fraction) => _rewards.ShowLevel(level, fraction);

        public void OfferDouble(string? text, Action? onTap) => _rewards.OfferDouble(text, onTap);

        /// <summary>The reward line on the deal result; empty until the deal is paid. Read by tests.</summary>
        public string RewardText => _rewards.RewardText;

        public void SetFriendActions(Func<string, bool> canAdd, Action<string> addFriend)
        {
            UseFriendActions(canAdd, addFriend);
            _canAdd = canAdd;
            _addFriendChip = addFriend;
            _friendsShownFor = "";
            Refresh();
        }

        /// <summary>The Add chips are drawn once a deal, so a chip that says "Request sent" keeps saying it.</summary>
        private void ShowFriendChips(TableModel model, string roundId)
        {
            if (_friendsShownFor == roundId || _canAdd == null)
            {
                return;
            }

            _friendsShownFor = roundId;
            _resultFriends.Clear();
            var row = model.Spectating ? null : RoundRewards.FriendChips(model, _canAdd, _addFriendChip);
            if (row != null)
            {
                _resultFriends.Add(row);
            }
        }

        private void VoteNext()
        {
            if (_voted)
            {
                return;
            }

            _voted = true;
            _client.VoteRematch(true);
            Refresh();
        }
    }
}
