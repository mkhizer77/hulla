using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Progress
{
    /// <summary>One side of a tier: what it pays and whether it has been claimed.</summary>
    public sealed class PassReward
    {
        public PassReward(long coins, long gems, IReadOnlyList<string> items, string? title, bool claimed)
        {
            Coins = coins;
            Gems = gems;
            Items = items;
            Title = title;
            Claimed = claimed;
        }

        public long Coins { get; }

        public long Gems { get; }

        /// <summary>Catalog ids granted by this tier.</summary>
        public IReadOnlyList<string> Items { get; }

        public string? Title { get; }

        public bool Claimed { get; }

        public bool Empty => Coins == 0 && Gems == 0 && Items.Count == 0 && Title == null;
    }

    public sealed class PassTier
    {
        public PassTier(long tier, bool reached, PassReward free, PassReward premium)
        {
            Tier = tier;
            Reached = reached;
            Free = free;
            Premium = premium;
        }

        public long Tier { get; }

        public bool Reached { get; }

        public PassReward Free { get; }

        public PassReward Premium { get; }
    }

    /// <summary>The running season and the player's place in its forty tiers (GDD 12.9).</summary>
    public sealed class PassInfo
    {
        public PassInfo(string seasonName, long endTs, long xpPerTier, long xp, long tier, long intoTier, bool premium, bool catchUp, IReadOnlyList<PassTier> tiers)
        {
            SeasonName = seasonName;
            EndTs = endTs;
            XpPerTier = xpPerTier;
            Xp = xp;
            Tier = tier;
            IntoTier = intoTier;
            Premium = premium;
            CatchUp = catchUp;
            Tiers = tiers;
        }

        /// <summary>A string key such as "season.basant_2026".</summary>
        public string SeasonName { get; }

        public long EndTs { get; }

        public long XpPerTier { get; }

        public long Xp { get; }

        public long Tier { get; }

        public long IntoTier { get; }

        public bool Premium { get; }

        /// <summary>The last days of the season, when pass XP counts double.</summary>
        public bool CatchUp { get; }

        public IReadOnlyList<PassTier> Tiers { get; }
    }

    /// <summary>pass_get and pass_claim. Premium opens only through the server until receipts exist (#58).</summary>
    public interface IPassService
    {
        /// <returns>Null when no season is running.</returns>
        Awaitable<Result<PassInfo?>> PassAsync(CancellationToken cancellation);

        Awaitable<Result<bool>> ClaimAsync(long tier, bool premium, CancellationToken cancellation);
    }
}
