using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Hulla.Infrastructure.Content
{
    /// <summary>StreamingAssets/content, copied from the repository's content/ by BuildTools/Editor/ContentSync.</summary>
    public sealed class StreamingBundledContent : IBundledContent
    {
        public async Awaitable<string?> ReadAsync(string path, CancellationToken cancellation)
        {
            var full = Path.Combine(UnityEngine.Application.streamingAssetsPath, "content", path);
            using var request = UnityWebRequest.Get(full.Contains("://") ? full : "file://" + full);
            await request.SendWebRequest();
            cancellation.ThrowIfCancellationRequested();
            return request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
        }
    }
}
