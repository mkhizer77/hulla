using System;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Achievements
{
    /// <summary>Every achievement by category with progress, its gems and title, and a tick when done (GDD 12.7).</summary>
    public sealed class AchievementsView : IDisposable
    {
        private readonly AchievementsViewModel _model;
        private readonly VisualElement _list;
        private readonly Label _status;

        public AchievementsView(VisualElement root, AchievementsViewModel model, Action back)
        {
            _model = model;
            root.Q<Label>("title").text = UiText.Achievements;
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            _list = root.Q<VisualElement>("list");
            _status = root.Q<Label>("status");
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        /// <summary>Loads the list again after an error; null hides Retry.</summary>
        public Action? Retry { get; set; }

        private void Refresh()
        {
            _status.text = _model.Total > 0 ? string.Format(UiText.AchievementsCount, _model.UnlockedCount, _model.Total) : "";
            _list.Clear();
            if (_model.ErrorCode != null)
            {
                ListState.Error(_list, _model.ErrorCode, Retry);
                return;
            }

            if (_model.Total == 0)
            {
                ListState.Loading(_list);
                return;
            }

            foreach (var (category, items) in _model.Groups)
            {
                var heading = new Label(AchievementText.Category(category));
                heading.AddToClassList("caption");
                heading.AddToClassList("settings__heading");
                _list.Add(heading);
                foreach (var a in items)
                {
                    _list.Add(Row(a));
                }
            }
        }

        private static VisualElement Row(AchievementInfo a)
        {
            var row = new VisualElement();
            row.AddToClassList("lobby-seat");
            row.AddToClassList("friend-row");
            if (!a.Unlocked)
            {
                row.AddToClassList("achievement--locked");
            }

            var text = new VisualElement();
            text.AddToClassList("lobby-seat__name");
            text.Add(new Label(AchievementText.Name(a.Id, a.Metric, a.Target)));
            var detail = new Label(AchievementText.Goal(a.Metric, a.Target) + " · " + Reward(a));
            detail.AddToClassList("caption");
            detail.style.whiteSpace = WhiteSpace.Normal;
            text.Add(detail);
            row.Add(text);
            var state = new Label(a.Unlocked ? UiText.Done : $"{Math.Min(a.Progress, a.Target):N0}/{a.Target:N0}");
            state.AddToClassList(a.Unlocked ? "tag--ok" : "caption");
            row.Add(state);
            return row;
        }

        private static string Reward(AchievementInfo a) =>
            a.Title != null ? string.Format(UiText.GemsAndTitle, a.Gems, UiText.TitleName(a.Title)) : string.Format(UiText.GemsOnly, a.Gems);
    }
}
