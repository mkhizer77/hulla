using System;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaDailyRewardService : IDailyRewardService
    {
        private readonly NakamaConnection _connection;

        public NakamaDailyRewardService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<DailyRewardStatus>> StatusAsync(int tzOffsetMinutes, CancellationToken cancellation)
        {
            var body = await RpcAsync("daily_reward_get", new JObject { ["tzOffsetMinutes"] = tzOffsetMinutes }, cancellation);
            return body.IsOk
                ? Result<DailyRewardStatus>.Ok(new DailyRewardStatus((bool?)body.Value["claimable"] == true, Day(body.Value["today"]), Day(body.Value["next"])))
                : Result<DailyRewardStatus>.Fail(body.Error);
        }

        public async Awaitable<Result<DailyRewardStatus>> ClaimAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("daily_reward_claim", new JObject(), cancellation);
            return body.IsOk
                ? Result<DailyRewardStatus>.Ok(new DailyRewardStatus(false, Day(body.Value["reward"]), Day(body.Value["next"])))
                : Result<DailyRewardStatus>.Fail(body.Error);
        }

        private static DailyRewardDay Day(JToken? day) =>
            new DailyRewardDay((long?)day?["day"] ?? 1, (long?)day?["coins"] ?? 0, (long?)day?["gems"] ?? 0);

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
