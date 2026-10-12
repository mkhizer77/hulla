using System;
using System.Threading;
using Hulla.Application.Services;

namespace Hulla.Composition
{
    /// <summary>One signed-in player; owns at most one table at a time.</summary>
    public sealed class SessionScope : IDisposable
    {
        private readonly CancellationTokenSource _lifetime;
        private TableScope? _table;

        public SessionScope(AppScope app, PlayerSession player)
        {
            App = app;
            Player = player;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime);
        }

        public AppScope App { get; }

        public PlayerSession Player { get; }

        public CancellationToken Lifetime => _lifetime.Token;

        public TableScope? Table => _table;

        public TableScope BeginTable(TableTicket ticket)
        {
            _table?.Dispose();
            _table = new TableScope(this, ticket);
            return _table;
        }

        public void EndTable()
        {
            _table?.Dispose();
            _table = null;
        }

        public void Dispose()
        {
            EndTable();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
