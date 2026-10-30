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
            _root = root;
            _status = root.Q<Label>("status");
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        /// <summary>Loads the list again after an error; null hides Retry.</summary>
        public Action? Retry { get; set; }

        private VisualElement? _root;

        /// <summary>
        /// The medal grid (design: Trophies · Achievements): the total with its bar, then three tiles a row,
        /// unlocked first in colour, locked ones grey with a thin bar; a tile opens its sheet.
        /// </summary>
        private void Refresh()
        {
            _status.text = "";
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

            var total = new VisualElement();
            total.AddToClassList("medals__total");
            var count = new Label(Strings.Format(UiText.OfCount, _model.UnlockedCount, _model.Total));
            count.AddToClassList("medals__count");
            total.Add(count);
            var bar = new Meter(_model.UnlockedCount, _model.Total);
            bar.AddToClassList("medals__bar");
            total.Add(bar);
            _list.Add(total);
            var all = new System.Collections.Generic.List<AchievementInfo>();
            foreach (var (_, items) in _model.Groups)
            {
                all.AddRange(items);
            }

            all.Sort((a, b) => a.Unlocked != b.Unlocked ? (a.Unlocked ? -1 : 1) : (b.Progress * a.Target).CompareTo(a.Progress * b.Target));
            // Unlocked medals large, three a row; locked ones as wide tiles, two a row (concept: Achievements).
            var unlocked = new VisualElement();
            unlocked.AddToClassList("medals__grid");
            var locked = new VisualElement();
            locked.AddToClassList("medals__grid");
            locked.AddToClassList("medals__grid--locked");
            foreach (var a in all)
            {
                (a.Unlocked ? unlocked : locked).Add(Tile(a));
            }

            if (unlocked.childCount > 0)
            {
                _list.Add(unlocked);
            }

            if (locked.childCount > 0)
            {
                _list.Add(locked);
            }
        }

        /// <summary>Each achievement has its own medal (Basant.uss: .medal--id), gold when won, silver before.</summary>
        private static void MedalArt(VisualElement medal, string id) => medal.AddToClassList("medal--" + id);

        private VisualElement Tile(AchievementInfo a)
        {
            var tile = new Button(() => OpenSheet(a)) { text = "" };
            tile.AddToClassList("medal-tile");
            tile.EnableInClassList("medal-tile--locked", !a.Unlocked);
            var medal = new VisualElement { pickingMode = PickingMode.Ignore };
            medal.AddToClassList("achievement__medal");
            medal.AddToClassList("medal-tile__medal");
            MedalArt(medal, a.Id);
            tile.Add(medal);
            var name = new Label(AchievementText.Name(a.Id, a.Metric, a.Target)) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("medal-tile__name");
            if (a.Unlocked)
            {
                tile.Add(name);
                return tile;
            }

            var side = new VisualElement { pickingMode = PickingMode.Ignore };
            side.AddToClassList("medal-tile__side");
            side.Add(name);
            var bar = new Meter(a.Progress, a.Target);
            bar.AddToClassList("medal-tile__bar");
            side.Add(bar);
            tile.Add(side);
            return tile;
        }

        /// <summary>The achievement's sheet: the medal large, the goal, the reward, the date or the progress.</summary>
        private void OpenSheet(AchievementInfo a)
        {
            if (_root == null)
            {
                return;
            }

            // The sheet (concept: Achievement sheet): the medal rising over the top edge, the name, the goal,
            // the reward on a sand strip, the date or the progress, and Close.
            var panel = Navigation.Sheet.Open(_root, null);
            panel.AddToClassList("medal-sheet");
            var medal = new VisualElement { pickingMode = PickingMode.Ignore };
            medal.AddToClassList("achievement__medal");
            medal.AddToClassList("medal-sheet__medal");
            MedalArt(medal, a.Id);
            medal.EnableInClassList("medal-sheet__medal--locked", !a.Unlocked);
            panel.Add(medal);
            var title = new Label(AchievementText.Name(a.Id, a.Metric, a.Target));
            title.AddToClassList("sheet__title");
            panel.Add(title);
            panel.Add(Navigation.Sheet.Line(AchievementText.Goal(a.Metric, a.Target), "medal-sheet__goal"));
            panel.Add(Navigation.Sheet.Line(Strings.Format(UiText.AchievementReward, Reward(a)), "medal-sheet__reward"));
            if (a.UnlockedTs is long ts)
            {
                var date = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.ToString(Strings.Text("format.date_long"), Strings.Culture);
                panel.Add(Navigation.Sheet.Line(Strings.Format(UiText.UnlockedOn, date), "medal-sheet__date"));
            }
            else
            {
                var bar = new Meter(a.Progress, a.Target);
                bar.AddToClassList("medal-sheet__bar");
                panel.Add(bar);
                panel.Add(Navigation.Sheet.Line(Strings.Format(UiText.OfCount, Math.Min(a.Progress, a.Target), a.Target), "medal-sheet__date"));
            }

            Navigation.Sheet.Answers(panel).Add(Navigation.Sheet.Button(UiText.Close, () => Navigation.Sheet.Close(panel)));
        }

        private static string Reward(AchievementInfo a) =>
            a.Title != null ? Strings.Format(UiText.GemsAndTitle, a.Gems, UiText.TitleName(a.Title)) : Strings.Format(UiText.GemsOnly, a.Gems);
    }
}
