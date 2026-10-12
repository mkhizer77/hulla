using System.Collections.Generic;

namespace Hulla.Application.Progress
{
    /// <summary>Level, XP and today's coins (GDD 12.2, 12.3), as progress_get reports them.</summary>
    public sealed class ProgressInfo
    {
        public ProgressInfo(long level, long xp, long intoLevel, long toNext, long coinsToday, long loginStreak, IReadOnlyList<string> titles, RewardInfo? lastReward)
        {
            Level = level;
            Xp = xp;
            IntoLevel = intoLevel;
            ToNext = toNext;
            CoinsToday = coinsToday;
            LoginStreak = loginStreak;
            Titles = titles;
            LastReward = lastReward;
        }

        public long Level { get; }

        public long Xp { get; }

        public long IntoLevel { get; }

        /// <summary>XP the current level needs in all (100 + 25n), so the bar reads IntoLevel of ToNext.</summary>
        public long ToNext { get; }

        public long CoinsToday { get; }

        public long LoginStreak { get; }

        public IReadOnlyList<string> Titles { get; }

        public RewardInfo? LastReward { get; }
    }
}
