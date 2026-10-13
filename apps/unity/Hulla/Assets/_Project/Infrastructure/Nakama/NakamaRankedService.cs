using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>ranked_get and ranked_board (GDD 7.5, 12.9).</summary>
    public sealed class NakamaRankedService : IRankedService
    {
        private readonly NakamaConnection _connection;

        public NakamaRankedService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<RankedCard>> CardAsync(CancellationToken cancellation)
        {
            var body = await CallAsync("ranked_get", new JObject(), cancellation);
            if (!body.IsOk)
            {
                return Result<RankedCard>.Fail(body.Error.Code);
            }

            return Result<RankedCard>.Ok(Card(body.Value));
        }

        public async Awaitable<Result<IReadOnlyList<RankedRow>>> BoardAsync(CancellationToken cancellation)
        {
            var body = await CallAsync("ranked_board", new JObject { ["limit"] = 50 }, cancellation);
            if (!body.IsOk)
            {
                return Result<IReadOnlyList<RankedRow>>.Fail(body.Error.Code);
            }

            var rows = new List<RankedRow>();
            if (body.Value["rows"] is JArray list)
            {
                foreach (var row in list)
                {
                    rows.Add(new RankedRow((long?)row["rank"] ?? 0, (string?)row["userId"] ?? "", (string?)row["username"] ?? "?", (int?)row["rating"] ?? 0, (string?)row["league"]));
                }
            }

            return Result<IReadOnlyList<RankedRow>>.Ok(rows);
        }

        /// <summary>A ranked_get body as the card; public for the tests.</summary>
        public static RankedCard Parse(string json) => Card(JObject.Parse(json));

        private static RankedCard Card(JObject body)
        {
            var leagues = new List<RankedLeague>();
            if (body["leagues"] is JArray list)
            {
                foreach (var league in list)
                {
                    var reward = league["reward"];
                    leagues.Add(new RankedLeague(
                        (string?)league["id"] ?? "",
                        (int?)league["minRating"] ?? 0,
                        (bool?)league["topOnly"] == true,
                        (string?)reward?["title"] ?? "",
                        (long?)reward?["coins"] ?? 0,
                        reward?["items"] is JArray items && items.Count > 0,
                        reward?["frame"] != null && reward["frame"]!.Type == JTokenType.String));
                }
            }

            var last = body["lastSeason"] is JObject season
                ? new RankedSeasonResult((string?)season["seasonId"] ?? "", (string?)season["league"], (long?)season["rank"])
                : null;
            var placement = body["placement"];
            var matchmaking = body["matchmaking"];
            return new RankedCard(
                (int?)body["season"]?["number"] ?? 1,
                (long?)body["season"]?["endTs"] ?? 0,
                (int?)body["rating"] ?? 0,
                (string?)body["league"],
                (long?)body["rank"],
                (int?)placement?["played"] ?? 0,
                (int?)placement?["of"] ?? 10,
                (int?)body["protectedRounds"] ?? 0,
                leagues,
                (int?)matchmaking?["band"] ?? 150,
                (int?)matchmaking?["widenBy"] ?? 150,
                (int?)matchmaking?["maxBand"] ?? 600,
                last,
                (bool?)body["seasonSettled"] == true);
        }

        private async Awaitable<Result<JObject>> CallAsync(string rpc, JObject input, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<JObject>.Fail("session.not_signed_in");
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, rpc, input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                if ((bool?)body["ok"] != true)
                {
                    return Result<JObject>.Fail((string?)body["error"]?["code"] ?? "table.unavailable");
                }

                return Result<JObject>.Ok(body);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{rpc}: {error.Message}");
                return Result<JObject>.Fail("table.unavailable", error.GetType().Name);
            }
        }
    }
}
