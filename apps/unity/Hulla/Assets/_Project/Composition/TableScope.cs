using System;
using System.Threading;

namespace Hulla.Composition
{
    /// <summary>One table: the table client, the animation scheduler and the active game module's objects.</summary>
    public sealed class TableScope : IDisposable
    {
        private readonly CancellationTokenSource _lifetime;

        public TableScope(SessionScope session, string matchId)
        {
            Session = session;
            MatchId = matchId;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime);
        }

        public SessionScope Session { get; }

        public string MatchId { get; }

        public CancellationToken Lifetime => _lifetime.Token;

        public void Dispose()
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
