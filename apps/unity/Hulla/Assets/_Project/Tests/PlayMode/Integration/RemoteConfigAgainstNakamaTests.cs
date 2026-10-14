using System;
using System.Collections;
using Hulla.Application.ViewModels;
using Hulla.Infrastructure.Nakama;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>config_get against a running Nakama (GDD 22.5, 20.15): flags arrive, and only an old client must update.</summary>
    public sealed class RemoteConfigAgainstNakamaTests
    {
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator FlagsArriveAndOnlyAnOldClientMustUpdate()
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
            var sessions = new NakamaSessionService(connection, "hulla-config-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Assert.IsTrue((await sessions.SignInAsync("Config", default)).IsOk);
            var model = new RemoteConfigModel(new NakamaRemoteConfigService(connection));
            await model.RefreshAsync("99.0.0", "*", default);
            Assert.Greater(model.Current.Flags.Count, 0, "the flags arrived");
            Assert.IsFalse(model.UpdateRequired);
            Assert.IsTrue(model.Current.Flags.ContainsKey("quick_play"));
            await model.RefreshAsync("0.0.1", "*", default);
            Assert.IsTrue(model.UpdateRequired, "a client below the minimum must update");
            connection.Close();
        }
    }
}
