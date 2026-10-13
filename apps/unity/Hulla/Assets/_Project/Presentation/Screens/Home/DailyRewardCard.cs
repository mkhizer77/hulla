using System;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Home
{
    /// <summary>The daily reward strip on Home: today's reward and Claim, then what tomorrow brings.</summary>
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
            _claim.text = UiText.Claim;
            _claim.clicked += claim;
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            var status = _model.Status;
            _root.style.display = status == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (status == null)
            {
                return;
            }

            if (status.Claimable)
            {
                _text.text = string.Format(UiText.DailyReady, status.Today.Day, Amount(status.Today));
                _claim.style.display = DisplayStyle.Flex;
                _claim.SetEnabled(!_model.Claiming);
                return;
            }

            _claim.style.display = DisplayStyle.None;
            var tomorrow = string.Format(UiText.DailyTomorrow, Amount(status.Next));
            _text.text = _model.Claimed != null ? string.Format(UiText.DailyClaimed, Amount(_model.Claimed)) + " " + tomorrow : tomorrow;
        }

        /// <summary>"70 coins" or "80 coins + 2 gems".</summary>
        public static string Amount(DailyRewardDay day) =>
            day.Gems > 0 ? string.Format(UiText.CoinsAndGems, day.Coins, day.Gems) : string.Format(UiText.CoinsOnly, day.Coins);
    }
}
