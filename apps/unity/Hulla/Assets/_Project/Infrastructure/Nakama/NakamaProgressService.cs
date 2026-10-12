using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaProgressService : IProgressService
    {
        private readonly NakamaConnection _connection;

        public NakamaProgressService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<ProgressInfo>> ProgressAsync(int tzOffsetMinutes, CancellationToken cancellation)
        {
            var body = await RpcAsync("progress_get", new JObject { ["tzOffsetMinutes"] = tzOffsetMinutes }, cancellation);
            if (!body.IsOk)
            {
                return Result<ProgressInfo>.Fail(body.Error);
            }

            var b = body.Value;
            var titles = new List<string>();
            if (b["titles"] is JArray list)
            {
                foreach (var t in list)
                {
                    titles.Add((string)t!);
                }
            }

            RewardInfo? reward = null;
            if (b["lastReward"] is JObject r)
            {
                reward = new RewardInfo(
                    (string?)r["roundId"] ?? "",
                    (long?)r["xp"] ?? 0,
                    (long?)r["coins"] ?? 0,
                    (long?)r["levelFrom"] ?? 0,
                    (long?)r["levelTo"] ?? 0,
                    (bool?)r["breakdown"]?["capped"] == true);
            }

            return Result<ProgressInfo>.Ok(new ProgressInfo(
                (long?)b["level"] ?? 1,
                (long?)b["xp"] ?? 0,
                (long?)b["intoLevel"] ?? 0,
                (long?)b["toNext"] ?? 0,
                (long?)b["coinsToday"] ?? 0,
                (long?)b["loginStreak"] ?? 0,
                titles,
                reward));
        }

        public async Awaitable<Result<(long Coins, long Gems)>> BalancesAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("wallet_get", new JObject(), cancellation);
            if (!body.IsOk)
            {
                return Result<(long Coins, long Gems)>.Fail(body.Error);
            }

            var balances = body.Value["balances"];
            return Result<(long Coins, long Gems)>.Ok(((long?)balances?["coins"] ?? 0, (long?)balances?["gems"] ?? 0));
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
