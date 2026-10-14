using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using Hulla.Infrastructure.Nakama;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class RemoteConfigModelTests
    {
        private sealed class FakeConfig : IRemoteConfigService
        {
            public Result<RemoteConfigSnapshot> Answer = Result<RemoteConfigSnapshot>.Fail("session.unavailable");
            public string? AskedVersion;

            public Awaitable<Result<RemoteConfigSnapshot>> FetchAsync(string clientVersion, string cohort, CancellationToken cancellation)
            {
                AskedVersion = clientVersion;
                var done = new AwaitableCompletionSource<Result<RemoteConfigSnapshot>>();
                done.SetResult(Answer);
                return done.Awaitable;
            }
        }

        private const string Body = @"{""ok"":true,""version"":7,""region"":""eu"",""minClientVersion"":""0.3.0"",""updateRequired"":true,
            ""flags"":{""maintenance"":false,""quick_play"":false,""voice"":true,""turn_seconds_public"":12,""update_url"":""https://hulla.gg/get""}}";

        [Test]
        public void TheServersAnswerIsReadIntoTypedFlags()
        {
            var snapshot = NakamaRemoteConfigService.Parse(Body);
            Assert.AreEqual(7, snapshot.Version);
            Assert.AreEqual("eu", snapshot.Region);
            Assert.IsTrue(snapshot.UpdateRequired);
            Assert.IsFalse(snapshot.Bool("quick_play", true));
            Assert.AreEqual(12, snapshot.Number("turn_seconds_public", 15));
            Assert.AreEqual("https://hulla.gg/get", snapshot.Text("update_url", ""));
            Assert.AreEqual(3, snapshot.Number("missing", 3));
        }

        [Test]
        public void BeforeAnAnswerAndWhenTheServerIsDownEverythingIsOn()
        {
            var service = new FakeConfig();
            var model = new RemoteConfigModel(service);
            Assert.IsTrue(model.QuickPlay);
            Assert.IsTrue(model.Voice);
            Assert.IsFalse(model.UpdateRequired);
            _ = model.RefreshAsync("0.2.0", "*", CancellationToken.None);
            Assert.AreEqual("0.2.0", service.AskedVersion);
            Assert.IsTrue(model.QuickPlay, "a failed fetch keeps the defaults");
        }

        [Test]
        public void MaintenanceRestsQuickPlayAndAnOldClientMustUpdate()
        {
            var service = new FakeConfig { Answer = Result<RemoteConfigSnapshot>.Ok(NakamaRemoteConfigService.Parse(Body.Replace(@"""maintenance"":false", @"""maintenance"":true").Replace(@"""quick_play"":false", @"""quick_play"":true"))) };
            var model = new RemoteConfigModel(service);
            var changed = 0;
            model.Changed += () => changed++;
            _ = model.RefreshAsync("0.2.0", "*", CancellationToken.None);
            Assert.AreEqual(1, changed);
            Assert.IsTrue(model.Maintenance);
            Assert.IsFalse(model.QuickPlay);
            Assert.IsTrue(model.UpdateRequired);
        }
    }
}
