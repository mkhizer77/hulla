using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaLeaderboardService : ILeaderboardService
    {
        private readonly NakamaConnection _connection;

        public NakamaLeaderboardService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<LeaderboardPage>> BoardAsync(LeaderboardPeriod period, bool friends, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<LeaderboardPage>.Fail("session.not_signed_in");
            }

            var input = new JObject
            {
                ["board"] = period == LeaderboardPeriod.Monthly ? "monthly" : "weekly",
                ["scope"] = friends ? "friends" : "global",
                ["limit"] = 50,
            };
            try
            {
                var response = await _connection.Client.RpcAsync(session, "leaderboard_get", input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                if ((bool?)body["ok"] != true)
                {
                    return Result<LeaderboardPage>.Fail((string?)body["error"]?["code"] ?? "economy.unavailable");
                }

                var rows = new List<LeaderboardRow>();
                if (body["rows"] is JArray list)
                {
                    foreach (var row in list)
                    {
                        rows.Add(Row(row));
                    }
                }

                var me = body["me"] is JObject mine ? Row(mine) : null;
                return Result<LeaderboardPage>.Ok(new LeaderboardPage(rows, me, (long?)body["nextResetTs"]));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"leaderboard_get: {error.Message}");
                return Result<LeaderboardPage>.Fail("economy.unavailable", error.GetType().Name);
            }
        }

        private static LeaderboardRow Row(JToken row) =>
            new LeaderboardRow((long?)row["rank"] ?? 0, (string?)row["userId"] ?? "", (string?)row["username"] ?? "?", (long?)row["score"] ?? 0, (long?)row["rounds"] ?? 0);
    }
}
