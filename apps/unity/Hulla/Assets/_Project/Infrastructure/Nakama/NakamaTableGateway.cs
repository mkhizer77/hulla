using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Hulla.Protocol;
using Nakama;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>The table RPCs (table_create, table_join_by_code) and the match socket for one table at a time.</summary>
    public sealed class NakamaTableGateway : ITableGateway, IDisposable
    {
        private readonly NakamaConnection _connection;
        private readonly ProtocolCodec _codec = new ProtocolCodec();
        private TableTicket? _ticket;
        private ISocket? _socket;
        private long _envelopeSeq;
        private string _tableId = "";

        public NakamaTableGateway(NakamaConnection connection)
        {
            _connection = connection;
        }

        public event Action<ServerMessage>? MessageReceived;

        public event Action? Disconnected;

        public Awaitable<Result<TableTicket>> CreateTableAsync(CancellationToken cancellation) =>
            CallAsync("table_create", new JObject { ["gameId"] = "thulla", ["mode"] = "private" }, cancellation);

        public Awaitable<Result<TableTicket>> JoinByCodeAsync(string code, CancellationToken cancellation) =>
            CallAsync("table_join_by_code", new JObject { ["code"] = code.Trim().ToUpperInvariant() }, cancellation);

        public async Awaitable<Result<bool>> EnterAsync(TableTicket ticket, CancellationToken cancellation)
        {
            var socket = _connection.Socket;
            if (socket == null)
            {
                return Result<bool>.Fail("session.not_signed_in");
            }

            Detach();
            _socket = socket;
            _ticket = ticket;
            _tableId = ticket.TableId;
            socket.ReceivedMatchState += OnMatchState;
            socket.Closed += OnClosed;
            try
            {
                // join_table metadata: the room code, the resume token for a rejoin, and whether to watch.
                var metadata = new System.Collections.Generic.Dictionary<string, string>();
                if (!ticket.IsMatchmade)
                {
                    metadata["code"] = ticket.Code;
                }

                if (!string.IsNullOrEmpty(ticket.ResumeToken))
                {
                    metadata["resumeToken"] = ticket.ResumeToken!;
                }

                if (ticket.AsSpectator)
                {
                    metadata["asSpectator"] = "true";
                }

                await socket.JoinMatchAsync(ticket.MatchId, metadata);
                return Result<bool>.Ok(true);
            }
            catch (Exception error)
            {
                Detach();
                Debug.LogWarning($"join failed: {error.Message}");
                return Result<bool>.Fail(ServerErrors.CodeOf(error, "table.join_failed"), error.GetType().Name);
            }
        }

        public void Send(string type, object payload)
        {
            var socket = _socket;
            var ticket = _ticket;
            if (socket == null || ticket == null)
            {
                return;
            }

            _envelopeSeq++;
            var json = _codec.Encode(type, _tableId, _envelopeSeq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), payload);
            SendAsync(socket, ticket.MatchId, MessageTypes.OpcodeFor(type), json);
        }

        public async Awaitable LeaveAsync()
        {
            var socket = _socket;
            var ticket = _ticket;
            Detach();
            if (socket != null && ticket != null && socket.IsConnected)
            {
                try
                {
                    await socket.LeaveMatchAsync(ticket.MatchId);
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"leave failed: {error.Message}");
                }
            }
        }

        public void Dispose() => Detach();

        private static void SendAsync(ISocket socket, string matchId, long opcode, string json)
        {
            socket.SendMatchStateAsync(matchId, opcode, json).ContinueWith(
                task => Debug.LogWarning($"send failed: {task.Exception?.GetBaseException().Message}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Awaitable<Result<TableTicket>> CallAsync(string rpc, JObject input, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<TableTicket>.Fail("session.not_signed_in");
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, rpc, input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                if ((bool?)body["ok"] != true)
                {
                    return Result<TableTicket>.Fail((string?)body["error"]?["code"] ?? "table.unavailable");
                }

                return Result<TableTicket>.Ok(new TableTicket((string)body["matchId"]!, (string)body["code"]!, (string)body["tableId"]!));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{rpc} failed: {error.Message}");
                return Result<TableTicket>.Fail("table.unavailable", error.GetType().Name);
            }
        }

        private void OnMatchState(IMatchState state)
        {
            if (_ticket == null || state.MatchId != _ticket.MatchId)
            {
                return;
            }

            var message = _codec.Decode(state.State);
            if (message.HasValue)
            {
                // A matchmade table is known only by match id until its first table_state names the
                // code; envelopes carry "t_" + code, the server's table id (shared-ts ids.tableId).
                if (_tableId.Length == 0 && message.Value.Payload is TableState first)
                {
                    _tableId = "t_" + first.Code;
                }

                MessageReceived?.Invoke(message.Value);
            }
        }

        private void OnClosed(string reason) => Disconnected?.Invoke();

        private void Detach()
        {
            if (_socket != null)
            {
                _socket.ReceivedMatchState -= OnMatchState;
                _socket.Closed -= OnClosed;
            }

            _socket = null;
            _ticket = null;
        }
    }
}
