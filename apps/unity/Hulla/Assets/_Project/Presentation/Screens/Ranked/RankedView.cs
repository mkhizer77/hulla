using System;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Screens.Leaderboard;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Ranked
{
    /// <summary>
    /// The Ranked card (GDD 7.5, 12.9): league or placement progress, rating, the season's end, Play
    /// ranked, the leagues with their season rewards, and the season's top places.
    /// </summary>
    public sealed class RankedView : IDisposable
    {
        private readonly RankedViewModel _model;
        private readonly Func<long> _nowMs;
        private readonly string? _myId;
        private readonly Label _status;
        private readonly Label _banner;
        private readonly VisualElement _card;
        private readonly Label _league;
        private readonly Label _rating;
        private readonly VisualElement _bar;
        private readonly VisualElement _fill;
        private readonly Label _progress;
        private readonly Label _season;
        private readonly Button _play;
        private readonly Label _ladderHeading;
        private readonly VisualElement _ladder;
        private readonly Label _topHeading;
        private readonly VisualElement _top;

        public RankedView(VisualElement root, RankedViewModel model, Func<long> nowMs, string? myId, Action back, Action<RankedCard> play)
        {
            _model = model;
            _nowMs = nowMs;
            _myId = myId;
            root.Q<Label>("title").text = RankedText.Title;
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            _status = root.Q<Label>("status");
            _banner = root.Q<Label>("banner");
            _card = root.Q<VisualElement>("card");
            _league = root.Q<Label>("league");
            _rating = root.Q<Label>("rating");
            _bar = root.Q<VisualElement>("bar");
            _fill = root.Q<VisualElement>("fill");
            _progress = root.Q<Label>("progress");
            _season = root.Q<Label>("season");
            root.Q<Label>("rules").text = RankedText.Rules;
            _play = root.Q<Button>("play");
            _play.text = RankedText.Play;
            _play.clicked += () =>
            {
                if (_model.Card != null)
                {
                    play(_model.Card);
                }
            };
            _ladderHeading = root.Q<Label>("ladder-heading");
            _ladderHeading.text = RankedText.Leagues;
            _ladder = root.Q<VisualElement>("ladder");
            _topHeading = root.Q<Label>("top-heading");
            _topHeading.text = RankedText.Top;
            _top = root.Q<VisualElement>("top");
            _model.Changed += Refresh;
            Refresh();
        }

        /// <summary>Loads the card again after an error; null hides Retry.</summary>
        public Action? Retry { get; set; }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            var card = _model.Card;
            _status.text = "";
            _ladder.Clear();
            _top.Clear();
            var shown = card != null;
            _card.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            _ladderHeading.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            _topHeading.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            _play.SetEnabled(shown && !_model.Loading);
            if (_model.ErrorCode != null)
            {
                _banner.EnableInClassList("ranked__banner--on", false);
                ListState.Error(_ladder, _model.ErrorCode, Retry);
                return;
            }

            if (card == null)
            {
                _banner.EnableInClassList("ranked__banner--on", false);
                if (_model.Loading)
                {
                    ListState.Loading(_ladder);
                }

                return;
            }

            ShowBanner(card);
            _league.text = RankedText.League(card.League);
            _rating.text = string.Format(RankedText.Rating, card.Rating) + (card.Rank is long rank ? " · " + string.Format(RankedText.Rank, rank) : "");
            if (card.Placed)
            {
                _bar.style.display = DisplayStyle.None;
                _progress.text = card.ProtectedRounds > 0 ? string.Format(RankedText.Protected, card.ProtectedRounds) : "";
            }
            else
            {
                _bar.style.display = DisplayStyle.Flex;
                _fill.style.width = Length.Percent(card.PlacementOf == 0 ? 100f : 100f * card.PlacementPlayed / card.PlacementOf);
                _progress.text = string.Format(RankedText.Placement, card.PlacementPlayed, card.PlacementOf) + ". " +
                    string.Format(RankedText.PlacementNote, card.PlacementOf - card.PlacementPlayed);
            }

            _season.text = string.Format(RankedText.SeasonEnds, card.SeasonNumber, LeaderboardView.Duration(card.SeasonEndTs - _nowMs()));
            for (var i = card.Leagues.Count - 1; i >= 0; i--)
            {
                _ladder.Add(Step(card.Leagues[i], card.League));
            }

            if (_model.Top.Count == 0)
            {
                ListState.Empty(_top, RankedText.TopEmpty);
            }

            foreach (var row in _model.Top)
            {
                _top.Add(TopRow(row, row.UserId == _myId));
            }
        }

        private void ShowBanner(RankedCard card)
        {
            var last = card.LastSeason;
            var on = card.SeasonSettled && last != null;
            _banner.EnableInClassList("ranked__banner--on", on);
            if (!on || last == null)
            {
                return;
            }

            _banner.text = last.League == null
                ? string.Format(RankedText.SeasonEndedUnplaced, RankedText.Season(last.SeasonId))
                : string.Format(RankedText.SeasonEnded, RankedText.Season(last.SeasonId), RankedText.League(last.League));
        }

        private static VisualElement Step(RankedLeague league, string? mine)
        {
            var line = new VisualElement();
            line.AddToClassList("ranked__step");
            if (league.Id == mine)
            {
                line.AddToClassList("ranked__step--me");
            }

            var name = new Label(RankedText.League(league.Id));
            name.AddToClassList("ranked__step-name");
            line.Add(name);
            var floor = league.TopOnly ? string.Format(RankedText.UstadNote, 500) : league.MinRating + "+";
            var detail = new Label(floor + " · " + RankedText.Reward(league));
            detail.AddToClassList("ranked__step-detail");
            line.Add(detail);
            return line;
        }

        private static VisualElement TopRow(RankedRow row, bool mine)
        {
            var line = new VisualElement();
            line.AddToClassList("leaderboard__row");
            if (mine)
            {
                line.AddToClassList("leaderboard__row--me");
            }

            var rank = new Label(row.Rank.ToString());
            rank.AddToClassList("leaderboard__rank");
            line.Add(rank);
            var name = new Label(mine ? string.Format(UiText.YouAre, row.Name) : row.Name);
            name.AddToClassList("leaderboard__name");
            line.Add(name);
            var rating = new Label(RankedText.League(row.League) + " · " + row.Rating);
            rating.AddToClassList("leaderboard__points");
            line.Add(rating);
            return line;
        }
    }
}
