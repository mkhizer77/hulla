using System;
using System.Collections.Generic;
using Hulla.Application.Table;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using UnityEngine.UIElements;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The results overlay (GDD 14.8): the escape-order podium, the loser with the table's title, the
    /// room's points and Bhabhi tally, a highlight or two, "Add friend" chips for co-players who are
    /// not friends yet, and the rematch vote with its countdown (R52).
    /// </summary>
    public sealed class ThullaResults
    {
        private readonly VisualElement _root;
        private readonly Label _title;
        private readonly VisualElement _body;
        private readonly Button _playAgain;
        private readonly Label _status;
        private readonly Func<long, string> _nameOf;
        private long _shownSeconds = -1;
        private readonly Label _reward;
        private readonly Button _share;
        private Action? _onShare;

        public ThullaResults(VisualElement root, Func<long, string> nameOf, Action playAgain)
        {
            _root = root;
            _nameOf = nameOf;
            _title = root.Q<Label>("result-title");
            _body = root.Q<VisualElement>("result-order");
            _playAgain = root.Q<Button>("play-again");
            _playAgain.text = UiText.PlayAgain;
            _playAgain.clicked += playAgain;
            _status = root.Q<Label>("result-status");
            _reward = root.Q<Label>("result-reward");
            _share = root.Q<Button>("result-share");
            _share.text = UiText.ShareResult;
            _share.clicked += () => _onShare?.Invoke();
            _share.style.display = DisplayStyle.None;
        }

        /// <summary>Shares the result as a card (GDD 10.9); null hides the button (the tutorial).</summary>
        public Action? Share
        {
            get => _onShare;
            set
            {
                _onShare = value;
                _share.style.display = value != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>The round's XP, coins and any level-up (GDD 14.8), once the server has paid it.</summary>
        public void ShowReward(string? text) => _reward.text = text ?? "";

        /// <summary>Who may get an "Add friend" chip; null hides the chips.</summary>
        public Func<string, bool>? CanAdd { get; set; }

        public Action<string>? AddFriend { get; set; }

        public void Show(TableModel model, bool voted)
        {
            var result = model.LastResult;
            var show = result != null && !model.InRound;
            _root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (result == null || !show)
            {
                return;
            }

            _title.text = string.Format(UiText.LoserIs, _nameOf(result.BhabhiSeat), LoserTitle(model));
            _body.Clear();
            Heading(UiText.EscapeOrder);
            for (var i = 0; i < result.EscapeOrder.Count; i++)
            {
                Line((i + 1) + ". " + _nameOf(result.EscapeOrder[i]), i == 0 ? "results__first" : null);
            }

            var series = result.Series;
            if (series != null && series.RoundsPlayed > 1)
            {
                Heading(string.Format(UiText.AfterRounds, series.RoundsPlayed));
                for (var seat = 0; seat < series.Points.Count; seat++)
                {
                    var tally = seat < series.LoserTally.Count ? series.LoserTally[seat] : 0;
                    Line(string.Format(UiText.PointsLine, _nameOf(seat), series.Points[seat], tally, LoserTitle(model)), null);
                }
            }

            Highlights(result);
            FriendChips(model);
            _playAgain.SetEnabled(!voted);
            _shownSeconds = -1;
            UpdateStatus(model, voted, 0);
        }

        /// <summary>Per frame while the overlay shows: the rematch countdown, redrawn when the second changes.</summary>
        public void Tick(TableModel model, bool voted, long nowMs)
        {
            if (_root.style.display == DisplayStyle.Flex)
            {
                UpdateStatus(model, voted, nowMs);
            }
        }

        private void UpdateStatus(TableModel model, bool voted, long nowMs)
        {
            var deadline = model.RematchDeadlineTs;
            var seconds = deadline == null || nowMs == 0 ? -1 : Math.Max(0, (deadline.Value - nowMs + 999) / 1000);
            if (seconds == _shownSeconds && nowMs != 0)
            {
                return;
            }

            _shownSeconds = seconds;
            var votes = model.RematchVotes.Count;
            var players = 0;
            foreach (var seat in model.Seats)
            {
                players += seat.Occupied ? 1 : 0;
            }

            var text = voted ? UiText.Waiting4Rematch : "";
            if (votes > 0)
            {
                text = (text.Length > 0 ? text + " " : "") + string.Format(UiText.RematchVotes, votes, players);
            }

            if (seconds >= 0)
            {
                text = (text.Length > 0 ? text + " · " : "") + seconds + "s";
            }

            _status.text = text;
        }

        private void Highlights(RoundEnd result)
        {
            long biggest = 0;
            long picker = -1;
            long thullas = 0;
            long giver = -1;
            for (var seat = 0; seat < result.Stats.Count; seat++)
            {
                var stats = result.Stats[seat];
                if (stats.CardsPickedUp > biggest)
                {
                    biggest = stats.CardsPickedUp;
                    picker = seat;
                }

                if (stats.ThullasGiven > thullas)
                {
                    thullas = stats.ThullasGiven;
                    giver = seat;
                }
            }

            if (picker < 0 && giver < 0)
            {
                return;
            }

            Heading(UiText.Highlights);
            if (giver >= 0)
            {
                Line(string.Format(UiText.MostThullas, _nameOf(giver), thullas), null);
            }

            if (picker >= 0)
            {
                Line(string.Format(UiText.MostPickedUp, _nameOf(picker), biggest), null);
            }
        }

        private void FriendChips(TableModel model)
        {
            if (CanAdd == null || AddFriend == null)
            {
                return;
            }

            var row = new VisualElement();
            row.AddToClassList("chips");
            foreach (var seat in model.Seats)
            {
                if (!seat.Occupied || seat.Seat == model.MySeat || seat.PlayerId == null || !CanAdd(seat.PlayerId))
                {
                    continue;
                }

                var id = seat.PlayerId;
                var chip = new Button { text = string.Format(UiText.AddName, seat.DisplayName ?? "?") };
                chip.AddToClassList("chip");
                chip.clicked += () =>
                {
                    AddFriend(id);
                    chip.SetEnabled(false);
                    chip.text = UiText.RequestSent;
                };
                row.Add(chip);
            }

            if (row.childCount > 0)
            {
                Heading(UiText.MakeFriends);
                _body.Add(row);
            }
        }

        private void Heading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("caption");
            label.AddToClassList("settings__heading");
            _body.Add(label);
        }

        private void Line(string text, string? cls)
        {
            var label = new Label(text);
            label.AddToClassList("rulebook__line");
            if (cls != null)
            {
                label.AddToClassList(cls);
            }

            _body.Add(label);
        }

        private static string LoserTitle(TableModel model) =>
            (model.Selection?.Settings?.LoserTitle ?? Protocol.LoserTitle.Bhabhi) switch
            {
                Protocol.LoserTitle.Donkey => RulebookText.Donkey,
                Protocol.LoserTitle.Thullad => RulebookText.Thullad,
                _ => RulebookText.Bhabhi,
            };
    }
}
