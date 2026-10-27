using System;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using Hulla.Presentation.Theme;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Home
{
    /// <summary>
    /// The daily reward card on Home: the gift, "Daily Reward" and the day of the 7-day calendar. While
    /// today's reward waits the card glows and a tap claims it; the amount is in the card's tooltip.
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
            TextArt.Mark(root.Q<Label>("daily-title"), "daily-reward").text = UiText.DailyReward;
            _claim = root.Q<Button>("daily-claim");
            _claim.text = "";
            _claim.tooltip = UiText.Claim;
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

            _text.text = Strings.Format(UiText.DailyDay, status.Today.Day);
            _root.EnableInClassList("home__daily--ready", status.Claimable);
            if (status.Claimable)
            {
                _root.tooltip = Strings.Format(UiText.DailyReady, status.Today.Day, Amount(status.Today));
                _claim.style.display = DisplayStyle.Flex;
                _claim.SetEnabled(!_model.Claiming);
                return;
            }

            _claim.style.display = DisplayStyle.None;
            var tomorrow = Strings.Format(UiText.DailyTomorrow, Amount(status.Next));
            _root.tooltip = _model.Claimed != null ? Strings.Format(UiText.DailyClaimed, Amount(_model.Claimed)) + " " + tomorrow : tomorrow;
        }

        /// <summary>"70 coins" or "80 coins + 2 gems".</summary>
        public static string Amount(DailyRewardDay day) =>
            day.Gems > 0 ? Strings.Format(UiText.CoinsAndGems, day.Coins, day.Gems) : Strings.Format(UiText.CoinsOnly, day.Coins);
    }
}
