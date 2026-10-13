using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Progress
{
    public sealed class LeaderboardRow
    {
        public LeaderboardRow(long rank, string userId, string name, long points, long rounds)
        {
            Rank = rank;
            UserId = userId;
            Name = name;
            Points = points;
            Rounds = rounds;
        }

        public long Rank { get; }

        public string UserId { get; }

        public string Name { get; }

        public long Points { get; }

        public long Rounds { get; }
    }

    public sealed class LeaderboardPage
    {
        public LeaderboardPage(IReadOnlyList<LeaderboardRow> rows, LeaderboardRow? me, long? nextResetTs)
        {
            Rows = rows;
            Me = me;
            NextResetTs = nextResetTs;
        }

        public IReadOnlyList<LeaderboardRow> Rows { get; }

        /// <summary>The player's own row, also when it is not on this page; null before their first points.</summary>
        public LeaderboardRow? Me { get; }

        public long? NextResetTs { get; }
    }

    public enum LeaderboardPeriod
    {
        Weekly,
        Monthly,
    }

    /// <summary>leaderboard_get: Baithak points this week or month, for everyone or friends (GDD 12.8).</summary>
    public interface ILeaderboardService
    {
        Awaitable<Result<LeaderboardPage>> BoardAsync(LeaderboardPeriod period, bool friends, CancellationToken cancellation);
    }
}
