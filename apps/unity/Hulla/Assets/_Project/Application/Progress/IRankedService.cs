using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Progress
{
    public sealed class RankedLeague
    {
        public RankedLeague(string id, int minRating, bool topOnly, string rewardTitle, long rewardCoins, bool rewardCardBack, bool rewardFrame)
        {
            Id = id;
            MinRating = minRating;
            TopOnly = topOnly;
            RewardTitle = rewardTitle;
            RewardCoins = rewardCoins;
            RewardCardBack = rewardCardBack;
            RewardFrame = rewardFrame;
        }

        public string Id { get; }

        public int MinRating { get; }

        /// <summary>Ustad: held only inside the season board's top places.</summary>
        public bool TopOnly { get; }

        public string RewardTitle { get; }

        public long RewardCoins { get; }

        public bool RewardCardBack { get; }

        public bool RewardFrame { get; }
    }

    public sealed class RankedSeasonResult
    {
        public RankedSeasonResult(string seasonId, string? league, long? rank)
        {
            SeasonId = seasonId;
            League = league;
            Rank = rank;
        }

        public string SeasonId { get; }

        /// <summary>Null when placement was not finished.</summary>
        public string? League { get; }

        public long? Rank { get; }
    }

    /// <summary>ranked_get: the player's season card (GDD 7.5, 12.9).</summary>
    public sealed class RankedCard
    {
        public RankedCard(
            int seasonNumber,
            long seasonEndTs,
            int rating,
            string? league,
            long? rank,
            int placementPlayed,
            int placementOf,
            int protectedRounds,
            IReadOnlyList<RankedLeague> leagues,
            int band,
            int widenBy,
            int maxBand,
            RankedSeasonResult? lastSeason,
            bool seasonSettled)
        {
            SeasonNumber = seasonNumber;
            SeasonEndTs = seasonEndTs;
            Rating = rating;
            League = league;
            Rank = rank;
            PlacementPlayed = placementPlayed;
            PlacementOf = placementOf;
            ProtectedRounds = protectedRounds;
            Leagues = leagues;
            Band = band;
            WidenBy = widenBy;
            MaxBand = maxBand;
            LastSeason = lastSeason;
            SeasonSettled = seasonSettled;
        }

        public int SeasonNumber { get; }

        public long SeasonEndTs { get; }

        public int Rating { get; }

        /// <summary>Null during the placement rounds.</summary>
        public string? League { get; }

        public long? Rank { get; }

        public int PlacementPlayed { get; }

        public int PlacementOf { get; }

        public bool Placed => PlacementPlayed >= PlacementOf;

        public int ProtectedRounds { get; }

        public IReadOnlyList<RankedLeague> Leagues { get; }

        public int Band { get; }

        public int WidenBy { get; }

        public int MaxBand { get; }

        public RankedSeasonResult? LastSeason { get; }

        /// <summary>The last season ended and was paid out on this very call.</summary>
        public bool SeasonSettled { get; }
    }

    public sealed class RankedRow
    {
        public RankedRow(long rank, string userId, string name, int rating, string? league)
        {
            Rank = rank;
            UserId = userId;
            Name = name;
            Rating = rating;
            League = league;
        }

        public long Rank { get; }

        public string UserId { get; }

        public string Name { get; }

        public int Rating { get; }

        public string? League { get; }
    }

    public interface IRankedService
    {
        Awaitable<Result<RankedCard>> CardAsync(CancellationToken cancellation);

        Awaitable<Result<IReadOnlyList<RankedRow>>> BoardAsync(CancellationToken cancellation);
    }
}
