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

        private readonly IReadOnlyList<IAnalyticsSink> _sinks;
        private readonly List<AnalyticsEvent> _queue = new List<AnalyticsEvent>();
        private readonly Dictionary<string, object> _context = new Dictionary<string, object>();
        private readonly Dictionary<string, object> _properties = new Dictionary<string, object>();
        private readonly Dictionary<string, object> _sentProperties = new Dictionary<string, object>();
        private string? _userId;
        private string? _sentUserId;
        private bool _identified;
        private bool _forgotten;
        private float _sinceFlush;

        public AnalyticsClient(params IAnalyticsSink[] sinks)
        {
            _sinks = sinks;
        }

        public bool Consented { get; private set; }

        public int Dropped { get; private set; }

        public int Queued => _queue.Count;

        public void SetConsent(bool consented)
        {
            Consented = consented;
            if (consented)
            {
                _forgotten = false;
                SendIdentity();
                return;
            }

            _queue.Clear();
            _sentProperties.Clear();
            _identified = false;
            if (!_forgotten)
            {
                _forgotten = true;
                foreach (var sink in _sinks)
                {
                    sink.Forget();
                }
            }
        }

        /// <summary>
        /// Goes on every event until cleared with null: the game and the kind of table while the player
        /// is at one (ARCHITECTURE 12.4). An event's own parameter of the same name wins.
        /// </summary>
        public void SetContext(string key, object? value)
        {
            if (value == null)
            {
                _context.Remove(key);
            }
            else
            {
                _context[key] = value;
            }
        }

        /// <summary>The signed-in account's id (null when none) and what is known about the player; only changes are passed on.</summary>
        public void Identify(string? userId, IReadOnlyDictionary<string, object> properties)
        {
            if (userId != _userId)
            {
                // Another account: nothing known about the last one describes this one.
                _properties.Clear();
                _sentProperties.Clear();
            }

            _userId = userId;
            foreach (var pair in properties)
            {
                if (!ForbiddenKeys.Contains(pair.Key))
                {
                    _properties[pair.Key] = pair.Value;
                }
            }

            SendIdentity();
        }

        public void Track(AnalyticsEvent analyticsEvent)
        {
            if (!Consented)
            {
                return;
            }

            var withContext = WithContext(analyticsEvent);
            if (!IsClean(withContext))
            {
                Dropped++;
                return;
            }

            _queue.Add(withContext);
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
            foreach (var sink in _sinks)
            {
                sink.Send(batch);
            }
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

        private AnalyticsEvent WithContext(AnalyticsEvent analyticsEvent)
        {
            if (_context.Count == 0)
            {
                return analyticsEvent;
            }

            var merged = new Dictionary<string, object>(_context);
            foreach (var pair in analyticsEvent.Parameters)
            {
                merged[pair.Key] = pair.Value;
            }

            return new AnalyticsEvent(analyticsEvent.Name, merged);
        }

        private void SendIdentity()
        {
            if (!Consented)
            {
                return;
            }

            var changed = new Dictionary<string, object>();
            foreach (var pair in _properties)
            {
                if (!_sentProperties.TryGetValue(pair.Key, out var sent) || !Equals(sent, pair.Value))
                {
                    changed[pair.Key] = pair.Value;
                    _sentProperties[pair.Key] = pair.Value;
                }
            }

            if (_identified && changed.Count == 0 && _sentUserId == _userId)
            {
                return;
            }

            _identified = true;
            _sentUserId = _userId;
            // Events before this moment belong to whoever was signed in when they happened.
            Flush();
            foreach (var sink in _sinks)
            {
                sink.Identify(_userId, changed);
            }
        }
    }
}
