using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Progress
{
    /// <summary>One day of the 7-day calendar (GDD 12.6).</summary>
    public sealed class DailyRewardDay
    {
        public DailyRewardDay(long day, long coins, long gems)
        {
            Day = day;
            Coins = coins;
            Gems = gems;
        }

        public long Day { get; }

        public long Coins { get; }

        public long Gems { get; }
    }

    /// <summary>Whether today's reward is waiting, what it is, and what tomorrow brings.</summary>
    public sealed class DailyRewardStatus
    {
        public DailyRewardStatus(bool claimable, DailyRewardDay today, DailyRewardDay next)
        {
            Claimable = claimable;
            Today = today;
            Next = next;
        }

        public bool Claimable { get; }

        public DailyRewardDay Today { get; }

        public DailyRewardDay Next { get; }
    }

    /// <summary>daily_reward_get and daily_reward_claim; the server pays through the ledger, once a local day.</summary>
    public interface IDailyRewardService
    {
        Awaitable<Result<DailyRewardStatus>> StatusAsync(int tzOffsetMinutes, CancellationToken cancellation);

        /// <returns>The status after the claim (a repeat claim today changes nothing).</returns>
        Awaitable<Result<DailyRewardStatus>> ClaimAsync(CancellationToken cancellation);
    }
}
