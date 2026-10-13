using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Progress
{
    public sealed class AchievementInfo
    {
        public AchievementInfo(string id, string category, string metric, long target, long progress, long? unlockedTs, long gems, string? title)
        {
            Id = id;
            Category = category;
            Metric = metric;
            Target = target;
            Progress = progress;
            UnlockedTs = unlockedTs;
            Gems = gems;
            Title = title;
        }

        public string Id { get; }

        /// <summary>play, social, skill, collection or seasonal.</summary>
        public string Category { get; }

        public string Metric { get; }

        public long Target { get; }

        public long Progress { get; }

        public long? UnlockedTs { get; }

        public bool Unlocked => UnlockedTs != null;

        public long Gems { get; }

        /// <summary>The title it grants, as a key; null when it grants none.</summary>
        public string? Title { get; }
    }

    /// <summary>achievements_get: the list with progress, and what unlocked on this call (GDD 12.7).</summary>
    public interface IAchievementsService
    {
        Awaitable<Result<(IReadOnlyList<AchievementInfo> Achievements, IReadOnlyList<string> UnlockedNow)>> AchievementsAsync(CancellationToken cancellation);
    }
}
