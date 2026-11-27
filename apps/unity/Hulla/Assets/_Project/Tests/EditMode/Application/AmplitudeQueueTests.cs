using System.Collections.Generic;
using Hulla.Application.Analytics;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class AmplitudeQueueTests
    {
        private long _now = 1_800_000_000_000;
        private int _ids;

        private AmplitudeQueue Queue() => new AmplitudeQueue(
            "test-key",
            new AmplitudeDevice("device-abc", "Android", "Android", "14", "Pixel 7a", "0.1.2", "English", "PK"),
            () => _now,
            () => "id" + _ids++);

        [SetUp]
        public void Reset()
        {
            _now = 1_800_000_000_000;
            _ids = 0;
        }

        [Test]
        public void AnEventIsOneJsonObjectWithItsTimeSessionAndInsertId()
        {
            var queue = Queue();
            queue.Identify("user-12345", new Dictionary<string, object>());
            queue.Enqueue(AnalyticsEvents.RoundEnd(312.5, seats: 4, lost: false));
            Assert.AreEqual(1, queue.Held.Count, "an identity with nothing to set sends nothing");
            Assert.AreEqual(
                "{\"event_type\":\"round_end\",\"time\":1800000000000,\"session_id\":1800000000000,\"insert_id\":\"id0\","
                + "\"device_id\":\"device-abc\",\"user_id\":\"user-12345\",\"platform\":\"Android\",\"os_name\":\"Android\","
                + "\"os_version\":\"14\",\"device_model\":\"Pixel 7a\",\"app_version\":\"0.1.2\",\"language\":\"English\",\"country\":\"PK\","
                + "\"event_properties\":{\"duration\":312.5,\"seats\":4,\"lost\":false}}",
                queue.Held[0]);
        }

        [Test]
        public void ARequestCarriesTheKeyAndAtMostTenEvents()
        {
            var queue = Queue();
            for (var i = 0; i < 12; i++)
            {
                queue.Enqueue(AnalyticsEvents.ScreenView("home"));
            }

            var body = queue.NextRequest();
            Assert.IsNotNull(body);
            StringAssert.StartsWith("{\"api_key\":\"test-key\",\"events\":[{", body);
            Assert.AreEqual(AmplitudeQueue.MaxPerRequest, Occurrences(body!, "\"event_type\""));
            Assert.IsNull(queue.NextRequest(), "one request at a time");
            queue.Completed(200);
            Assert.AreEqual(2, queue.Held.Count);
            Assert.AreEqual(2, Occurrences(queue.NextRequest()!, "\"event_type\""));
            queue.Completed(200);
            Assert.IsNull(queue.NextRequest());
        }

        [Test]
        public void BeforeSignInAnEventHasOnlyTheDeviceId()
        {
            var queue = Queue();
            queue.Enqueue(AnalyticsEvents.AppOpen(true));
            StringAssert.DoesNotContain("user_id", queue.Held[0]);
            queue.Identify("abc", new Dictionary<string, object>());
            queue.Enqueue(AnalyticsEvents.AppOpen(true));
            StringAssert.DoesNotContain("user_id", queue.Held[1], "Amplitude refuses ids under five characters");
        }

        [Test]
        public void WhatIsKnownAboutThePlayerGoesAsAnIdentifyEvent()
        {
            var queue = Queue();
            queue.Identify("user-12345", new Dictionary<string, object> { ["level"] = 7L, ["payer"] = true, ["locale"] = "en" });
            StringAssert.Contains("\"event_type\":\"$identify\"", queue.Held[0]);
            StringAssert.EndsWith("\"user_properties\":{\"$set\":{\"level\":7,\"payer\":true,\"locale\":\"en\"}}}", queue.Held[0]);
        }

        [Test]
        public void ALongPauseStartsANewSession()
        {
            var queue = Queue();
            queue.Enqueue(AnalyticsEvents.ScreenView("home"));
            _now += 60_000;
            queue.Enqueue(AnalyticsEvents.ScreenView("shop"));
            StringAssert.Contains("\"session_id\":1800000000000", queue.Held[1]);
            _now += AmplitudeQueue.SessionGapMs + 1;
            queue.Enqueue(AnalyticsEvents.ScreenView("home"));
            StringAssert.Contains("\"session_id\":" + _now, queue.Held[2]);
        }

        [Test]
        public void ARefusedBatchIsDroppedAThrottledOrFailedOneIsKept()
        {
            var queue = Queue();
            queue.Enqueue(AnalyticsEvents.ScreenView("home"));
            queue.NextRequest();
            queue.Completed(429);
            Assert.AreEqual(1, queue.Held.Count);
            Assert.IsNull(queue.NextRequest(), "waits after a 429");
            _now += AmplitudeQueue.ThrottledWaitMs;
            Assert.IsNotNull(queue.NextRequest());
            queue.Completed(0);
            Assert.AreEqual(1, queue.Held.Count, "no answer keeps the event");
            Assert.IsNull(queue.NextRequest());
            _now += AmplitudeQueue.FirstRetryMs;
            Assert.IsNotNull(queue.NextRequest());
            queue.Completed(503);
            _now += AmplitudeQueue.FirstRetryMs;
            Assert.IsNull(queue.NextRequest(), "each failure waits twice as long");
            _now += AmplitudeQueue.FirstRetryMs;
            Assert.IsNotNull(queue.NextRequest());
            queue.Completed(400);
            Assert.IsEmpty(queue.Held, "a 400 would be refused again");
        }

        [Test]
        public void ATooLargeRequestIsHalved()
        {
            var queue = Queue();
            for (var i = 0; i < 8; i++)
            {
                queue.Enqueue(AnalyticsEvents.ScreenView("home"));
            }

            queue.NextRequest();
            queue.Completed(413);
            Assert.AreEqual(8, queue.Held.Count);
            Assert.AreEqual(4, Occurrences(queue.NextRequest()!, "\"event_type\""));
        }

        [Test]
        public void EventsFromTheLastRunGoFirstAndTheOldestGiveWayWhenFull()
        {
            var queue = Queue();
            queue.Enqueue(AnalyticsEvents.ScreenView("shop"));
            queue.Restore(new[] { "{\"event_type\":\"old\"}", "", "not json" });
            Assert.AreEqual(2, queue.Held.Count);
            StringAssert.Contains("old", queue.Held[0]);
            for (var i = 0; i < AmplitudeQueue.MaxHeld; i++)
            {
                queue.Enqueue(AnalyticsEvents.ScreenView("home"));
            }

            Assert.AreEqual(AmplitudeQueue.MaxHeld, queue.Held.Count);
            StringAssert.DoesNotContain("old", queue.Held[0]);
        }

        [Test]
        public void TextIsEscapedAndWhatJsonCannotHoldIsLeftOut()
        {
            var queue = Queue();
            queue.Enqueue(new AnalyticsEvent("error_shown", new Dictionary<string, object>
            {
                ["code"] = "a\"b\\c\nd",
                ["bad"] = double.NaN,
                ["list"] = new[] { 1 },
                ["ok"] = 2,
            }));
            StringAssert.EndsWith("\"event_properties\":{\"code\":\"a\\\"b\\\\c\\nd\",\"ok\":2}}", queue.Held[0]);
        }

        [Test]
        public void ClearingForgetsEverything()
        {
            var queue = Queue();
            queue.Identify("user-12345", new Dictionary<string, object> { ["level"] = 1L });
            queue.NextRequest();
            queue.Clear();
            Assert.IsEmpty(queue.Held);
            queue.Enqueue(AnalyticsEvents.AppOpen(true));
            StringAssert.DoesNotContain("user_id", queue.Held[0]);
            Assert.IsNotNull(queue.NextRequest());
        }

        private static int Occurrences(string text, string part)
        {
            var count = 0;
            for (var at = text.IndexOf(part, System.StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, System.StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}
