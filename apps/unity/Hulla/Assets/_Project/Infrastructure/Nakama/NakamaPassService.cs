using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaPassService : IPassService
    {
        private readonly NakamaConnection _connection;

        public NakamaPassService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<PassInfo?>> PassAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("pass_get", new JObject(), cancellation);
            if (!body.IsOk)
            {
                return Result<PassInfo?>.Fail(body.Error);
            }

            var b = body.Value;
            if (b["season"] is not JObject season)
            {
                return Result<PassInfo?>.Ok(null);
            }

            var tiers = new List<PassTier>();
            if (b["tiers"] is JArray list)
            {
                foreach (var t in list)
                {
                    tiers.Add(new PassTier((long?)t["tier"] ?? 0, (bool?)t["reached"] == true, Reward(t["free"]), Reward(t["premium"])));
                }
            }

            return Result<PassInfo?>.Ok(new PassInfo(
                (string?)season["name"] ?? "",
                (long?)season["endTs"] ?? 0,
                (long?)season["xpPerTier"] ?? 1000,
                (long?)b["xp"] ?? 0,
                (long?)b["tier"] ?? 0,
                (long?)b["intoTier"] ?? 0,
                (bool?)b["premium"] == true,
                (bool?)b["catchUp"] == true,
                tiers));
        }

        public async Awaitable<Result<bool>> ClaimAsync(long tier, bool premium, CancellationToken cancellation)
        {
            var body = await RpcAsync("pass_claim", new JObject { ["tier"] = tier, ["track"] = premium ? "premium" : "free" }, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error);
        }

        private static PassReward Reward(JToken? r)
        {
            var items = new List<string>();
            if (r?["items"] is JArray list)
            {
                foreach (var id in list)
                {
                    items.Add((string)id!);
                }
            }

            return new PassReward((long?)r?["coins"] ?? 0, (long?)r?["gems"] ?? 0, items, (string?)r?["title"], (bool?)r?["claimed"] == true);
        }

        private async Awaitable<Result<JObject>> RpcAsync(string id, JObject input, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<JObject>.Fail("session.not_signed_in");
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, id, input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                return (bool?)body["ok"] == true
                    ? Result<JObject>.Ok(body)
                    : Result<JObject>.Fail((string?)body["error"]?["code"] ?? "economy.unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{id}: {error.Message}");
                return Result<JObject>.Fail("economy.unavailable", error.GetType().Name);
            }
        }
    }
}
