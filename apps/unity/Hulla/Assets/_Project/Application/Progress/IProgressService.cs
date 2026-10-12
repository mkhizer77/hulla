using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Progress
{
    /// <summary>progress_get and wallet_get. Coins and gems only ever come from the server's ledger (D2).</summary>
    public interface IProgressService
    {
        /// <param name="tzOffsetMinutes">The device's UTC offset, so the daily counters roll at local midnight.</param>
        Awaitable<Result<ProgressInfo>> ProgressAsync(int tzOffsetMinutes, CancellationToken cancellation);

        Awaitable<Result<(long Coins, long Gems)>> BalancesAsync(CancellationToken cancellation);
    }
}
