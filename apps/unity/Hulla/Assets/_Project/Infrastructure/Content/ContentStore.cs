using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Hulla.Application.Services;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Content
{
    /// <summary>
    /// Signed content with an offline fallback (ADR-0011, GDD 18.9). The bundled manifest and files
    /// ship in the binary; newer ones arrive through <see cref="IContentApi"/>, are accepted only with
    /// a valid signature (or unsigned in a development build) and only once every changed file
    /// matches its hash, and are cached by hash under the cache folder. A launch pins one manifest:
    /// what <see cref="SyncAsync"/> downloads is read from the next launch on.
    /// </summary>
    public sealed class ContentStore : IContentStore
    {
        private const string ManifestPath = "manifest.json";
        private const string CachedManifestFile = "manifest.signed.json";
        private readonly IBundledContent _bundled;
        private readonly IContentApi _api;
        private readonly string _cacheDir;
        private readonly RSAParameters? _publicKey;
        private readonly bool _acceptUnsigned;
        private ContentManifest? _bundledManifest;
        private ContentManifest? _pinned;
        private long _newest;

        /// <param name="publicKey">The content key in the binary; null means only unsigned content could pass.</param>
        /// <param name="acceptUnsigned">True in development builds, which talk to boxes without a signature.</param>
        public ContentStore(IBundledContent bundled, IContentApi api, string cacheDir, RSAParameters? publicKey, bool acceptUnsigned)
        {
            _bundled = bundled;
            _api = api;
            _cacheDir = cacheDir;
            _publicKey = publicKey;
            _acceptUnsigned = acceptUnsigned;
        }

        public long Version => _pinned?.Version ?? 0;

        private string FilesDir => Path.Combine(_cacheDir, "files");

        public async Awaitable OpenAsync(CancellationToken cancellation)
        {
            var bundledText = await _bundled.ReadAsync(ManifestPath, cancellation);
            _bundledManifest = bundledText == null ? null : ContentManifest.Parse(bundledText);
            _pinned = _bundledManifest;
            var cached = ReadCachedManifest();
            if (cached != null && cached.Version > (_pinned?.Version ?? 0) && Complete(cached))
            {
                _pinned = cached;
            }

            _newest = _pinned?.Version ?? 0;
        }

        public async Awaitable<string?> ReadTextAsync(string path, CancellationToken cancellation)
        {
            var entry = _pinned?.Find(path);
            if (entry == null)
            {
                return null;
            }

            var cached = ReadCachedFile(entry.Sha256);
            if (cached != null)
            {
                return cached;
            }

            // The bundled copy is the fallback for any file: it shipped in the binary.
            return await _bundled.ReadAsync(path, cancellation);
        }

        public async Awaitable<ContentSyncOutcome> SyncAsync(CancellationToken cancellation)
        {
            var fetched = await _api.ManifestAsync(cancellation);
            if (!fetched.IsOk)
            {
                return ContentSyncOutcome.Unavailable;
            }

            var signed = fetched.Value;
            var manifest = Accept(signed);
            if (manifest == null)
            {
                Debug.LogWarning("content manifest rejected: bad signature or shape");
                return ContentSyncOutcome.Rejected;
            }

            if (manifest.Version <= _newest)
            {
                return ContentSyncOutcome.UpToDate;
            }

            foreach (var entry in manifest.Files)
            {
                if (ReadCachedFile(entry.Sha256) != null || BundledMatches(entry))
                {
                    continue;
                }

                var file = await _api.FileAsync(entry.Path, cancellation);
                if (!file.IsOk)
                {
                    return ContentSyncOutcome.Unavailable;
                }

                if (!Matches(file.Value, entry))
                {
                    Debug.LogWarning($"content file rejected: {entry.Path} does not match its hash");
                    return ContentSyncOutcome.Rejected;
                }

                WriteAtomically(Path.Combine(FilesDir, entry.Sha256 + ".json"), file.Value);
            }

            var record = new JObject
            {
                ["signedBytes"] = signed.SignedBytes,
                ["signature"] = signed.Signature,
                ["keyId"] = signed.KeyId,
            };
            WriteAtomically(Path.Combine(_cacheDir, CachedManifestFile), record.ToString(Newtonsoft.Json.Formatting.None));
            _newest = manifest.Version;
            Prune(manifest);
            return ContentSyncOutcome.Downloaded;
        }

        public static string Sha256Hex(string text)
        {
            // The concrete type, not SHA256.Create(): IL2CPP strips what CryptoConfig finds by name.
            using var sha = new SHA256Managed();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var hex = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                hex.Append(b.ToString("x2"));
            }

            return hex.ToString();
        }

        private static bool Matches(string text, ContentManifest.Entry entry) =>
            Sha256Hex(text) == entry.Sha256 && (entry.Bytes < 0 || Encoding.UTF8.GetByteCount(text) == entry.Bytes);

        private ContentManifest? Accept(SignedManifest signed)
        {
            var trusted = signed.Signature == null
                ? _acceptUnsigned
                : _publicKey.HasValue && ManifestSignature.Verify(signed.SignedBytes, signed.Signature, _publicKey.Value);
            return trusted ? ContentManifest.Parse(signed.SignedBytes) : null;
        }

        private bool BundledMatches(ContentManifest.Entry entry) => _bundledManifest?.Find(entry.Path)?.Sha256 == entry.Sha256;

        /// <summary>Every file is in the cache or bundled with the same hash, so a launch never pins half a set.</summary>
        private bool Complete(ContentManifest manifest)
        {
            foreach (var entry in manifest.Files)
            {
                if (ReadCachedFile(entry.Sha256) == null && !BundledMatches(entry))
                {
                    return false;
                }
            }

            return true;
        }

        private ContentManifest? ReadCachedManifest()
        {
            try
            {
                var path = Path.Combine(_cacheDir, CachedManifestFile);
                if (!File.Exists(path))
                {
                    return null;
                }

                var record = JObject.Parse(File.ReadAllText(path));
                var bytes = (string?)record["signedBytes"];
                return bytes == null ? null : Accept(new SignedManifest(bytes, (string?)record["signature"], (string?)record["keyId"]));
            }
            catch (Exception error)
            {
                Debug.LogWarning($"cached content manifest unreadable: {error.Message}");
                return null;
            }
        }

        /// <summary>A cached file only counts when it still hashes to its name.</summary>
        private string? ReadCachedFile(string sha256)
        {
            var path = Path.Combine(FilesDir, sha256 + ".json");
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var text = File.ReadAllText(path, Encoding.UTF8);
                if (Sha256Hex(text) == sha256)
                {
                    return text;
                }

                File.Delete(path);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"cached content unreadable: {error.Message}");
            }

            return null;
        }

        private static void WriteAtomically(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }

        /// <summary>Keeps the files this launch reads and the ones the next launch will.</summary>
        private void Prune(ContentManifest next)
        {
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var manifest in new[] { _pinned, next })
            {
                if (manifest == null)
                {
                    continue;
                }

                foreach (var entry in manifest.Files)
                {
                    keep.Add(entry.Sha256 + ".json");
                }
            }

            try
            {
                foreach (var file in Directory.GetFiles(FilesDir))
                {
                    if (!keep.Contains(Path.GetFileName(file)))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning($"content cache not pruned: {error.Message}");
            }
        }
    }
}
