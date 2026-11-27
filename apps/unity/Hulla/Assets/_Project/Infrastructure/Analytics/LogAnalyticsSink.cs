using System.Collections.Generic;
using System.Text;
using Hulla.Application.Analytics;
using Hulla.Protocol;
using UnityEngine;

namespace Hulla.Infrastructure.Analytics
{
    /// <summary>Writes batches to the log in development builds, next to Firebase (#71).</summary>
    public sealed class LogAnalyticsSink : IAnalyticsSink
    {
        public void Send(IReadOnlyList<AnalyticsEvent> batch)
        {
            if (!Debug.isDebugBuild)
            {
                return;
            }

            var text = new StringBuilder("[analytics]");
            foreach (var e in batch)
            {
                text.Append(' ').Append(e.Name).Append('{');
                var first = true;
                foreach (var pair in e.Parameters)
                {
                    text.Append(first ? "" : ",").Append(pair.Key).Append('=').Append(pair.Value);
                    first = false;
                }

                text.Append('}');
            }

            Debug.Log(text.ToString());
        }

        public void Identify(string? userId, IReadOnlyDictionary<string, object> properties)
        {
            if (!Debug.isDebugBuild || properties.Count == 0)
            {
                return;
            }

            var text = new StringBuilder("[analytics] player{");
            var first = true;
            foreach (var pair in properties)
            {
                text.Append(first ? "" : ",").Append(pair.Key).Append('=').Append(pair.Value);
                first = false;
            }

            Debug.Log(text.Append('}').ToString());
        }

        public void Forget()
        {
        }
    }
}
