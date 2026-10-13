using System;

namespace Hulla.Application.Social
{
    /// <summary>
    /// Holds a link that arrives before the app can act on it (a cold start) and hands it over once the
    /// flow is ready; later links (a warm start) go straight through. Android can report a cold-start
    /// link twice, so the same link within a few seconds is dropped.
    /// </summary>
    public sealed class DeepLinkRouter
    {
        private const float RepeatWindowSeconds = 3f;

        private string? _pending;
        private float _pendingAt;
        private bool _ready;
        private string? _lastUrl;
        private float _lastAt = float.NegativeInfinity;

        /// <summary>The room code and when its link arrived, on the caller's clock.</summary>
        public event Action<string, float>? InviteOpened;

        public void Receive(string? url, float now)
        {
            var code = InviteLink.CodeFrom(url);
            if (code == null || (url == _lastUrl && now - _lastAt < RepeatWindowSeconds))
            {
                return;
            }

            _lastUrl = url;
            _lastAt = now;
            if (_ready)
            {
                InviteOpened?.Invoke(code, now);
                return;
            }

            _pending = code;
            _pendingAt = now;
        }

        /// <returns>True when a held link was handed over.</returns>
        public bool Ready()
        {
            _ready = true;
            if (_pending == null)
            {
                return false;
            }

            var code = _pending;
            _pending = null;
            InviteOpened?.Invoke(code, _pendingAt);
            return true;
        }

        public bool HasPending => _pending != null;
    }
}
