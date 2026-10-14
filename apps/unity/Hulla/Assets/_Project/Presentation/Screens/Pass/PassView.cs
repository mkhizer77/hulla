using System;
using System.Collections.Generic;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Screens.Leaderboard;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Pass
{
    /// <summary>
    /// The season pass (GDD 12.9): your tier and XP, the season's end, and the forty tiers with the
    /// free and premium rewards side by side, each claimable once reached.
    /// </summary>
    public sealed class PassView : IDisposable
    {
        private readonly PassViewModel _model;
        private readonly Func<long> _nowMs;
        private readonly Action<long, bool> _claim;
        private readonly VisualElement _list;
        private readonly Label _status;

        public PassView(VisualElement root, PassViewModel model, Func<long> nowMs, Action back, Action<long, bool> claim)
        {
            _model = model;
            _nowMs = nowMs;
            _claim = claim;
            root.Q<Label>("title").text = UiText.SeasonPass;
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
            _list.Clear();
            var info = _model.Info;
            if (_model.ErrorCode != null)
            {
                _status.text = UiText.Error(_model.ErrorCode);
            }
            else if (_model.NoSeason)
            {
                _status.text = UiText.NoSeason;
            }
            else
            {
                _status.text = "";
            }

            if (info == null)
            {
                return;
            }

            var header = new VisualElement();
            header.AddToClassList("panel");
            header.AddToClassList("pass__header");
            _list.Add(header);
            var heading = new Label(SeasonName(info.SeasonName));
            heading.AddToClassList("title");
            heading.AddToClassList("title--small");
            header.Add(heading);
            var line = info.Tier >= info.Tiers.Count
                ? Strings.Format(UiText.PassComplete, info.Tiers.Count)
                : Strings.Format(UiText.PassProgress, info.Tier, info.Tiers.Count, info.IntoTier, info.XpPerTier);
            line += " · " + Strings.Format(UiText.SeasonEndsIn, LeaderboardView.Duration(info.EndTs - _nowMs()));
            if (info.CatchUp)
            {
                line += " · " + UiText.DoubleXp;
            }

            var progress = new Label(line);
            progress.AddToClassList("status");
            header.Add(progress);
            header.Add(new Meter(info.Tier >= info.Tiers.Count ? 1 : info.IntoTier, info.Tier >= info.Tiers.Count ? 1 : info.XpPerTier));
            if (!info.Premium)
            {
                var locked = new Label(UiText.PremiumLocked);
                locked.AddToClassList("caption");
                locked.style.whiteSpace = WhiteSpace.Normal;
                header.Add(locked);
            }

            foreach (var tier in info.Tiers)
            {
                _list.Add(Row(tier, info.Premium));
            }
        }

        private VisualElement Row(PassTier tier, bool premium)
        {
            var row = new VisualElement();
            row.AddToClassList("pass__row");
            if (!tier.Reached)
            {
                row.AddToClassList("pass__row--ahead");
            }

            var number = new Label(tier.Tier.ToString());
            number.AddToClassList("pass__tier");
            row.Add(number);
            row.Add(Side(tier, tier.Free, false, true));
            row.Add(Side(tier, tier.Premium, true, premium));
            return row;
        }

        private VisualElement Side(PassTier tier, PassReward reward, bool premiumTrack, bool unlocked)
        {
            var side = new VisualElement();
            side.AddToClassList("pass__side");
            if (premiumTrack)
            {
                side.AddToClassList("pass__side--premium");
            }

            var text = new Label(reward.Empty ? "" : Describe(reward));
            text.AddToClassList("pass__reward");
            side.Add(text);
            if (reward.Empty)
            {
                return side;
            }

            if (reward.Claimed)
            {
                var done = new Label(UiText.Claimed);
                done.AddToClassList("tag--ok");
                side.Add(done);
            }
            else if (tier.Reached && unlocked)
            {
                var claim = new Button(() => _claim(tier.Tier, premiumTrack)) { text = UiText.Claim };
                claim.AddToClassList("chip");
                claim.AddToClassList("chip--on");
                claim.SetEnabled(!_model.Busy);
                side.Add(claim);
            }
            else if (premiumTrack && !unlocked)
            {
                var lockTag = new Label(UiText.Locked);
                lockTag.AddToClassList("caption");
                side.Add(lockTag);
            }

            return side;
        }

        /// <summary>"60 coins", "5 gems", "Kites" or a combination.</summary>
        public static string Describe(PassReward reward)
        {
            var parts = new List<string>();
            if (reward.Coins > 0)
            {
                parts.Add(Strings.Format(UiText.CoinsOnly, reward.Coins));
            }

            if (reward.Gems > 0)
            {
                parts.Add(Strings.Format(UiText.GemsOnly, reward.Gems));
            }

            foreach (var item in reward.Items)
            {
                parts.Add(ItemText.Name(item));
            }

            if (reward.Title != null)
            {
                parts.Add(Strings.Format(UiText.TitleReward, UiText.TitleName(reward.Title)));
            }

            return string.Join(" + ", parts);
        }

        /// <summary>"season.basant_2026" → "Basant 2026" until the string tables land (#65).</summary>
        public static string SeasonName(string key)
        {
            var name = key.StartsWith("season.") ? key.Substring("season.".Length) : key;
            return UiText.TitleName(name);
        }
    }
}
