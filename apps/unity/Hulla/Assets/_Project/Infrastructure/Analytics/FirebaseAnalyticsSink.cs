using System.Collections.Generic;
using Hulla.Application.Analytics;
using Hulla.Infrastructure.Platform;
using Hulla.Protocol;

namespace Hulla.Infrastructure.Analytics
{
    /// <summary>Hands each batch to Firebase Analytics, and to the log in development builds (#71).</summary>
    public sealed class FirebaseAnalyticsSink : IAnalyticsSink
    {
        private readonly FirebaseServices _firebase;
        private readonly IAnalyticsSink _log;

        public FirebaseAnalyticsSink(FirebaseServices firebase, IAnalyticsSink log)
        {
            _firebase = firebase;
            _log = log;
        }

        public void Send(IReadOnlyList<AnalyticsEvent> batch)
        {
            foreach (var analyticsEvent in batch)
            {
                _firebase.Log(analyticsEvent);
            }

            _log.Send(batch);
        }

        /// <summary>Firebase keeps its own anonymous instance id; the account and its properties go to Amplitude only.</summary>
        public void Identify(string? userId, IReadOnlyDictionary<string, object> properties) => _log.Identify(userId, properties);

        /// <summary>Firebase's collection is switched off by <see cref="FirebaseServices.SetConsent"/>.</summary>
        public void Forget()
        {
        }
    }
}
