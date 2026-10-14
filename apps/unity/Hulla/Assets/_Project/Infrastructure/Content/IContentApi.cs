using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Infrastructure.Content
{
    /// <summary>Where newer content comes from: the server's content_manifest and content_get today, a CDN later.</summary>
    public interface IContentApi
    {
        Awaitable<Result<SignedManifest>> ManifestAsync(CancellationToken cancellation);

        /// <summary>The file's exact text, which the store checks against the manifest's hash.</summary>
        Awaitable<Result<string>> FileAsync(string path, CancellationToken cancellation);
    }

    /// <summary>The manifest as signed: the exact bytes, the base64 signature (null when unsigned) and the key's name.</summary>
    public sealed class SignedManifest
    {
        public SignedManifest(string signedBytes, string? signature, string? keyId)
        {
            SignedBytes = signedBytes;
            Signature = signature;
            KeyId = keyId;
        }

        public string SignedBytes { get; }

        public string? Signature { get; }

        public string? KeyId { get; }
    }

    /// <summary>The content shipped in the binary (StreamingAssets/content), trusted as it is.</summary>
    public interface IBundledContent
    {
        Awaitable<string?> ReadAsync(string path, CancellationToken cancellation);
    }
}
