using System.Collections.Generic;
using Hulla.Protocol;

namespace Hulla.Application.Analytics
{
    /// <summary>Where a batch of events goes: Firebase and GameAnalytics adapters once the accounts exist (#71), a log until then.</summary>
    public interface IAnalyticsSink
    {
        void Send(IReadOnlyList<AnalyticsEvent> batch);
    }
}
