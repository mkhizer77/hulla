using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>The player's data rights (GDD 10.2, 25): export everything, delete after a grace period, or change their mind.</summary>
    public interface IAccountService
    {
        /// <summary>The account export as JSON text.</summary>
        Awaitable<Result<string>> ExportAsync(CancellationToken cancellation);

        /// <summary>Schedules deletion; the result is when it happens (Unix ms).</summary>
        Awaitable<Result<long>> DeleteAsync(CancellationToken cancellation);

        Awaitable<Result<bool>> CancelDeleteAsync(CancellationToken cancellation);
    }
}
