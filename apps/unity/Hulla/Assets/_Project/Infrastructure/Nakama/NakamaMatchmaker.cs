using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Nakama;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>
    /// Quick Play tickets on Nakama's matchmaker, shaped like server/nakama/integration/matchmaking:
    /// string properties game, mode and lane, numeric seats, three to <c>seats</c> players. The
    /// matchmaker_matched hook opens the table and the matched event carries its match id.
    /// </summary>
    public sealed class NakamaMatchmaker : IMatchmaker, IDisposable
    {
        private const string Lane = "any";
        private const int MinimumPlayers = 3;
        private readonly NakamaConnection _connection;
        private ISocket? _socket;
        private IMatchmakerTicket? _ticket;

        public NakamaMatchmaker(NakamaConnection connection)
        {
            _connection = connection;
        }

        public event Action<TableTicket>? Matched;

        public static string Query(int seats, bool anySize) =>
            "+properties.game:thulla +properties.mode:quick +properties.lane:" + Lane +
            (anySize ? "" : " +properties.seats:" + seats);

        public async Awaitable<Result<bool>> SearchAsync(int seats, bool anySize, CancellationToken cancellation)
        {
            var socket = _connection.Socket;
            if (socket == null)
            {
                return Result<bool>.Fail("session.not_signed_in");
            }

            Attach(socket);
            try
            {
                await RemoveTicketAsync();
                cancellation.ThrowIfCancellationRequested();
                _ticket = await socket.AddMatchmakerAsync(
                    Query(seats, anySize),
                    MinimumPlayers,
                    seats,
                    new Dictionary<string, string> { ["game"] = "thulla", ["mode"] = "quick", ["lane"] = Lane },
                    new Dictionary<string, double> { ["seats"] = seats });
                return Result<bool>.Ok(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"matchmaker: {error.Message}");
                return Result<bool>.Fail("matchmaker.unavailable", error.GetType().Name);
            }
        }

        public const int RankedSeats = 4;

        public async Awaitable<Result<bool>> SearchRankedAsync(int band, CancellationToken cancellation)
        {
            var socket = _connection.Socket;
            if (socket == null)
            {
                return Result<bool>.Fail("session.not_signed_in");
            }

            Attach(socket);
            try
            {
                await RemoveTicketAsync();
                cancellation.ThrowIfCancellationRequested();
                // The server's matchmaker hook rewrites the query with the stored rating and this band.
                _ticket = await socket.AddMatchmakerAsync(
                    "+properties.game:thulla +properties.mode:ranked",
                    RankedSeats,
                    RankedSeats,
                    new Dictionary<string, string> { ["game"] = "thulla", ["mode"] = "ranked" },
                    new Dictionary<string, double> { ["band"] = band });
                return Result<bool>.Ok(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"ranked matchmaker: {error.Message}");
                return Result<bool>.Fail("matchmaker.unavailable", error.GetType().Name);
            }
        }

        public async Awaitable<Result<TableTicket>> BotTableAsync(int seats, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<TableTicket>.Fail("session.not_signed_in");
            }

            try
            {
                var input = new Newtonsoft.Json.Linq.JObject { ["seats"] = seats };
                var response = await _connection.Client.RpcAsync(session, "quick_play_bots", input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = Newtonsoft.Json.Linq.JObject.Parse(response.Payload);
                if ((bool?)body["ok"] != true)
                {
                    return Result<TableTicket>.Fail((string?)body["error"]?["code"] ?? "matchmaker.unavailable");
                }

                return Result<TableTicket>.Ok(new TableTicket((string)body["matchId"]!, (string)body["code"]!, (string)body["tableId"]!));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"quick_play_bots failed: {error.Message}");
                return Result<TableTicket>.Fail("matchmaker.unavailable", error.GetType().Name);
            }
        }

        public async Awaitable CancelAsync()
        {
            try
            {
                await RemoveTicketAsync();
            }
            catch (Exception error)
            {
                Debug.LogWarning($"matchmaker cancel: {error.Message}");
            }
        }

        public void Dispose()
        {
            if (_socket != null)
            {
                _socket.ReceivedMatchmakerMatched -= OnMatched;
            }

            _socket = null;
        }

        private async Awaitable RemoveTicketAsync()
        {
            var ticket = _ticket;
            _ticket = null;
            if (ticket != null && _socket != null && _socket.IsConnected)
            {
                await _socket.RemoveMatchmakerAsync(ticket);
            }
        }

        private void Attach(ISocket socket)
        {
            if (_socket == socket)
            {
                return;
            }

            Dispose();
            _socket = socket;
            socket.ReceivedMatchmakerMatched += OnMatched;
        }

        private void OnMatched(IMatchmakerMatched matched)
        {
            if (_ticket == null || matched.Ticket != _ticket.Ticket || string.IsNullOrEmpty(matched.MatchId))
            {
                return;
            }

            _ticket = null;
            Matched?.Invoke(TableTicket.Matchmade(matched.MatchId));
        }
    }
}
