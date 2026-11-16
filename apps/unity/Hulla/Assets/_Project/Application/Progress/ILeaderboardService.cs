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

    /// <summary>Which game's board: each game keeps its own, and All ranks by coins earned in either.</summary>
    public enum GameFilter
    {
        All,
        Thulla,
        Rung,
    }

    public static class GameFilters
    {
        /// <summary>The server's name for the filter in leaderboard_get.</summary>
        public static string Wire(GameFilter game) => game == GameFilter.Rung ? "rung" : game == GameFilter.All ? "all" : "thulla";

        /// <summary>The game a mission or achievement belongs to, from its metric; null when it counts in both.</summary>
        public static GameFilter? OfMetric(string metric)
        {
            if (metric.StartsWith("rung_", System.StringComparison.Ordinal))
            {
                return GameFilter.Rung;
            }

            return metric == "rounds_played" || metric == "rounds_with_friend" || metric == "friends" || metric == "cosmetics_owned" || metric == "voice_private" || metric == "rounds_with_four_friends"
                ? (GameFilter?)null
                : GameFilter.Thulla;
        }

        /// <summary>Whether something of <paramref name="of"/> shows under the chosen filter; things for both games show everywhere.</summary>
        public static bool Shows(GameFilter chosen, GameFilter? of) => chosen == GameFilter.All || of == null || of == chosen;
    }

    /// <summary>leaderboard_get: points this week or month on one game's board, for everyone or friends (GDD 12.8).</summary>
    public interface ILeaderboardService
    {
        Awaitable<Result<LeaderboardPage>> BoardAsync(LeaderboardPeriod period, bool friends, CancellationToken cancellation);

        Awaitable<Result<LeaderboardPage>> BoardAsync(LeaderboardPeriod period, bool friends, GameFilter game, CancellationToken cancellation) => BoardAsync(period, friends, cancellation);
    }
}
