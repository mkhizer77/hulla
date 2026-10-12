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

            public void Send(IReadOnlyList<AnalyticsEvent> batch) => Batches.Add(batch);
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
