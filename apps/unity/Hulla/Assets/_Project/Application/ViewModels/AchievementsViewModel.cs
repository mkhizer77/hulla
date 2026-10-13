using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The achievements list (GDD 12.7), unlocked first within each category.</summary>
    public sealed class AchievementsViewModel
    {
        private static readonly string[] Order = { "play", "skill", "social", "collection", "seasonal" };
        private readonly IAchievementsService _service;

        public AchievementsViewModel(IAchievementsService service)
        {
            _service = service;
        }

        public event Action? Changed;

        public IReadOnlyList<(string Category, IReadOnlyList<AchievementInfo> Items)> Groups { get; private set; } = Array.Empty<(string, IReadOnlyList<AchievementInfo>)>();

        public int UnlockedCount { get; private set; }

        public int Total { get; private set; }

        /// <summary>Ids that unlocked on the last refresh, for a toast.</summary>
        public IReadOnlyList<string> UnlockedNow { get; private set; } = Array.Empty<string>();

        public string? ErrorCode { get; private set; }

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            var answer = await _service.AchievementsAsync(cancellation);
            if (!answer.IsOk)
            {
                ErrorCode = answer.Error.Code;
                Changed?.Invoke();
                return;
            }

            ErrorCode = null;
            var (list, now) = answer.Value;
            var groups = new List<(string, IReadOnlyList<AchievementInfo>)>();
            foreach (var category in Order)
            {
                var items = new List<AchievementInfo>();
                foreach (var a in list)
                {
                    if (a.Category == category)
                    {
                        items.Add(a);
                    }
                }

                // Stable: unlocked first, then closest to done.
                items.Sort((x, y) =>
                {
                    if (x.Unlocked != y.Unlocked)
                    {
                        return x.Unlocked ? -1 : 1;
                    }

                    return Fraction(y).CompareTo(Fraction(x));
                });
                if (items.Count > 0)
                {
                    groups.Add((category, items));
                }
            }

            var unlocked = 0;
            foreach (var a in list)
            {
                unlocked += a.Unlocked ? 1 : 0;
            }

            Groups = groups;
            UnlockedCount = unlocked;
            Total = list.Count;
            UnlockedNow = now;
            Changed?.Invoke();
        }

        private static double Fraction(AchievementInfo a) => a.Target <= 0 ? 0 : (double)a.Progress / a.Target;
    }
}
