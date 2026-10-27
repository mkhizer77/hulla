using System;
using System.Collections.Generic;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Home
{
    /// <summary>
    /// The reward chest on Home: the chest sits on its Daily Reward board and glows while today's reward
    /// waits; a tap on the chest or the board claims it. Once claimed the chest pops open, gold coins arc
    /// from it into the potli one after another, and the potli's count ticks up with each one until it
    /// shows the new balance. Under reduce motion the chest just opens and the count jumps.
    /// </summary>
    public sealed class DailyRewardCard : IDisposable
    {
        public const int FlightMs = 620;
        public const int StaggerMs = 70;
        public const int PopMs = 200;

        private readonly DailyRewardViewModel _model;
        private readonly VisualElement _root;
        private readonly Label _text;
        private readonly Button _claim;
        private readonly VisualElement? _chest;
        private readonly VisualElement? _stage;
        private readonly VisualElement? _pouch;
        private readonly Label? _coins;
        private readonly bool _reduceMotion;
        private bool _celebrated;

        /// <param name="stage">The screen the coins fly over; with pouch and coins, null skips the flight.</param>
        /// <param name="pouch">Where the coins land.</param>
        /// <param name="coins">The balance on the pouch, ticked up as the coins land.</param>
        public DailyRewardCard(VisualElement root, DailyRewardViewModel model, Action claim, VisualElement? stage = null, VisualElement? pouch = null, Label? coins = null, bool reduceMotion = false)
        {
            _root = root;
            _model = model;
            _stage = stage;
            _pouch = pouch;
            _coins = coins;
            _reduceMotion = reduceMotion;
            _text = root.Q<Label>("daily-text");
            _claim = root.Q<Button>("daily-claim");
            _claim.text = UiText.DailyReward;
            _chest = root.Q<VisualElement>("daily-chest");
            void Claim()
            {
                if (_model.Status?.Claimable == true && !_model.Claiming)
                {
                    claim();
                }
            }

            _claim.clicked += Claim;
            _chest?.RegisterCallback<ClickEvent>(_ => Claim());
            _celebrated = _model.Claimed != null;
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            var status = _model.Status;
            _root.EnableInClassList("daily--ready", status?.Claimable == true && !_model.Claiming);
            _root.EnableInClassList("daily--claimed", _model.Claimed != null);
            if (_model.Claimed != null && !_celebrated)
            {
                _celebrated = true;
                Celebrate(_model.Claimed.Coins);
            }

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

        /// <summary>The chest pops open, then the coins fly to the potli and the count ticks up.</summary>
        private void Celebrate(long amount)
        {
            if (_chest != null && !_reduceMotion)
            {
                _chest.AddToClassList("daily__chest--pop");
                _chest.schedule.Execute(() => _chest.RemoveFromClassList("daily__chest--pop")).StartingIn(PopMs);
            }

            if (_stage == null || _pouch == null || _coins == null || _chest == null || amount <= 0)
            {
                return;
            }

            var counter = _coins;
            var pouch = _pouch;
            var start = Balance(counter.text);
            var end = start + amount;
            if (_reduceMotion)
            {
                counter.text = end.ToString("N0");
                return;
            }

            var from = _stage.WorldToLocal(_chest.worldBound.center);
            var to = _stage.WorldToLocal(pouch.worldBound.center);
            var count = (int)Math.Clamp(amount / 10, 5, 10);
            var flying = new List<VisualElement>();
            for (var i = 0; i < count; i++)
            {
                var coin = new VisualElement { pickingMode = PickingMode.Ignore };
                coin.AddToClassList("coin-fly");
                coin.style.left = from.x;
                coin.style.top = from.y;
                coin.style.opacity = 0f;
                _stage.Add(coin);
                flying.Add(coin);
            }

            var startedMs = Environment.TickCount;
            var landed = 0;
            var lift = new Vector2((from.x + to.x) * 0.5f, Mathf.Min(from.y, to.y) - 260f);
            _stage.schedule.Execute(() =>
            {
                var elapsed = Environment.TickCount - startedMs;
                for (var i = 0; i < flying.Count; i++)
                {
                    var coin = flying[i];
                    if (coin.parent == null)
                    {
                        continue;
                    }

                    var t = (elapsed - i * StaggerMs) / (float)FlightMs;
                    if (t < 0f)
                    {
                        continue;
                    }

                    if (t >= 1f)
                    {
                        coin.RemoveFromHierarchy();
                        landed++;
                        counter.text = (start + amount * landed / count).ToString("N0");
                        pouch.AddToClassList("home__pouch--bump");
                        pouch.schedule.Execute(() => pouch.RemoveFromClassList("home__pouch--bump")).StartingIn(90);
                        continue;
                    }

                    // Ease out along a curve that rises over the screen and drops into the potli.
                    var k = 1f - (1f - t) * (1f - t);
                    var at = Vector2.Lerp(Vector2.Lerp(from, lift, k), Vector2.Lerp(lift, to, k), k);
                    coin.style.opacity = 1f;
                    coin.style.translate = new Translate(at.x - from.x, at.y - from.y);
                    coin.style.scale = new Scale(Vector2.one * (1f - 0.35f * k));
                }

                if (landed == count)
                {
                    counter.text = end.ToString("N0");
                }
            }).Every(16).Until(() => landed == count);
        }

        /// <summary>The number on the potli, ignoring separators: "1,240" → 1240.</summary>
        public static long Balance(string text)
        {
            long value = 0;
            foreach (var c in text)
            {
                if (c >= '0' && c <= '9')
                {
                    value = value * 10 + (c - '0');
                }
            }

            return value;
        }

        /// <summary>"70 coins" or "80 coins + 2 gems".</summary>
        public static string Amount(DailyRewardDay day) =>
            day.Gems > 0 ? Strings.Format(UiText.CoinsAndGems, day.Coins, day.Gems) : Strings.Format(UiText.CoinsOnly, day.Coins);
    }
}
