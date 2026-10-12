using System;
using System.Threading;

namespace Hulla.Composition
{
    /// <summary>Authenticated services for one signed-in session; owns at most one table at a time.</summary>
    public sealed class SessionScope : IDisposable
    {
        private readonly CancellationTokenSource _lifetime;
        private TableScope? _table;

        public SessionScope(AppScope app)
        {
            App = app;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime);
        }

        public AppScope App { get; }

        public CancellationToken Lifetime => _lifetime.Token;

        public TableScope? Table => _table;

        public TableScope BeginTable(string matchId)
        {
            _table?.Dispose();
            _table = new TableScope(this, matchId);
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
