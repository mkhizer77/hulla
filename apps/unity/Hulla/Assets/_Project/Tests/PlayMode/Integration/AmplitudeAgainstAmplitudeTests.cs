using System;
using System.Collections;
using System.Collections.Generic;
using Hulla.Infrastructure.Analytics;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>
    /// The real sink against a real Amplitude project (docs/runbooks/amplitude-setup.md). Runs only when
    /// HULLA_AMPLITUDE_KEY is set, with the dev project's key; HULLA_AMPLITUDE_REGION=us for a US project.
    /// </summary>
    public sealed class AmplitudeAgainstAmplitudeTests
    {
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AnEventAndWhatIsKnownAboutThePlayerAreAccepted()
        {
            var key = Environment.GetEnvironmentVariable("HULLA_AMPLITUDE_KEY");
            if (string.IsNullOrEmpty(key))
            {
                Assert.Ignore("HULLA_AMPLITUDE_KEY is not set");
            }

            var eu = !string.Equals(Environment.GetEnvironmentVariable("HULLA_AMPLITUDE_REGION"), "us", StringComparison.OrdinalIgnoreCase);
            var sink = new AmplitudeSink(key!, eu);
            try
            {
                sink.Identify(null, new Dictionary<string, object> { ["level"] = 1L, ["locale"] = "en" });
                sink.Send(new[]
                {
                    AnalyticsEvents.AppOpen(true, "none"),
                    AnalyticsEvents.ScreenView("home", false),
                    AnalyticsEvents.RoundEnd(12.5, seats: 4, humans: 1, bots: 3, friends: 0, lost: false, moves: 13),
                    // A per-move event must not leave for Amplitude.
                    AnalyticsEvents.Pickup(3),
                });
                Assert.AreEqual(4, sink.Held, "the identity and three events wait; the pick-up is left out");
                var deadline = Time.realtimeSinceStartup + 30f;
                while (sink.Held > 0 && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.AreEqual(0, sink.Held, "Amplitude answered");
                Assert.AreEqual(200, sink.LastStatus, "and took the events");
            }
            finally
            {
                sink.Forget();
            }
        }
    }
}
