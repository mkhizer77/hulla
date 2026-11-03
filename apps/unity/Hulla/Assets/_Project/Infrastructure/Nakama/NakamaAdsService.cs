using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Ads;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaAdsService : IAdsService
    {
        private readonly NakamaConnection _connection;

        public NakamaAdsService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<AdsInfo>> GetAsync(int tzOffsetMinutes, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<AdsInfo>.Fail("session.not_signed_in");
            }

            try
            {
                var input = new JObject { ["tzOffsetMinutes"] = tzOffsetMinutes };
                var response = await _connection.Client.RpcAsync(session, "ads_get", input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                if ((bool?)body["ok"] != true)
                {
                    return Result<AdsInfo>.Fail((string?)body["error"]?["code"] ?? "economy.ads_unavailable");
                }

                var interstitials = body["interstitials"];
                return Result<AdsInfo>.Ok(new AdsInfo(
                    (bool?)body["rewardedEnabled"] == true,
                    Counts(body["caps"]),
                    Counts(body["used"]),
                    (int?)body["rerollCredits"] ?? 0,
                    (bool?)interstitials?["enabled"] == true,
                    (int?)interstitials?["fromRound"] ?? 4,
                    (int?)interstitials?["intervalSeconds"] ?? 240));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"ads_get: {error.GetType().Name}");
                return Result<AdsInfo>.Fail("economy.ads_unavailable", error.GetType().Name);
            }
        }

        private static IReadOnlyDictionary<string, int> Counts(JToken? token)
        {
            var counts = new Dictionary<string, int>();
            if (token is JObject values)
            {
                foreach (var pair in values)
                {
                    counts[pair.Key] = (int?)pair.Value ?? 0;
                }
            }

            return counts;
        }
    }
}
