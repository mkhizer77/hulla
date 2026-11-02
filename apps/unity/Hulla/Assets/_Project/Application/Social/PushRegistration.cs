using System;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Social
{
    /// <summary>What push_register takes: this install, its FCM token and where it is.</summary>
    public sealed class PushDeviceInput
    {
        public PushDeviceInput(string installId, string token, string platform, string locale, int tzOffsetMinutes)
        {
            InstallId = installId;
            Token = token;
            Platform = platform;
            Locale = locale;
            TzOffsetMinutes = tzOffsetMinutes;
        }

        public string InstallId { get; }

        public string Token { get; }

        /// <summary>"android" or "ios".</summary>
        public string Platform { get; }

        public string Locale { get; }

        public int TzOffsetMinutes { get; }
    }

    /// <summary>push_register on the server (GDD 10.10).</summary>
    public interface IPushRegistry
    {
        Awaitable<Result<bool>> RegisterAsync(PushDeviceInput device, CancellationToken cancellation);
    }

    /// <summary>
    /// Sends the phone's push token once the player is signed in, again when the token or the player
    /// changes, and never twice for the same pair. A failed send is tried at the next sign-in.
    /// </summary>
    public sealed class PushRegistration
    {
        private readonly IPushRegistry _registry;
        private readonly string _installId;
        private readonly string _platform;
        private readonly Func<string> _locale;
        private string? _token;
        private string? _userId;
        private string? _sent;
        private bool _sending;

        public PushRegistration(IPushRegistry registry, string installId, string platform, Func<string> locale)
        {
            _registry = registry;
            _installId = installId;
            _platform = platform;
            _locale = locale;
        }

        public void TokenReceived(string token, CancellationToken cancellation)
        {
            _token = token;
            _ = SendAsync(cancellation);
        }

        public void SignedIn(string userId, CancellationToken cancellation)
        {
            _userId = userId;
            _ = SendAsync(cancellation);
        }

        private async Awaitable SendAsync(CancellationToken cancellation)
        {
            if (_token == null || _userId == null || _sending)
            {
                return;
            }

            var key = _userId + "/" + _token;
            if (key == _sent)
            {
                return;
            }

            _sending = true;
            try
            {
                var offset = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes;
                var result = await _registry.RegisterAsync(new PushDeviceInput(_installId, _token, _platform, _locale(), offset), cancellation);
                if (result.IsOk)
                {
                    _sent = key;
                }
            }
            finally
            {
                _sending = false;
            }

            // The token or the player changed while the call was out.
            if (_userId + "/" + _token != key)
            {
                await SendAsync(cancellation);
            }
        }
    }
}
