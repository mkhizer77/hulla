using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Application.Voice;
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
    public sealed class RungTableView : IEventPresenter, IDisposable
    {
        /// <summary>How long a won trick stays face up before the next card may show (seconds).</summary>
        public const float TrickHoldSeconds = 0.8f;

        private readonly TableClient _client;
        private readonly Func<long> _nowMs;
        private readonly SoundPlayer? _sounds;
        private readonly VisualElement _root;
        private readonly OpponentRing _ring;
        private readonly PlayerBadge _me = new PlayerBadge();
        private readonly PileView _pile;
        private readonly HandView _hand;
        private readonly Label _info;
        private readonly Label _statusText;
        private readonly Label _statusSecs;
        private readonly Label _note;
        private readonly Button _mic;
        private readonly Label _voicePill;
        private readonly Button _speaker;
        private readonly VisualElement _bar = new VisualElement { name = "rung-bar" };
        private readonly Label _trump = new Label { name = "rung-trump" };
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
        private readonly List<(string Card, string Owner, long Seat)> _plays = new List<(string, string, long)>();
        private readonly List<string> _handCards = new List<string>();
        private VoiceController? _voice;
        private MicControl? _micControl;
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

        public RungTableView(VisualElement root, TableClient client, Func<long> nowMs, Action leave, SoundPlayer? sounds = null, bool reduceMotion = false, bool oneTap = false)
        {
            _root = root;
            _client = client;
            _nowMs = nowMs;
            _sounds = sounds;
            _reduceMotion = reduceMotion;
            _ring = new OpponentRing(root.Q<VisualElement>("opponents"));
            root.Q<VisualElement>("me")?.Add(_me);
            _pile = new PileView(root.Q<VisualElement>("pile")) { ReduceMotion = reduceMotion };
            _hand = new HandView(root.Q<VisualElement>("hand")) { OneTap = oneTap };
            _hand.Played += card => _client.PlayCard(card);
            _info = root.Q<Label>("info");
            _statusText = root.Q<Label>("status-text");
            _statusSecs = root.Q<Label>("status-secs");
            _note = root.Q<Label>("pile-note");
            _mic = root.Q<Button>("mic");
            _mic.style.display = DisplayStyle.None;
            _voicePill = root.Q<Label>("voice-pill");
            _speaker = root.Q<Button>("voice-speaker");
            _speaker.clicked += () => _voice?.ToggleSpeaker();
            var leaveButton = root.Q<Button>("leave");
            leaveButton.text = UiText.Leave;
            leaveButton.clicked += leave;
            // Thulla's own controls on the shared layout: hand-taking, the discard, the recap and the coach.
            foreach (var name in new[] { "take-hand", "discard", "recap", "coach", "chat", "more", "rules", "settings", "watch-icon", "watch-count", "results", "quick-strip" })
            {
                var element = root.Q<VisualElement>(name);
                if (element != null)
                {
                    element.style.display = DisplayStyle.None;
                }
            }

            var felt = root.Q<VisualElement>("felt");
            BuildBar(felt);
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

        public void UseVoice(VoiceController voice)
        {
            _voice = voice;
            _micControl = new MicControl(_mic, voice, ShowVoice, "table__mic-tip");
            voice.Changed += ShowVoice;
            ShowVoice();
        }

        public void ApplySettings(bool oneTap, bool reduceMotion, bool largeCards)
        {
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
                    return 0f;
                case RungTrickWon _:
                    return _reduceMotion ? 0.3f : TrickHoldSeconds;
                case RungPileTaken taken:
                    _sounds?.Play(Sound.Pickup);
                    Say(Strings.Format(RungText.PileTaken, NameOf(taken.Seat)));
                    return _reduceMotion ? 0.3f : TrickHoldSeconds;
                case RungTrumpCalled _:
                    _sounds?.Play(Sound.Stamp);
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
            foreach (var badge in _ring.Badges)
            {
                badge.Tick(deltaSeconds);
            }

            if (_micControl is { Stale: true })
            {
                ShowVoice();
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
            if (_voice != null)
            {
                _voice.Changed -= ShowVoice;
            }

            _micControl?.Dispose();
        }

        private void BuildBar(VisualElement felt)
        {
            _bar.AddToClassList("rung-bar");
            _bar.pickingMode = PickingMode.Ignore;
            _trump.AddToClassList("rung-bar__trump");
            _tricks.AddToClassList("rung-bar__tricks");
            _pileCount.AddToClassList("rung-bar__pile");
            _bazi.AddToClassList("rung-bar__bazi");
            foreach (var plate in new[] { _trump, _tricks, _pileCount, _bazi })
            {
                // Read on any skin's felt: ivory on a dark plate, set here so no skin's label colours win.
                plate.style.color = new UnityEngine.Color(0.96f, 0.95f, 0.91f);
                plate.style.backgroundColor = new UnityEngine.Color(0.13f, 0.09f, 0.1f, 0.88f);
                plate.style.fontSize = 30;
                _bar.Add(plate);
            }

            _trump.style.fontSize = 44;
            _trump.style.backgroundColor = new UnityEngine.Color(0.98f, 0.97f, 0.95f);
            _pileCount.style.color = new UnityEngine.Color(1f, 0.76f, 0.29f);
            felt.Add(_bar);
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
            panel.Add(_resultTitle);
            panel.Add(_resultLine);
            panel.Add(_resultBazi);
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
            _trump.style.color = rung.Trump == 'H' || rung.Trump == 'D' ? new UnityEngine.Color(0.83f, 0.18f, 0.18f) : new UnityEngine.Color(0.11f, 0.11f, 0.12f);
            _trump.style.display = rung.Trump == null ? DisplayStyle.None : DisplayStyle.Flex;
            _tricks.text = RungText.Us + " " + Strings.Format(RungText.Tricks, rung.Counts[team], rung.Counts[1 - team]) + " " + RungText.Them;
            _pileCount.text = rung.Pile > 0 ? Strings.Format(RungText.Pile, rung.Pile) : "";
            _pileCount.style.display = rung.Pile > 0 ? DisplayStyle.Flex : DisplayStyle.None;
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
                // A suit button is a card face whatever the skin makes of buttons: dark suits on ivory.
                var red = suit == 'H' || suit == 'D';
                button.style.backgroundColor = new UnityEngine.Color(0.98f, 0.97f, 0.95f);
                button.style.color = red ? new UnityEngine.Color(0.83f, 0.18f, 0.18f) : new UnityEngine.Color(0.11f, 0.11f, 0.12f);
                button.EnableInClassList("rung-ask__suit--red", red);
                button.style.backgroundImage = StyleKeyword.None;
                button.style.width = 118;
                button.style.height = 118;
                button.style.minWidth = 0;
                button.style.fontSize = 78;
                button.style.paddingLeft = 0;
                button.style.paddingRight = 0;
                button.style.paddingTop = 0;
                button.style.paddingBottom = 0;
                button.style.marginLeft = 4;
                button.style.marginRight = 4;
                button.style.borderTopLeftRadius = 24;
                button.style.borderTopRightRadius = 24;
                button.style.borderBottomLeftRadius = 24;
                button.style.borderBottomRightRadius = 24;
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
            }

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

            _resultLine.text = RungText.Us + " " + Strings.Format(RungText.Tricks, result.Counts[team], result.Counts[1 - team]) + " " + RungText.Them;
            var points = rung.Teams?.Points;
            _resultBazi.text = points != null && points.Count >= 2 ? Strings.Format(RungText.Bazi, points[team], points[1 - team]) : "";
            var votes = model.RematchVotes.Count;
            _again.text = _voted || votes > 0
                ? Strings.Format(RungText.NextDealVotes, votes, RungModel.Seats)
                : finished ? RungText.NewBazi : RungText.NextDeal;
            _again.SetEnabled(!_voted && !model.Spectating);
            _switch.style.display = model.IsHost && model.IsPrivate ? DisplayStyle.Flex : DisplayStyle.None;
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

        private void ShowVoice()
        {
            var voice = _voice;
            if (voice == null || _micControl == null)
            {
                return;
            }

            var hidden = voice.State == VoiceState.Hidden;
            _mic.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            _mic.SetEnabled(voice.CanSpeak);
            var retry = voice.State == VoiceState.Failed && voice.MicOn;
            var live = voice.MicOn && !retry && (!_micControl.HoldMode || voice.Talking);
            _mic.text = _micControl.Label();
            _mic.EnableInClassList("table__mic--off", !voice.CanSpeak || !live);
            _mic.EnableInClassList("table__mic--on", live);
            _mic.EnableInClassList("table__mic--retry", retry);
            _mic.EnableInClassList("table__mic--held", voice.Talking);
            var note = MicControl.Line(voice, _micControl.Render());
            _voicePill.text = note;
            _voicePill.style.display = note.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            var speakerShown = !hidden && voice.State != VoiceState.Suspended;
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

        private string NameOf(long seat)
        {
            var seats = _client.Model.Seats;
            return seat >= 0 && seat < seats.Count && !string.IsNullOrEmpty(seats[(int)seat].DisplayName) ? seats[(int)seat].DisplayName! : "?";
        }
    }
}
