using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.ViewModels;
using Hulla.Infrastructure.Content;
using Hulla.Infrastructure.Nakama;

namespace Hulla.Composition
{
    /// <summary>
    /// Lives for the whole run: configuration, the server connection, sign-in, the table gateway and
    /// the home screen's view model (ARCHITECTURE 9.2). Builds the session scope once signed in.
    /// </summary>
    public sealed class AppScope : IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly NakamaConnection _connection;
        private readonly NakamaSessionService _sessionService;
        private readonly NakamaTableGateway _tables;
        private SessionScope? _session;

        public AppScope(AppConfig config)
        {
            Config = config;
            _connection = NakamaConnection.Create(config.Scheme, config.Host, config.Port, config.ServerKey);
            _sessionService = new NakamaSessionService(_connection);
            _tables = new NakamaTableGateway(_connection);
            Home = new HomeViewModel(_sessionService, _tables);
        }

        public AppConfig Config { get; private set; }

        public CancellationToken Lifetime => _lifetime.Token;

        public ISessionService Sessions => _sessionService;

        public ITableGateway Tables => _tables;

        public HomeViewModel Home { get; }

        public IChatCatalog Chat { get; } = new StreamingChatCatalog();

        public SessionScope? Session => _session;

        public void UseServer(AppConfig config)
        {
            if (config.HostAndPort == Config.HostAndPort)
            {
                return;
            }

            EndSession();
            Config = config;
            _connection.Retarget(config.Scheme, config.Host, config.Port, config.ServerKey);
            _sessionService.Forget();
        }

        public SessionScope BeginSession(PlayerSession player)
        {
            if (_session != null && _session.Player.UserId == player.UserId)
            {
                return _session;
            }

            _session?.Dispose();
            _session = new SessionScope(this, player);
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
            _tables.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
