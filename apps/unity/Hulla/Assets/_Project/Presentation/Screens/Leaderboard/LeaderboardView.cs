using System;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Leaderboard
{
    /// <summary>Baithak points this week or month, for everyone or friends, with your own rank pinned (GDD 12.8).</summary>
    public sealed class LeaderboardView : IDisposable
    {
        private readonly LeaderboardViewModel _model;
        private readonly Func<long> _nowMs;
        private readonly string? _myId;
        private readonly VisualElement _list;
        private readonly VisualElement _me;
        private readonly Label _status;
        private readonly Label _reset;
        private readonly Button _weekly;
        private readonly Button _monthly;
        private readonly Button _everyone;
        private readonly Button _friends;

        public LeaderboardView(VisualElement root, LeaderboardViewModel model, Func<long> nowMs, string? myId, Action back, Action<LeaderboardPeriod, bool> show, Action? ranked = null)
        {
            _model = model;
            _nowMs = nowMs;
            _myId = myId;
            root.Q<Label>("title").text = UiText.Leaderboard;
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            _weekly = Chip(root, "weekly", UiText.ThisWeek, () => show(LeaderboardPeriod.Weekly, _model.Friends));
            _monthly = Chip(root, "monthly", UiText.ThisMonth, () => show(LeaderboardPeriod.Monthly, _model.Friends));
            _everyone = Chip(root, "everyone", UiText.Everyone, () => show(_model.Period, false));
            _friends = Chip(root, "friends", UiText.FriendsTab, () => show(_model.Period, true));
            // Ranked has its own season board (GDD 12.9); the chip opens it.
            var rankedChip = Chip(root, "ranked", RankedText.Title, () => ranked?.Invoke());
            rankedChip.style.display = ranked == null ? DisplayStyle.None : DisplayStyle.Flex;
            _list = root.Q<VisualElement>("list");
            _me = root.Q<VisualElement>("me");
            _status = root.Q<Label>("status");
            _reset = root.Q<Label>("reset");
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        /// <summary>Loads the board again after an error; null hides Retry.</summary>
        public Action? Retry { get; set; }

        private static Button Chip(VisualElement root, string name, string text, Action clicked)
        {
            var chip = root.Q<Button>(name);
            chip.text = text;
            chip.clicked += clicked;
            return chip;
        }

        private void Refresh()
        {
            _weekly.EnableInClassList("chip--on", _model.Period == LeaderboardPeriod.Weekly);
            _monthly.EnableInClassList("chip--on", _model.Period == LeaderboardPeriod.Monthly);
            _everyone.EnableInClassList("chip--on", !_model.Friends);
            _friends.EnableInClassList("chip--on", _model.Friends);
            var page = _model.Page;
            _status.text = "";
            _reset.text = page?.NextResetTs is long reset ? Strings.Format(UiText.ResetsIn, Duration(reset - _nowMs())) : "";
            _list.Clear();
            _me.Clear();
            if (_model.ErrorCode != null)
            {
                _me.style.display = DisplayStyle.None;
                ListState.Error(_list, _model.ErrorCode, Retry);
                return;
            }

            if (page == null)
            {
                _me.style.display = DisplayStyle.None;
                if (_model.Loading)
                {
                    ListState.Loading(_list);
                }

                return;
            }

            if (page.Rows.Count == 0 && !_model.Loading)
            {
                ListState.Empty(_list, UiText.BoardEmpty);
            }

            var meOnPage = false;
            foreach (var row in page.Rows)
            {
                var mine = row.UserId == _myId;
                meOnPage |= mine;
                _list.Add(Row(row, mine));
            }

            _me.style.display = page.Me != null && !meOnPage ? DisplayStyle.Flex : DisplayStyle.None;
            if (page.Me != null && !meOnPage)
            {
                _me.Add(Row(page.Me, true));
            }
        }

        private static VisualElement Row(LeaderboardRow row, bool mine)
        {
            var line = new VisualElement();
            line.AddToClassList("leaderboard__row");
            if (mine)
            {
                line.AddToClassList("leaderboard__row--me");
            }

            var rank = new Label(row.Rank.ToString());
            rank.AddToClassList("leaderboard__rank");
            if (row.Rank is >= 1 and <= 3)
            {
                rank.AddToClassList("leaderboard__rank--" + row.Rank);
            }
            line.Add(rank);
            var name = new Label(mine ? Strings.Format(UiText.YouAre, row.Name) : row.Name);
            name.AddToClassList("leaderboard__name");
            line.Add(name);
            var points = new Label(Strings.Format(UiText.PointsShort, row.Points));
            points.AddToClassList("leaderboard__points");
            line.Add(points);
            return line;
        }

        /// <summary>"3d 4h", "5h 12m" or "12m".</summary>
        public static string Duration(long ms)
        {
            var span = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            if (span.TotalDays >= 1)
            {
                return Strings.Get("ui.duration_days_hours", (int)span.TotalDays, span.Hours);
            }

            return span.TotalHours >= 1
                ? Strings.Get("ui.duration_hours_minutes", (int)span.TotalHours, span.Minutes)
                : Strings.Get("ui.duration_minutes", Math.Max(1, span.Minutes));
        }
    }
}
