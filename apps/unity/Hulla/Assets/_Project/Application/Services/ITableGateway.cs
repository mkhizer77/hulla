using System;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>
    /// The table RPCs and the match socket. Messages arrive on the main thread, already decoded into
    /// generated DTOs; the gateway never interprets them.
    /// </summary>
    public interface ITableGateway
    {
        event Action<ServerMessage>? MessageReceived;

        event Action? Disconnected;

        Awaitable<Result<TableTicket>> CreateTableAsync(CancellationToken cancellation);

        Awaitable<Result<TableTicket>> JoinByCodeAsync(string code, CancellationToken cancellation);

        Awaitable<Result<bool>> EnterAsync(TableTicket ticket, CancellationToken cancellation);

        void Send(string type, object payload);

        Awaitable LeaveAsync();
    }
}
