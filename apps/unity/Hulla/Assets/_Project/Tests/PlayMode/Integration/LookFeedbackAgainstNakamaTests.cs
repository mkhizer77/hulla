using System;
using System.Collections;
using Hulla.Application.Services;
using Hulla.Infrastructure.Nakama;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>The look playtest's answer against a running Nakama: kept, and a bad rating refused.</summary>
    public sealed class LookFeedbackAgainstNakamaTests
    {
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AnAnswerIsKept()
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
            var sessions = new NakamaSessionService(connection, "hulla-looks-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Assert.IsTrue((await sessions.SignInAsync("Tester", default)).IsOk);
            var feedback = new NakamaLookFeedbackService(connection);
            var sent = await feedback.SendAsync(new LookAnswer("basant", 5, true, "  bright and easy to read ", 3), default);
            Assert.IsTrue(sent.IsOk, sent.IsOk ? "" : sent.Error.Code);
            var refused = await feedback.SendAsync(new LookAnswer("basant", 0, true, "", 1), default);
            Assert.IsFalse(refused.IsOk);
            Assert.AreEqual("table.invalid_payload", refused.Error.Code);
            connection.Close();
        }
    }
}
