using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.Table;

namespace Hulla.Composition
{
    /// <summary>One table: the table client and, once the table screen is up, its animation scheduler.</summary>
    public sealed class TableScope : IDisposable
    {
        private readonly CancellationTokenSource _lifetime;

        public TableScope(SessionScope session, TableTicket ticket)
        {
            Session = session;
            Ticket = ticket;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime);
            Client = new TableClient(session.App.Tables, ticket, session.Player.UserId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        public SessionScope Session { get; }

        public TableTicket Ticket { get; }

        public TableClient Client { get; }

        public CancellationToken Lifetime => _lifetime.Token;

        public void Dispose()
        {
            Client.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
