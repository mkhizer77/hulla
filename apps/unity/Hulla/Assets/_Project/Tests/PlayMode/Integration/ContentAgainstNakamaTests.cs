using System;
using System.Collections;
using Hulla.Infrastructure.Content;
using Hulla.Infrastructure.Nakama;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>content_manifest and content_get against a running Nakama (ADR-0011): each served file matches its hash.</summary>
    public sealed class ContentAgainstNakamaTests
    {
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheServedManifestListsFilesThatMatchTheirHashes()
        {
            var host = Environment.GetEnvironmentVariable("HULLA_NAKAMA_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Ignore("HULLA_NAKAMA_HOST is not set");
            }

            var port = int.TryParse(Environment.GetEnvironmentVariable("HULLA_NAKAMA_PORT"), out var p) ? p : 7350;
            var awaiter = RunAsync(host!, port).GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            awaiter.GetResult();
        }

        private static async Awaitable RunAsync(string host, int port)
        {
            var connection = NakamaConnection.Create("http", host, port, "local-only-server-key");
            var sessions = new NakamaSessionService(connection, "hulla-content-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Assert.IsTrue((await sessions.SignInAsync("Content", default)).IsOk);
            var api = new NakamaContentApi(connection);
            var signed = await api.ManifestAsync(default);
            Assert.IsTrue(signed.IsOk, "the manifest arrived");
            Assert.IsNull(signed.Value.Signature, "a development box serves it unsigned");
            var manifest = ContentManifest.Parse(signed.Value.SignedBytes);
            Assert.IsNotNull(manifest);
            Assert.Greater(manifest!.Version, 0);
            foreach (var path in new[] { "emotes/v1.json", "presets/thulla/standard.json", "strings/en.json" })
            {
                var entry = manifest.Find(path);
                Assert.IsNotNull(entry, path);
                var file = await api.FileAsync(path, default);
                Assert.IsTrue(file.IsOk, path);
                Assert.AreEqual(entry!.Sha256, ContentStore.Sha256Hex(file.Value), path);
            }

            connection.Close();
        }
    }
}
