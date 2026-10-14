using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace Hulla.Infrastructure.Content
{
    /// <summary>
    /// Addressables behind <see cref="IAssetPacks"/> (ADR-0011). Remote bundles are built against a
    /// placeholder host (BuildTools/Editor/AddressablesSetup) that is rewritten to the CDN root from
    /// remote config, so moving the CDN needs no release; Addressables' bundle cache keeps them.
    /// </summary>
    public sealed class AddressablePacks : IAssetPacks
    {
        public const string PlaceholderRoot = "https://cdn.invalid/hulla";

        private readonly Dictionary<string, Texture2D?> _loaded = new Dictionary<string, Texture2D?>(StringComparer.Ordinal);
        private static string _remoteRoot = "";
        private bool _initialized;

        public AddressablePacks()
        {
            Addressables.InternalIdTransformFunc = Transform;
        }

        public void UseRemoteRoot(string root) => _remoteRoot = root.TrimEnd('/');

        /// <summary>The CDN root replaces the placeholder; with none, remote packs stay unreachable.</summary>
        public static string Rewrite(string id, string root) =>
            id.StartsWith(PlaceholderRoot, StringComparison.Ordinal) && root.Length > 0 ? root + id.Substring(PlaceholderRoot.Length) : id;

        public Texture2D? LoadLocal(string address)
        {
            if (_loaded.TryGetValue(address, out var cached))
            {
                return cached;
            }

            Initialize();
            Texture2D? texture = null;
            if (Exists(address))
            {
                var handle = Addressables.LoadAssetAsync<Texture2D>(address);
                texture = handle.WaitForCompletion();
                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    texture = null;
                }
            }

            _loaded[address] = texture;
            return texture;
        }

        public async Awaitable<Texture2D?> LoadAsync(string address, CancellationToken cancellation)
        {
            if (_loaded.TryGetValue(address, out var cached) && cached != null)
            {
                return cached;
            }

            Initialize();
            if (!Exists(address))
            {
                return null;
            }

            var handle = Addressables.LoadAssetAsync<Texture2D>(address);
            while (!handle.IsDone)
            {
                await Awaitable.NextFrameAsync(cancellation);
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning($"pack not loaded: {address} ({handle.OperationException?.Message})");
                Addressables.Release(handle);
                return null;
            }

            _loaded[address] = handle.Result;
            return handle.Result;
        }

        public async Awaitable<long> DownloadSizeAsync(string key, CancellationToken cancellation)
        {
            var handle = Addressables.GetDownloadSizeAsync(key);
            while (!handle.IsDone)
            {
                await Awaitable.NextFrameAsync(cancellation);
            }

            var size = handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : 0;
            Addressables.Release(handle);
            return size;
        }

        /// <summary>Reads the local catalog once; it ships in the binary, so this does not wait on the network.</summary>
        private void Initialize()
        {
            if (!_initialized)
            {
                Addressables.InitializeAsync().WaitForCompletion();
                _initialized = true;
            }
        }

        private static string Transform(IResourceLocation location) => Rewrite(location.InternalId, _remoteRoot);

        private static bool Exists(string address)
        {
            foreach (var locator in Addressables.ResourceLocators)
            {
                if (locator.Locate(address, typeof(Texture2D), out _))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
