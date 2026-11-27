using System.Collections.Generic;
using Hulla.Protocol;

namespace Hulla.Application.Analytics
{
    /// <summary>Where a batch of events goes: Firebase, Amplitude, and the log in development builds (#71).</summary>
    public interface IAnalyticsSink
    {
        void Send(IReadOnlyList<AnalyticsEvent> batch);

        /// <summary>
        /// Whose events these are and what is known about the player (level, payer, settings): the
        /// account's id, or null before sign-in, and only the properties that changed. Never a name.
        /// </summary>
        void Identify(string? userId, IReadOnlyDictionary<string, object> properties);

        /// <summary>The player said no to usage data: drop what is held and any id made for this device.</summary>
        void Forget();
    }
}
