using System.Collections.Generic;
using Hulla.Application.Analytics;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class AnalyticsClientTests
    {
        private sealed class Sink : IAnalyticsSink
        {
            public readonly List<IReadOnlyList<AnalyticsEvent>> Batches = new List<IReadOnlyList<AnalyticsEvent>>();

            public readonly List<(string? User, IReadOnlyDictionary<string, object> Properties)> Identities = new List<(string?, IReadOnlyDictionary<string, object>)>();

            public int Forgotten;

            public void Send(IReadOnlyList<AnalyticsEvent> batch) => Batches.Add(batch);

            public void Identify(string? userId, IReadOnlyDictionary<string, object> properties) => Identities.Add((userId, properties));

            public void Forget() => Forgotten++;
        }

        [Test]
        public void NothingIsKeptBeforeConsent()
        {
            var sink = new Sink();
            var analytics = new AnalyticsClient(sink);
            analytics.Track(AnalyticsEvents.AppOpen(true));
            analytics.SetConsent(true);
            analytics.Flush();
            Assert.IsEmpty(sink.Batches, "an event before consent is dropped, not held");
        }

        [Test]
        public void AfterConsentEventsLeaveInBatches()
        {
            var sink = new Sink();
            var analytics = new AnalyticsClient(sink);
            analytics.SetConsent(true);
            for (var i = 0; i < AnalyticsClient.BatchSize; i++)
            {
                analytics.Track(AnalyticsEvents.Pickup(i));
            }

            Assert.AreEqual(1, sink.Batches.Count, "a full batch flushes at once");
            analytics.Track(AnalyticsEvents.Pickup(3));
            analytics.Tick(AnalyticsClient.FlushSeconds);
            Assert.AreEqual(2, sink.Batches.Count, "and the rest after the flush interval");
            Assert.AreEqual("pickup", sink.Batches[1][0].Name);
        }

        [Test]
        public void WithdrawingConsentDropsWhatWasQueued()
        {
            var sink = new Sink();
            var analytics = new AnalyticsClient(sink);
            analytics.SetConsent(true);
            analytics.Track(AnalyticsEvents.Pickup(2));
            analytics.SetConsent(false);
            analytics.Flush();
            Assert.IsEmpty(sink.Batches);
        }

        [Test]
        public void WithdrawingConsentTellsEverySinkToForgetOnce()
        {
            var first = new Sink();
            var second = new Sink();
            var analytics = new AnalyticsClient(first, second);
            analytics.SetConsent(true);
            analytics.SetConsent(false);
            analytics.SetConsent(false);
            Assert.AreEqual(1, first.Forgotten);
            Assert.AreEqual(1, second.Forgotten);
        }

        [Test]
        public void EveryEventGoesToEverySink()
        {
            var first = new Sink();
            var second = new Sink();
            var analytics = new AnalyticsClient(first, second);
            analytics.SetConsent(true);
            analytics.Track(AnalyticsEvents.ScreenView("home"));
            analytics.Flush();
            Assert.AreEqual("screen_view", first.Batches[0][0].Name);
            Assert.AreEqual("screen_view", second.Batches[0][0].Name);
        }

        [Test]
        public void ContextGoesOnEveryEventUntilCleared()
        {
            var sink = new Sink();
            var analytics = new AnalyticsClient(sink);
            analytics.SetConsent(true);
            analytics.SetContext("game", "rung");
            analytics.Track(AnalyticsEvents.RoundStart("private", 4));
            analytics.Track(new AnalyticsEvent("round_end", new Dictionary<string, object> { ["game"] = "thulla" }));
            analytics.SetContext("game", null);
            analytics.Track(AnalyticsEvents.ScreenView("home"));
            analytics.Flush();
            var batch = sink.Batches[0];
            Assert.AreEqual("rung", batch[0].Parameters["game"]);
            Assert.AreEqual("thulla", batch[1].Parameters["game"], "the event's own parameter wins");
            Assert.IsFalse(batch[2].Parameters.ContainsKey("game"));
        }

        [Test]
        public void ThePlayerIsDescribedOnlyAfterConsentAndOnlyByWhatChanged()
        {
            var sink = new Sink();
            var analytics = new AnalyticsClient(sink);
            analytics.Identify("user-1", new Dictionary<string, object> { ["level"] = 3L, ["name"] = "Ayesha" });
            Assert.IsEmpty(sink.Identities, "nothing before consent");
            analytics.SetConsent(true);
            Assert.AreEqual(1, sink.Identities.Count);
            Assert.AreEqual("user-1", sink.Identities[0].User);
            Assert.AreEqual(3L, sink.Identities[0].Properties["level"]);
            Assert.IsFalse(sink.Identities[0].Properties.ContainsKey("name"), "a name never leaves");
            analytics.Identify("user-1", new Dictionary<string, object> { ["level"] = 3L });
            Assert.AreEqual(1, sink.Identities.Count, "nothing changed, nothing sent");
            analytics.Identify("user-1", new Dictionary<string, object> { ["level"] = 4L, ["payer"] = false });
            Assert.AreEqual(2, sink.Identities[1].Properties.Count);
            analytics.Identify("user-2", new Dictionary<string, object> { ["level"] = 4L });
            Assert.AreEqual("user-2", sink.Identities[2].User);
            Assert.AreEqual(4L, sink.Identities[2].Properties["level"], "another account is described from scratch");
        }

        [Test]
        public void EventsBeforeASignInAreSentBeforeTheAccountIsNamed()
        {
            var sink = new Sink();
            var analytics = new AnalyticsClient(sink);
            analytics.SetConsent(true);
            analytics.Track(AnalyticsEvents.AppOpen(true));
            analytics.Identify("user-1", new Dictionary<string, object>());
            Assert.AreEqual(1, sink.Batches.Count, "the open event left before the identity changed");
        }

        [Test]
        public void AnEventWithACardListANameOrAListIsNeverSent()
        {
            var sink = new Sink();
            var analytics = new AnalyticsClient(sink);
            analytics.SetConsent(true);
            analytics.Track(new AnalyticsEvent("pickup", new Dictionary<string, object> { ["cards"] = "AS, KH, 2C" }));
            analytics.Track(new AnalyticsEvent("friend_add", new Dictionary<string, object> { ["name"] = "Ayesha" }));
            analytics.Track(new AnalyticsEvent("pickup", new Dictionary<string, object> { ["cards"] = new[] { "AS" } }));
            analytics.Flush();
            Assert.AreEqual(3, analytics.Dropped);
            Assert.IsEmpty(sink.Batches);
            Assert.IsTrue(AnalyticsClient.IsClean(AnalyticsEvents.Pickup(9)), "a count is fine");
        }
    }
}
