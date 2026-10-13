using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Progress
{
    /// <summary>missions_get, missions_claim and missions_reroll (GDD 12.5); claims pay through the ledger once.</summary>
    public interface IMissionsService
    {
        Awaitable<Result<(IReadOnlyList<MissionInfo> Missions, bool RerollAvailable)>> MissionsAsync(int tzOffsetMinutes, CancellationToken cancellation);

        Awaitable<Result<bool>> ClaimAsync(string missionId, CancellationToken cancellation);

        Awaitable<Result<bool>> RerollAsync(string missionId, CancellationToken cancellation);
    }
}
