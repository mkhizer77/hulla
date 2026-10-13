using System;
using System.Collections.Generic;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Missions
{
    /// <summary>Today's three and this week's five (GDD 12.5): progress, the reward, Claim, and the free daily re-roll.</summary>
    public sealed class MissionsView : IDisposable
    {
        private readonly MissionsViewModel _model;
        private readonly VisualElement _list;
        private readonly Label _status;
        private readonly Action<string> _claim;
        private readonly Action<string> _reroll;

        public MissionsView(VisualElement root, MissionsViewModel model, Action back, Action<string> claim, Action<string> reroll)
        {
            _model = model;
            _claim = claim;
            _reroll = reroll;
            root.Q<Label>("title").text = UiText.Missions;
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            _list = root.Q<VisualElement>("list");
            _status = root.Q<Label>("status");
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            _status.text = _model.ErrorCode == null ? "" : UiText.Error(_model.ErrorCode);
            _list.Clear();
            Section(UiText.MissionsToday, _model.Daily, _model.RerollAvailable);
            Section(UiText.MissionsWeek, _model.Weekly, false);
        }

        private void Section(string title, IReadOnlyList<MissionInfo> missions, bool canReroll)
        {
            var heading = new Label(title);
            heading.AddToClassList("caption");
            heading.AddToClassList("settings__heading");
            _list.Add(heading);
            foreach (var mission in missions)
            {
                var row = new VisualElement();
                row.AddToClassList("lobby-seat");
                row.AddToClassList("friend-row");
                var text = new VisualElement();
                text.AddToClassList("lobby-seat__name");
                text.Add(new Label(UiText.MissionText(mission.Metric, mission.Target)));
                var detail = new Label(string.Format(UiText.MissionProgress, Math.Min(mission.Progress, mission.Target), mission.Target, Reward(mission)));
                detail.AddToClassList("caption");
                text.Add(detail);
                row.Add(text);
                if (mission.Claimed)
                {
                    var done = new Label(UiText.Claimed);
                    done.AddToClassList("tag--ok");
                    row.Add(done);
                }
                else if (mission.Claimable)
                {
                    var id = mission.Id;
                    var claim = new Button(() => _claim(id)) { text = UiText.Claim };
                    claim.AddToClassList("chip");
                    claim.AddToClassList("chip--on");
                    row.Add(claim);
                }
                else if (canReroll && mission.Daily)
                {
                    var id = mission.Id;
                    var reroll = new Button(() => _reroll(id)) { text = UiText.Reroll };
                    reroll.AddToClassList("chip");
                    row.Add(reroll);
                }

                _list.Add(row);
            }
        }

        private static string Reward(MissionInfo mission) =>
            mission.Gems > 0 ? string.Format(UiText.RewardGems, mission.Coins, mission.Gems) : string.Format(UiText.RewardCoins, mission.Coins);
    }
}
