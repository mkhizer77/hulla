using System;
using System.Collections;
using Hulla.Infrastructure.Nakama;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>The tutorial's one-time reward against a running Nakama (GDD 17.2): paid once per account.</summary>
    public sealed class WelcomeAgainstNakamaTests
    {
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheTutorialPaysOnce()
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
            var sessions = new NakamaSessionService(connection, "hulla-welcome-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Assert.IsTrue((await sessions.SignInAsync("Newcomer", default)).IsOk);
            var welcome = new NakamaWelcomeService(connection);
            var first = await welcome.TutorialCompleteAsync(default);
            Assert.IsTrue(first.IsOk, first.IsOk ? "" : first.Error.Code);
            Assert.AreEqual(100, first.Value);
            var again = await welcome.TutorialCompleteAsync(default);
            Assert.AreEqual(0, again.Value);
            connection.Close();
        }
    }
}
