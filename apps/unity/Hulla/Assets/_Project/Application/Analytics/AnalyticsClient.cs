using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hulla.Protocol;

namespace Hulla.Application.Analytics
{
    /// <summary>
    /// Analytics behind consent (GDD 23.6, ARCHITECTURE 12.4). Before the player agrees nothing is kept;
    /// after, events queue and leave in batches, every few seconds, at a size limit or when the app
    /// pauses. An event carrying a name, chat text or a card list is dropped and counted, never sent.
    /// </summary>
    public sealed class AnalyticsClient
    {
        public const int BatchSize = 20;
        public const float FlushSeconds = 10f;

        private static readonly HashSet<string> ForbiddenKeys = new HashSet<string>
        {
            "name", "display_name", "username", "handle", "email", "phone", "text", "message", "chat", "hand", "hands", "cards_list", "deck", "seed",
        };

        private static readonly Regex CardList = new Regex(@"\b[2-9TJQKA][SHCD]\b(?:[\s,;|]+\b[2-9TJQKA][SHCD]\b)+");

        private readonly IAnalyticsSink _sink;
        private readonly List<AnalyticsEvent> _queue = new List<AnalyticsEvent>();
        private float _sinceFlush;

        public AnalyticsClient(IAnalyticsSink sink)
        {
            _sink = sink;
        }

        public bool Consented { get; private set; }

        public int Dropped { get; private set; }

        public int Queued => _queue.Count;

        public void SetConsent(bool consented)
        {
            Consented = consented;
            if (!consented)
            {
                _queue.Clear();
            }
        }

        public void Track(AnalyticsEvent analyticsEvent)
        {
            if (!Consented)
            {
                return;
            }

            if (!IsClean(analyticsEvent))
            {
                Dropped++;
                return;
            }

            _queue.Add(analyticsEvent);
            if (_queue.Count >= BatchSize)
            {
                Flush();
            }
        }

        public void Tick(float deltaSeconds)
        {
            _sinceFlush += deltaSeconds;
            if (_sinceFlush >= FlushSeconds)
            {
                Flush();
            }
        }

        public void Flush()
        {
            _sinceFlush = 0f;
            if (_queue.Count == 0 || !Consented)
            {
                return;
            }

            var batch = _queue.ToArray();
            _queue.Clear();
            _sink.Send(batch);
        }

        /// <summary>No personal data, chat or hidden cards (GDD 23.6): checked on every event, not just the generated ones.</summary>
        public static bool IsClean(AnalyticsEvent analyticsEvent)
        {
            foreach (var pair in analyticsEvent.Parameters)
            {
                if (ForbiddenKeys.Contains(pair.Key))
                {
                    return false;
                }

                switch (pair.Value)
                {
                    case string text when CardList.IsMatch(text):
                        return false;
                    case System.Collections.IEnumerable list when !(pair.Value is string):
                        return false;
                }
            }

            return true;
        }
    }
}
