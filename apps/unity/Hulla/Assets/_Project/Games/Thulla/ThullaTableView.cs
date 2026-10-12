using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The Thulla table (GDD 14.3 to 14.5) on the UI Toolkit table kit: opponents on the seat arc,
    /// the pile fanning in from each seat, the fanned hand with the server's legal cards lit and a
    /// one-line reason on an illegal tap, the turn timer ring, take-hand, quick chat, the thulla
    /// moment, the first-round rules recap and the results overlay with the rematch vote. It renders
    /// from the model; the scheduler only decides how long each event holds the stage.
    /// </summary>
    public sealed class ThullaTableView : IEventPresenter, IDisposable
    {
        /// <summary>The thulla moment never delays the next turn by more than this (GDD 14.6).</summary>
        private const float ThullaMomentSeconds = 1.2f;

        /// <summary>The first round opens with a short rules recap that a player can turn off (GDD 9.5).</summary>
        private const float RecapSeconds = 3f;

        /// <summary>How long a phrase, an emote or a play hint stays on screen.</summary>
        private const float SaySeconds = 3f;

        private readonly TableClient _client;
        private readonly Func<long> _nowMs;
        private readonly OpponentRing _ring;
        private readonly PileView _pile;
        private readonly Label _pileNote;
        private readonly HandView _hand;
        private readonly Label _turn;
        private readonly TimerRing _timer;
        private readonly Label _hint;
        private readonly Label _info;
        private readonly Button _takeHand;
        private readonly Label _toast;
        private readonly VisualElement _results;
        private readonly Label _resultTitle;
        private readonly VisualElement _resultOrder;
        private readonly Button _playAgain;
        private readonly Label _resultStatus;
        private readonly IChatCatalog _chat;
        private readonly VisualElement _chatPanel;
        private readonly Label _mySay;
        private readonly StampTimeline _stamp = new StampTimeline();
        private readonly VisualElement _felt;
        private readonly bool _reduceMotion;
        private readonly VisualElement _recap;
        private readonly Action? _hideRecapForGood;
        private readonly List<(string Card, string Owner, long Seat)> _plays = new List<(string Card, string Owner, long Seat)>();
        private readonly List<string> _handCards = new List<string>();
        private float _mySayLeft;
        private float _hintLeft;
        private bool _recapShown;
        private float _recapLeft;
        private long _shownSeconds = -1;
        private long _turnStartMs;
        private long _turnDeadlineMs;
        private bool _voted;

        /// <param name="oneTap">Play on the first tap instead of tap-to-raise (a setting, GDD 14.4).</param>
        /// <param name="hideRecapForGood">Remembers "don't show again"; null when the player already chose it.</param>
        public ThullaTableView(
            VisualElement root,
            TableClient client,
            IChatCatalog chat,
            Func<long> nowMs,
            Action leave,
            bool reduceMotion = false,
            Action? hideRecapForGood = null,
            bool oneTap = false)
        {
            _client = client;
            _chat = chat;
            _nowMs = nowMs;
            _reduceMotion = reduceMotion;
            _hideRecapForGood = hideRecapForGood;
            _felt = root.Q<VisualElement>("felt");
            _ring = new OpponentRing(root.Q<VisualElement>("opponents"));
            _pile = new PileView(root.Q<VisualElement>("pile")) { ReduceMotion = reduceMotion };
            _pileNote = root.Q<Label>("pile-note");
            _hand = new HandView(root.Q<VisualElement>("hand")) { OneTap = oneTap };
            _hand.Played += card => _client.PlayCard(card);
            _hand.Refused += ShowHint;
            _turn = root.Q<Label>("turn");
            _timer = new TimerRing();
            root.Q<VisualElement>("timer").Add(_timer);
            _hint = root.Q<Label>("hint");
            _info = root.Q<Label>("info");
            _takeHand = root.Q<Button>("take-hand");
            _takeHand.text = UiText.TakeHand;
            _takeHand.clicked += () => _client.TakeHand();
            _toast = root.Q<Label>("toast");
            _results = root.Q<VisualElement>("results");
            _resultTitle = root.Q<Label>("result-title");
            _resultOrder = root.Q<VisualElement>("result-order");
            _playAgain = root.Q<Button>("play-again");
            _playAgain.text = UiText.PlayAgain;
            _playAgain.clicked += VoteAgain;
            _resultStatus = root.Q<Label>("result-status");
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
            var chatButton = root.Q<Button>("chat");
            chatButton.text = UiText.Chat;
            chatButton.clicked += () => SetChatOpen(_chatPanel.style.display != DisplayStyle.Flex);
            var closeChat = root.Q<Button>("chat-close");
            closeChat.text = UiText.Close;
            closeChat.clicked += () => SetChatOpen(false);
            BuildChatPanel(root.Q<VisualElement>("chat-phrases"), root.Q<VisualElement>("chat-emotes"));
            _client.Model.Changed += Refresh;
            _client.Model.Chat += OnChat;
            Refresh();
        }

        public float Present(ServerMessage message)
        {
            switch (message.Payload)
            {
                case MoveApplied applied when applied.Thulla == true:
                    ShowStamp(UiText.Thulla);
                    return ThullaMomentSeconds;
                case TrickResolved resolved when resolved.ThullaBy != null:
                    return ThullaMomentSeconds;
                case TrickResolved _:
                    return 0.6f;
                case MoveApplied _:
                    return 0.2f;
                case RoundStart _:
                    _voted = false;
                    if (!_recapShown && _hideRecapForGood != null)
                    {
                        _recapShown = true;
                        _recapLeft = RecapSeconds;
                        _recap.style.display = DisplayStyle.Flex;
                    }

                    return 0.4f;
                default:
                    return 0f;
            }
        }

        /// <summary>Per frame: bubbles, the recap, the stamp and the turn timer, redrawn only when they change.</summary>
        public void Tick(float deltaSeconds)
        {
            foreach (var badge in _ring.Badges)
            {
                badge.Tick(deltaSeconds);
            }

            Countdown(ref _recapLeft, deltaSeconds, _recap);
            Countdown(ref _mySayLeft, deltaSeconds, _mySay);
            Countdown(ref _hintLeft, deltaSeconds, _hint);
            if (_stamp.Active)
            {
                _stamp.Advance(deltaSeconds);
                ApplyStamp();
            }

            var model = _client.Model;
            if (!model.MyTurn)
            {
                return;
            }

            var now = _nowMs();
            var leftMs = Math.Max(0, _turnDeadlineMs - now);
            var total = Math.Max(1, _turnDeadlineMs - _turnStartMs);
            _timer.Show((float)leftMs / total, leftMs / 1000f);
            var seconds = (leftMs + 999) / 1000;
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _turn.text = UiText.YourTurn + " · " + seconds + "s";
            }
        }

        public void Dispose()
        {
            _client.Model.Changed -= Refresh;
            _client.Model.Chat -= OnChat;
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

        private void Say(Action send)
        {
            send();
            SetChatOpen(false);
        }

        private void SetChatOpen(bool open) => _chatPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

        private void OnChat(ChatLine line)
        {
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

        private void VoteAgain()
        {
            _voted = true;
            _client.VoteRematch(true);
            Refresh();
        }

        private void ShowStamp(string text)
        {
            _toast.text = text;
            _stamp.Play(_reduceMotion);
            ApplyStamp();
        }

        private void ApplyStamp()
        {
            _toast.style.display = _stamp.Active ? DisplayStyle.Flex : DisplayStyle.None;
            _toast.style.opacity = _stamp.Opacity;
            _toast.style.scale = new Scale(new Vector2(_stamp.Scale, _stamp.Scale));
            _felt.style.translate = new Translate(_stamp.ShakeX, 0f);
        }

        private string NameOf(long seat)
        {
            var model = _client.Model;
            if (seat == model.MySeat)
            {
                return UiText.You;
            }

            return seat < model.Seats.Count ? model.Seats[(int)seat].DisplayName ?? "?" : "?";
        }

        private void Refresh()
        {
            RefreshInfo();
            RefreshOpponents();
            RefreshPile();
            RefreshHand();
            RefreshTurn();
            RefreshResults();
        }

        private void RefreshInfo()
        {
            var model = _client.Model;
            var text = model.IsPrivate && model.Code.Length > 0 ? model.Code : UiText.QuickPlayTable;
            if (model.InRound)
            {
                text += " · " + string.Format(UiText.Discard, model.DiscardCount);
                if (model.FairDealCommitted)
                {
                    text += " · " + UiText.FairDeal;
                }
            }

            _info.text = text;
        }

        private void RefreshOpponents()
        {
            var model = _client.Model;
            var count = model.SeatCount > 0 ? model.SeatCount : model.Seats.Count;
            _ring.Layout(count, model.MySeat ?? 0);
            for (var i = 0; i < count - 1 && i < _ring.Badges.Count; i++)
            {
                var seat = _ring.SeatAt(i);
                var info = seat < model.Seats.Count ? model.Seats[(int)seat] : default;
                var state = model.IsEscaped((int)seat)
                    ? UiText.Escaped
                    : info.AutoPlay || (info.Occupied && !info.Connected)
                        ? UiText.Away
                        : model.InRound && model.LeaderSeat == seat ? UiText.Lead : "";
                _ring.Badges[i].Show(NameOf(seat), model.HandCount((int)seat), model.TurnSeat == seat && model.InRound, state);
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
            _pileNote.text = showingLast && model.LastPicker != null ? string.Format(UiText.PickedUp, NameOf(model.LastPicker.Value)) : "";
        }

        /// <summary>Where a card played by <paramref name="seat"/> starts its flight: the badge, or the hand for the player.</summary>
        private Rect? SeatOrigin(long seat)
        {
            if (seat == _client.Model.MySeat)
            {
                var bounds = _mySay.parent.worldBound;
                return new Rect(bounds.center.x - 1f, bounds.yMax - 1f, 2f, 2f);
            }

            return _ring.BadgeFor(seat)?.worldBound;
        }

        private void RefreshHand()
        {
            var model = _client.Model;
            _handCards.Clear();
            foreach (var card in model.Hand)
            {
                if (card != model.PendingCard)
                {
                    _handCards.Add(card);
                }
            }

            _hand.Show(_handCards, model.IsLegal, model.MyTurn);
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
                }

                _turn.text = UiText.YourTurn;
                _timer.style.display = DisplayStyle.Flex;
            }
            else
            {
                _turn.text = !model.InRound || model.TurnSeat == null
                    ? ""
                    : model.TurnSeat == model.MySeat ? UiText.YourTurn : NameOf(model.TurnSeat.Value) + "…";
                _timer.style.display = DisplayStyle.None;
            }

            _takeHand.style.display = model.MyTurn && model.CanTakeHand ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshResults()
        {
            var model = _client.Model;
            var result = model.LastResult;
            var show = result != null && !model.InRound;
            _results.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (result == null || !show)
            {
                return;
            }

            _resultTitle.text = string.Format(UiText.Bhabhi, NameOf(result.BhabhiSeat));
            _resultOrder.Clear();
            var heading = new Label(UiText.EscapeOrder);
            heading.AddToClassList("caption");
            _resultOrder.Add(heading);
            for (var i = 0; i < result.EscapeOrder.Count; i++)
            {
                _resultOrder.Add(new Label((i + 1) + ". " + NameOf(result.EscapeOrder[i])));
            }

            _playAgain.SetEnabled(!_voted);
            _resultStatus.text = _voted ? UiText.Waiting4Rematch : "";
        }
    }
}
