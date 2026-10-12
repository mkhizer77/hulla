using System;
using System.Threading;

namespace Hulla.Composition
{
    /// <summary>
    /// Lives for the whole run: configuration and the services every screen needs (ARCHITECTURE 9.2).
    /// Builds the session scope once the player is signed in.
    /// </summary>
    public sealed class AppScope : IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private SessionScope? _session;

        public AppScope(AppConfig config)
        {
            Config = config;
        }

        public AppConfig Config { get; }

        public CancellationToken Lifetime => _lifetime.Token;

        public SessionScope? Session => _session;

        public SessionScope BeginSession()
        {
            _session?.Dispose();
            _session = new SessionScope(this);
            return _session;
        }

        public void EndSession()
        {
            _session?.Dispose();
            _session = null;
        }

        public void Dispose()
        {
            EndSession();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
