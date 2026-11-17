using System;
using Hulla.Application.Table;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Games.Shared
{
    /// <summary>
    /// What a result shows once the server has paid the round (GDD 14.8), for every game: the reward
    /// line, the 2x coins offer (GDD 13.6), the level bar, and "Add friend" chips for co-players who
    /// are not friends yet (GDD 10.3).
    /// </summary>
    public sealed class RoundRewards
    {
        private readonly Label _reward;
        private readonly Button _double;
        private readonly VisualElement _level;
        private readonly VisualElement _levelFill;
        private readonly Label _levelText;
        private Action? _onDouble;
        private bool _collapse;

        /// <summary>On a layout that already has the reward line and the level bar; the 2x button goes between them.</summary>
        public RoundRewards(Label reward, VisualElement level, VisualElement levelFill, Label levelText, string doubleName = "result-double")
        {
            _reward = reward;
            _level = level;
            _levelFill = levelFill;
            _levelText = levelText;
            _double = new Button(() => _onDouble?.Invoke()) { name = doubleName };
            _double.AddToClassList("results__double");
            _double.style.display = DisplayStyle.None;
            _reward.parent.Insert(_reward.parent.IndexOf(_reward) + 1, _double);
            _level.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// The same three rows built into a result that has none, in the shared results styles; the names
        /// carry <paramref name="prefix"/>, since the shared table layout already holds Thulla's.
        /// </summary>
        public static RoundRewards AddTo(VisualElement parent, string prefix)
        {
            var reward = new Label { name = prefix + "result-reward" };
            reward.AddToClassList("turn");
            reward.AddToClassList("result-reward");
            var level = new VisualElement { name = prefix + "result-level" };
            level.AddToClassList("results__level");
            var fill = new VisualElement { name = prefix + "result-level-fill" };
            fill.AddToClassList("results__level-fill");
            var text = new Label { name = prefix + "result-level-text" };
            text.AddToClassList("results__level-text");
            level.Add(fill);
            level.Add(text);
            parent.Add(reward);
            parent.Add(level);
            var rewards = new RoundRewards(reward, level, fill, text, prefix + "result-double") { _collapse = true };
            rewards.ShowReward(null);
            return rewards;
        }

        /// <summary>The reward line as shown; empty until the round is paid. Read by tests.</summary>
        public string RewardText => _reward.text;

        /// <summary>The round's XP, coins and any level-up, once the server has paid it.</summary>
        public void ShowReward(string? text)
        {
            _reward.text = text ?? "";
            if (_collapse)
            {
                // A built row takes no room while it is empty; a layout's own row keeps its place.
                _reward.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>The level bar under the reward line: the level reached and how far into the next.</summary>
        public void ShowLevel(long level, float fraction)
        {
            _level.style.display = DisplayStyle.Flex;
            _levelFill.style.width = Length.Percent(Math.Clamp(fraction, 0.04f, 1f) * 100f);
            _levelText.text = Strings.Format(UiText.LevelN, level);
        }

        /// <summary>Offers 2x coins for a rewarded ad; a null text hides it, a null action shows it waiting.</summary>
        public void OfferDouble(string? text, Action? onTap)
        {
            _double.style.display = text == null ? DisplayStyle.None : DisplayStyle.Flex;
            _double.text = text ?? "";
            _onDouble = onTap;
            _double.SetEnabled(onTap != null);
        }

        /// <summary>A new round's result starts empty: the last round's reward and offer go.</summary>
        public void Clear()
        {
            ShowReward(null);
            OfferDouble(null, null);
            _level.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// "New here:" and an Add chip for each co-player <paramref name="canAdd"/> allows; null when there is
        /// nobody to add.
        /// </summary>
        public static VisualElement? FriendChips(TableModel model, Func<string, bool>? canAdd, Action<string>? addFriend)
        {
            if (canAdd == null || addFriend == null)
            {
                return null;
            }

            var row = new VisualElement { name = "result-friends" };
            row.AddToClassList("chips");
            foreach (var seat in model.Seats)
            {
                if (!seat.Occupied || seat.Bot || seat.Seat == model.MySeat || seat.PlayerId == null || !canAdd(seat.PlayerId))
                {
                    continue;
                }

                var id = seat.PlayerId;
                var chip = new Button { text = Strings.Format(UiText.AddNamePlus, seat.DisplayName ?? "?") };
                chip.AddToClassList("chip");
                chip.clicked += () =>
                {
                    addFriend(id);
                    chip.SetEnabled(false);
                    chip.text = UiText.RequestSent;
                };
                row.Add(chip);
            }

            if (row.childCount == 0)
            {
                return null;
            }

            // "New here:" and the chips on one line (design: Results).
            var label = new Label(UiText.NewHere);
            label.AddToClassList("results__new-here");
            row.Insert(0, label);
            row.AddToClassList("results__friends");
            return row;
        }
    }
}
