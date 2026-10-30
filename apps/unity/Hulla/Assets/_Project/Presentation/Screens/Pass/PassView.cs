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

        /// <summary>
        /// The pass (design: Pass): the top plate with the season, days left and the bar to the next
        /// tier; the track scrolls sideways with the current tier lit, free above and premium below;
        /// a reached tier's small yellow Claim, a tick once claimed, a lock on premium. Between
        /// seasons the plate says so.
        /// </summary>
        private void Refresh()
        {
            _list.Clear();
            var info = _model.Info;
            _status.text = _model.ErrorCode != null ? UiText.Error(_model.ErrorCode) : "";
            if (info == null)
            {
                if (_model.NoSeason)
                {
                    var plate = new VisualElement();
                    plate.AddToClassList("panel");
                    plate.AddToClassList("pass__plate");
                    var line = new Label(UiText.NoSeason);
                    line.AddToClassList("pass__empty");
                    plate.Add(line);
                    _list.Add(plate);
                }

                return;
            }

            var card = new VisualElement();
            card.AddToClassList("panel");
            card.AddToClassList("pass__plate");
            var top = new VisualElement();
            top.AddToClassList("pass__plate-row");
            var season = new Label(SeasonName(info.SeasonName));
            season.AddToClassList("pass__season");
            top.Add(season);
            var days = Math.Max(0, (int)Math.Ceiling((info.EndTs - _nowMs()) / 86400000.0));
            var left = new Label(Strings.Format(UiText.DaysLeft, days));
            left.AddToClassList("pass__days");
            top.Add(left);
            card.Add(top);
            var complete = info.Tier >= info.Tiers.Count;
            var row = new VisualElement();
            row.AddToClassList("pass__plate-row");
            var tierLine = new Label(complete ? Strings.Format(UiText.PassComplete, info.Tiers.Count) : Strings.Format(UiText.TierNext, info.Tier, info.Tier + 1));
            tierLine.AddToClassList("pass__tierline");
            row.Add(tierLine);
            var xp = new Label(complete ? "" : Strings.Format(UiText.XpOf, info.IntoTier, info.XpPerTier));
            xp.AddToClassList("pass__xp");
            row.Add(xp);
            card.Add(row);
            var meter = new Meter(complete ? 1 : info.IntoTier, complete ? 1 : info.XpPerTier);
            meter.AddToClassList("pass__meter");
            card.Add(meter);
            var track = new ScrollView(ScrollViewMode.Horizontal) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            track.AddToClassList("pass__track");
            VisualElement? current = null;
            foreach (var tier in info.Tiers)
            {
                var column = Column(tier, info.Premium, tier.Tier == info.Tier + 1 || (complete && tier.Tier == info.Tiers.Count));
                if (column.ClassListContains("pass__col--current"))
                {
                    current = column;
                }

                track.Add(column);
            }

            card.Add(track);
            var labels = new VisualElement();
            labels.AddToClassList("pass__plate-row");
            var free = new Label(UiText.FreeTrack);
            free.AddToClassList("pass__track-label");
            labels.Add(free);
            var premium = new Label(UiText.PremiumTrack);
            premium.AddToClassList("pass__track-label");
            premium.AddToClassList("pass__track-label--premium");
            labels.Add(premium);
            card.Add(labels);
            _list.Add(card);
            if (!info.Premium)
            {
                var note = new Label(UiText.PremiumStore);
                note.AddToClassList("pass__note");
                _list.Add(note);
            }

            if (current != null)
            {
                var target = current;
                track.schedule.Execute(() => track.ScrollTo(target)).StartingIn(60);
            }
        }

        private VisualElement Column(PassTier tier, bool premium, bool current)
        {
            var column = new VisualElement();
            column.AddToClassList("pass__col");
            column.EnableInClassList("pass__col--current", current);
            var number = new Label(tier.Tier.ToString());
            number.AddToClassList("pass__num");
            column.Add(number);
            column.Add(Cell(tier, tier.Free, false, true));
            column.Add(Cell(tier, tier.Premium, true, premium));
            return column;
        }

        private VisualElement Cell(PassTier tier, PassReward reward, bool premiumTrack, bool unlocked)
        {
            var cell = new VisualElement();
            cell.AddToClassList("pass__cell");
            cell.EnableInClassList("pass__cell--premium", premiumTrack);
            cell.EnableInClassList("pass__cell--ahead", !tier.Reached);
            var text = new Label(reward.Empty ? "" : Describe(reward));
            text.AddToClassList("pass__reward");
            cell.Add(text);
            if (reward.Empty)
            {
                return cell;
            }

            if (reward.Claimed)
            {
                var tick = new VisualElement { pickingMode = PickingMode.Ignore };
                tick.AddToClassList("pass__tick");
                cell.Add(tick);
            }
            else if (tier.Reached && unlocked)
            {
                var claim = new Button(() => _claim(tier.Tier, premiumTrack)) { text = UiText.Claim };
                claim.AddToClassList("pass__claim");
                claim.SetEnabled(!_model.Busy);
                cell.Add(claim);
            }
            else if (premiumTrack && !unlocked)
            {
                var lockIcon = new VisualElement { pickingMode = PickingMode.Ignore };
                lockIcon.AddToClassList("pass__lock");
                cell.Add(lockIcon);
            }

            return cell;
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
