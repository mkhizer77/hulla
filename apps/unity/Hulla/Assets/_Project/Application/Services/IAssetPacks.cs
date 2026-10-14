using System.Threading;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>
    /// Art and audio by address (<c>avatars-base/a01</c>, ADR-0011). Local groups ship in the binary
    /// and load at once; remote groups stream from the content CDN on first use and stay cached.
    /// </summary>
    public interface IAssetPacks
    {
        /// <summary>A texture from a group in the binary, or null when the address is unknown.</summary>
        Texture2D? LoadLocal(string address);

        /// <summary>A texture from any group, downloading its pack when needed; null when unknown or offline.</summary>
        Awaitable<Texture2D?> LoadAsync(string address, CancellationToken cancellation);

        /// <summary>Bytes still to download for the packs behind a key; 0 when cached or in the binary.</summary>
        Awaitable<long> DownloadSizeAsync(string key, CancellationToken cancellation);

        /// <summary>Where remote packs come from, from remote config; empty keeps remote packs off.</summary>
        void UseRemoteRoot(string root);
    }
}
