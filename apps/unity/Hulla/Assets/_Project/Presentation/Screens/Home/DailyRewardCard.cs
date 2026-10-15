using System;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Home
{
    /// <summary>
    /// The reward chest on Home: the chest glows while today's reward waits and a tap on it or on the
    /// Daily Reward board claims it; the line under them says what today or tomorrow brings.
    /// </summary>
    public sealed class DailyRewardCard : IDisposable
    {
        private readonly DailyRewardViewModel _model;
        private readonly VisualElement _root;
        private readonly Label _text;
        private readonly Button _claim;

        public DailyRewardCard(VisualElement root, DailyRewardViewModel model, Action claim)
        {
            _root = root;
            _model = model;
            _text = root.Q<Label>("daily-text");
            _claim = root.Q<Button>("daily-claim");
            _claim.text = UiText.DailyReward;
            _claim.clicked += () =>
            {
                if (_model.Status?.Claimable == true && !_model.Claiming)
                {
                    claim();
                }
            };
            root.Q<VisualElement>("daily-chest")?.RegisterCallback<ClickEvent>(_ =>
            {
                if (_model.Status?.Claimable == true && !_model.Claiming)
                {
                    claim();
                }
            });
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            var status = _model.Status;
            _root.EnableInClassList("daily--ready", status?.Claimable == true && !_model.Claiming);
            _root.EnableInClassList("daily--claimed", _model.Claimed != null);
            if (status == null)
            {
                _text.text = "";
                return;
            }

            if (status.Claimable)
            {
                _text.text = Strings.Format(UiText.DailyReady, status.Today.Day, Amount(status.Today));
                return;
            }

            var tomorrow = Strings.Format(UiText.DailyTomorrow, Amount(status.Next));
            _text.text = _model.Claimed != null ? Strings.Format(UiText.DailyClaimed, Amount(_model.Claimed)) + " " + tomorrow : tomorrow;
        }

        /// <summary>"70 coins" or "80 coins + 2 gems".</summary>
        public static string Amount(DailyRewardDay day) =>
            day.Gems > 0 ? Strings.Format(UiText.CoinsAndGems, day.Coins, day.Gems) : Strings.Format(UiText.CoinsOnly, day.Coins);
    }
}
