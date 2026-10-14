using System.Threading;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>
    /// The content this launch reads (ADR-0011, GDD 18.9): one manifest version pinned for the whole
    /// session, each file checked against its hash. Newer signed content downloads in the background
    /// and is used from the next launch.
    /// </summary>
    public interface IContentStore
    {
        /// <summary>The pinned manifest version; 0 before <see cref="OpenAsync"/>.</summary>
        long Version { get; }

        /// <summary>Pins the newest complete, verified manifest on this device, else the bundled one.</summary>
        Awaitable OpenAsync(CancellationToken cancellation);

        /// <summary>A file by its manifest path (<c>presets/thulla/standard.json</c>), or null when it is not listed.</summary>
        Awaitable<string?> ReadTextAsync(string path, CancellationToken cancellation);

        /// <summary>Fetches the server's manifest and any changed files for the next launch.</summary>
        Awaitable<ContentSyncOutcome> SyncAsync(CancellationToken cancellation);
    }

    public enum ContentSyncOutcome
    {
        UpToDate,
        Downloaded,
        Rejected,
        Unavailable,
    }
}
