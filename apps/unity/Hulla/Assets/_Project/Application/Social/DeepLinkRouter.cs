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
        private bool _pendingReferral;
        private float _pendingAt;
        private bool _ready;
        private string? _lastUrl;
        private float _lastAt = float.NegativeInfinity;

        /// <summary>The room code and when its link arrived, on the caller's clock.</summary>
        public event Action<string, float>? InviteOpened;

        /// <summary>The sharer's friend code from a referral link (GDD 10.9).</summary>
        public event Action<string>? ReferralOpened;

        public void Receive(string? url, float now)
        {
            var code = InviteLink.CodeFrom(url);
            var friendCode = code == null ? InviteLink.FriendCodeFrom(url) : null;
            if ((code == null && friendCode == null) || (url == _lastUrl && now - _lastAt < RepeatWindowSeconds))
            {
                return;
            }

            _lastUrl = url;
            _lastAt = now;
            if (_ready)
            {
                Hand(code ?? friendCode!, friendCode != null, now);
                return;
            }

            _pending = code ?? friendCode;
            _pendingReferral = friendCode != null;
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
            Hand(code, _pendingReferral, _pendingAt);
            return true;
        }

        public bool HasPending => _pending != null;

        /// <summary>The app_open deep_link_type for what is held: none, invite or referral.</summary>
        public string PendingType => _pending == null ? "none" : _pendingReferral ? "referral" : "invite";

        private void Hand(string code, bool referral, float at)
        {
            if (referral)
            {
                ReferralOpened?.Invoke(code);
            }
            else
            {
                InviteOpened?.Invoke(code, at);
            }
        }
    }
}
