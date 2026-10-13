using System;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>
    /// Quick Play search on the server matchmaker (GDD 9.6). One ticket at a time; searching again
    /// replaces it. <see cref="Matched"/> carries the table to enter, raised on the main thread.
    /// </summary>
    public interface IMatchmaker
    {
        event Action<TableTicket>? Matched;

        /// <param name="seats">The table size asked for (4 to 6).</param>
        /// <param name="anySize">False keeps to that size; true accepts any table size (the widened search).</param>
        Awaitable<Result<bool>> SearchAsync(int seats, bool anySize, CancellationToken cancellation);

        /// <summary>A Ranked ticket (GDD 7.5): four seats, solo; the server puts the stored rating on it and holds the band.</summary>
        /// <param name="band">How far either side of the player's rating to look.</param>
        Awaitable<Result<bool>> SearchRankedAsync(int band, CancellationToken cancellation);

        Awaitable CancelAsync();
    }
}
