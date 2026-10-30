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
            _status.text = "";
            _list.Clear();
            if (_model.ErrorCode != null)
            {
                ListState.Error(_list, _model.ErrorCode, Retry);
                return;
            }

            if (_model.Daily.Count + _model.Weekly.Count == 0)
            {
                ListState.Empty(_list, UiText.NoMissionsYet);
                return;
            }

            var now = DateTime.Now;
            var hours = Math.Max(1, (int)Math.Ceiling((now.Date.AddDays(1) - now).TotalHours));
            var days = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
            Section(Strings.Format(UiText.MissionsTodayResets, hours), _model.Daily, true);
            Section(Strings.Format(UiText.MissionsWeekLeft, days == 0 ? 7 : days), _model.Weekly, false);
        }

        /// <summary>Loads the missions again after an error; null hides Retry.</summary>
        public Action? Retry { get; set; }

        private bool _swapping;

        /// <summary>
        /// A heading and its mission plates (design: Trophies · Missions): title and reward on top, the bar
        /// and the count under them; a finished one wears the yellow Claim. Today's heading carries the one
        /// free swap: tap it, then the mission to swap.
        /// </summary>
        private void Section(string title, IReadOnlyList<MissionInfo> missions, bool today)
        {
            var head = new VisualElement();
            head.AddToClassList("missions__head");
            var heading = new Label(title);
            heading.AddToClassList("caption");
            heading.AddToClassList("settings__heading");
            heading.AddToClassList("missions__heading");
            head.Add(heading);
            if (today)
            {
                var swap = new Button(() =>
                {
                    _swapping = !_swapping;
                    Refresh();
                }) { text = _model.RerollAvailable ? UiText.SwapOne : UiText.Swapped };
                swap.AddToClassList("chip");
                swap.AddToClassList("missions__swap");
                swap.EnableInClassList("chip--on", _swapping);
                swap.SetEnabled(_model.RerollAvailable);
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("missions__swap-icon");
                swap.Insert(0, icon);
                head.Add(swap);
            }

            _list.Add(head);
            foreach (var mission in missions)
            {
                var plate = new VisualElement();
                plate.AddToClassList("lobby-seat");
                plate.AddToClassList("mission");
                var top = new VisualElement();
                top.AddToClassList("mission__top");
                var name = new Label(UiText.MissionText(mission.Metric, mission.Target));
                name.AddToClassList("mission__name");
                top.Add(name);
                var reward = new Label(mission.Gems > 0 ? Strings.Format(UiText.PlusRewardGems, mission.Coins, mission.Gems) : Strings.Format(UiText.PlusReward, mission.Coins));
                reward.AddToClassList("mission__reward");
                top.Add(reward);
                plate.Add(top);
                var bottom = new VisualElement();
                bottom.AddToClassList("mission__bottom");
                var meter = new Meter(mission.Progress, mission.Target);
                meter.AddToClassList("mission__meter");
                bottom.Add(meter);
                if (mission.Claimed)
                {
                    var done = new Label(UiText.Claimed);
                    done.AddToClassList("tag--ok");
                    bottom.Add(done);
                }
                else if (mission.Claimable)
                {
                    var id = mission.Id;
                    var claim = new Button(() => _claim(id)) { text = Strings.Format(UiText.ClaimCoins, mission.Coins) };
                    claim.AddToClassList("chip");
                    claim.AddToClassList("chip--on");
                    claim.AddToClassList("mission__claim");
                    bottom.Add(claim);
                }
                else if (_swapping && today && _model.RerollAvailable)
                {
                    var id = mission.Id;
                    var swap = new Button(() =>
                    {
                        _swapping = false;
                        _reroll(id);
                    }) { text = UiText.Swap };
                    swap.AddToClassList("chip");
                    bottom.Add(swap);
                }
                else
                {
                    var count = new Label(Strings.Format(UiText.OfCount, Math.Min(mission.Progress, mission.Target), mission.Target));
                    count.AddToClassList("mission__count");
                    bottom.Add(count);
                }

                plate.Add(bottom);
                _list.Add(plate);
            }
        }

        private static string Reward(MissionInfo mission) =>
            mission.Gems > 0 ? Strings.Format(UiText.RewardGems, mission.Coins, mission.Gems) : Strings.Format(UiText.RewardCoins, mission.Coins);
    }
}
