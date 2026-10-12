using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using UnityEngine.UIElements;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The Thulla table in UI Toolkit: opponents across the top in play order, the pile in the middle,
    /// the hand at the bottom with the server's legal cards lit, the turn timer, take-hand, the thulla
    /// moment and the results overlay with the rematch vote. It renders from the model; the scheduler
    /// only decides how long each event holds the stage.
    /// </summary>
    public sealed class ThullaTableView : IEventPresenter, IDisposable
    {
        /// <summary>The thulla moment never delays the next turn by more than this (GDD 14.6).</summary>
        private const float ThullaMomentSeconds = 1.2f;

        private readonly TableClient _client;
        private readonly Func<long> _nowMs;
        private readonly VisualElement _opponents;
        private readonly VisualElement _pile;
        private readonly Label _pileNote;
        private readonly VisualElement _hand;
        private readonly Label _turn;
        private readonly Button _takeHand;
        private readonly Label _toast;
        private readonly VisualElement _results;
        private readonly Label _resultTitle;
        private readonly VisualElement _resultOrder;
        private readonly Button _playAgain;
        private readonly Label _resultStatus;
        private readonly List<SeatBadge> _badges = new List<SeatBadge>();
        private readonly IChatCatalog _chat;
        private readonly VisualElement _chatPanel;
        private readonly Label _mySay;
        private float _mySayLeft;
        private readonly StampTimeline _stamp = new StampTimeline();
        private readonly VisualElement _felt;
        private readonly bool _reduceMotion;
        private readonly VisualElement _recap;
        private readonly Action? _hideRecapForGood;
        private bool _recapShown;
        private float _recapLeft;

        /// <summary>The first round opens with a short rules recap that a player can turn off (GDD 9.5).</summary>
        private const float RecapSeconds = 3f;
        private long _shownSeconds = -1;
        private bool _voted;

        /// <summary>How long a phrase or emote stays on screen.</summary>
        private const float SaySeconds = 3f;

        /// <param name="hideRecapForGood">Remembers "don't show again"; null when the player already chose it.</param>
        public ThullaTableView(VisualElement root, TableClient client, IChatCatalog chat, Func<long> nowMs, Action leave, bool reduceMotion = false, Action? hideRecapForGood = null)
        {
            _hideRecapForGood = hideRecapForGood;
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
            _reduceMotion = reduceMotion;
            _felt = root.Q<VisualElement>("felt");
            _client = client;
            _chat = chat;
            _nowMs = nowMs;
            _opponents = root.Q<VisualElement>("opponents");
            _pile = root.Q<VisualElement>("pile");
            _pileNote = root.Q<Label>("pile-note");
            _hand = root.Q<VisualElement>("hand");
            _turn = root.Q<Label>("turn");
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

        /// <summary>Per frame: the toast fade and the turn countdown, redrawn only when the second changes.</summary>
        public void Tick(float deltaSeconds)
        {
            foreach (var badge in _badges)
            {
                badge.Tick(deltaSeconds);
            }

            if (_recapLeft > 0f)
            {
                _recapLeft -= deltaSeconds;
                if (_recapLeft <= 0f)
                {
                    _recap.style.display = DisplayStyle.None;
                }
            }

            if (_mySayLeft > 0f)
            {
                _mySayLeft -= deltaSeconds;
                if (_mySayLeft <= 0f)
                {
                    _mySay.style.display = DisplayStyle.None;
                }
            }

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

            var seconds = Math.Max(0, (model.DeadlineTs - _nowMs() + 999) / 1000);
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
            var model = _client.Model;
            if (line.Seat == model.MySeat)
            {
                _mySay.text = text;
                _mySay.style.display = DisplayStyle.Flex;
                _mySayLeft = SaySeconds;
                return;
            }

            var count = model.SeatCount;
            if (count == 0)
            {
                return;
            }

            var index = (int)((line.Seat - (model.MySeat ?? 0) - 1 + count) % count);
            if (index >= 0 && index < _badges.Count)
            {
                _badges[index].Say(text, SaySeconds);
            }
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
            _toast.style.scale = new Scale(new UnityEngine.Vector2(_stamp.Scale, _stamp.Scale));
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
            RefreshOpponents();
            RefreshPile();
            RefreshHand();
            RefreshTurn();
            RefreshResults();
        }

        private void RefreshOpponents()
        {
            var model = _client.Model;
            var count = model.SeatCount;
            var mine = (int)(model.MySeat ?? 0);
            var needed = Math.Max(0, count - 1);
            while (_badges.Count < needed)
            {
                var badge = new SeatBadge();
                _badges.Add(badge);
                _opponents.Add(badge);
            }

            for (var i = 0; i < _badges.Count; i++)
            {
                var badge = _badges[i];
                if (i >= needed)
                {
                    badge.style.display = DisplayStyle.None;
                    continue;
                }

                badge.style.display = DisplayStyle.Flex;
                var seat = (mine + 1 + i) % count;
                var info = seat < model.Seats.Count ? model.Seats[seat] : default;
                var state = model.IsEscaped(seat) ? UiText.Escaped : info.AutoPlay || (info.Occupied && !info.Connected) ? UiText.Away : "";
                badge.Show(NameOf(seat), model.HandCount(seat), model.TurnSeat == seat && model.InRound, state);
            }
        }

        private void RefreshPile()
        {
            var model = _client.Model;
            _pile.Clear();
            var showingLast = model.Trick.Count == 0 && model.LastTrick.Count > 0;
            var plays = showingLast ? model.LastTrick : model.Trick;
            foreach (var play in plays)
            {
                var slot = new VisualElement();
                slot.AddToClassList("pile__slot");
                var card = new CardElement().Show(play.Card);
                card.EnableInClassList("card--faded", showingLast);
                slot.Add(card);
                var owner = new Label(NameOf(play.Seat));
                owner.AddToClassList("pile__owner");
                slot.Add(owner);
                _pile.Add(slot);
            }

            _pileNote.text = showingLast && model.LastPicker != null ? string.Format(UiText.PickedUp, NameOf(model.LastPicker.Value)) : "";
        }

        private void RefreshHand()
        {
            var model = _client.Model;
            _hand.Clear();
            foreach (var card in model.Hand)
            {
                if (card == model.PendingCard)
                {
                    continue;
                }

                var element = new CardElement().Show(card);
                var legal = model.MyTurn && model.IsLegal(card);
                element.EnableInClassList("card--legal", legal);
                element.EnableInClassList("card--dim", model.MyTurn && !legal);
                if (legal)
                {
                    var chosen = card;
                    element.RegisterCallback<PointerUpEvent>(_ => _client.PlayCard(chosen));
                }

                _hand.Add(element);
            }
        }

        private void RefreshTurn()
        {
            var model = _client.Model;
            _shownSeconds = -1;
            if (model.MyTurn)
            {
                _turn.text = UiText.YourTurn;
            }
            else if (model.InRound && model.TurnSeat != null)
            {
                _turn.text = NameOf(model.TurnSeat.Value) + "…";
            }
            else
            {
                _turn.text = "";
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
