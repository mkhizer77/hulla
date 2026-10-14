using System;
using System.Threading;
using Hulla.Domain;
using Hulla.Infrastructure.Content;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>content_manifest and content_get (ADR-0011): the server serves the content it bundles until a CDN does.</summary>
    public sealed class NakamaContentApi : IContentApi
    {
        private readonly NakamaConnection _connection;

        public NakamaContentApi(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<SignedManifest>> ManifestAsync(CancellationToken cancellation)
        {
            var body = await CallAsync("content_manifest", "{}", cancellation);
            var bytes = (string?)body?["signedBytes"];
            if (body == null || bytes == null)
            {
                return Result<SignedManifest>.Fail("content.unavailable");
            }

            return Result<SignedManifest>.Ok(new SignedManifest(bytes, (string?)body["signature"], (string?)body["keyId"]));
        }

        public async Awaitable<Result<string>> FileAsync(string path, CancellationToken cancellation)
        {
            var input = new JObject { ["path"] = path }.ToString(Newtonsoft.Json.Formatting.None);
            var body = await CallAsync("content_get", input, cancellation);
            var text = (string?)body?["text"];
            return text == null ? Result<string>.Fail("content.unavailable", path) : Result<string>.Ok(text);
        }

        private async Awaitable<JObject?> CallAsync(string rpc, string payload, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return null;
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, rpc, payload, null, cancellation);
                var body = JObject.Parse(response.Payload);
                return (bool?)body["ok"] == true ? body : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{rpc}: {error.Message}");
                return null;
            }
        }
    }
}
