using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Tests
{
    internal sealed class FakeGateway : ITableGateway
    {
        public readonly List<(string Type, object Payload)> Sent = new List<(string Type, object Payload)>();

        public event Action<ServerMessage>? MessageReceived;

        public event Action? Disconnected;

        public void Deliver(ServerMessage message) => MessageReceived?.Invoke(message);

        public void Drop() => Disconnected?.Invoke();

        public Awaitable<Result<TableTicket>> CreateTableAsync(CancellationToken cancellation) => throw new NotSupportedException();

        public Awaitable<Result<TableTicket>> JoinByCodeAsync(string code, CancellationToken cancellation) => throw new NotSupportedException();

        public Awaitable<Result<bool>> EnterAsync(TableTicket ticket, CancellationToken cancellation) => throw new NotSupportedException();

        public void Send(string type, object payload) => Sent.Add((type, payload));

        public Awaitable LeaveAsync() => throw new NotSupportedException();
    }
}
