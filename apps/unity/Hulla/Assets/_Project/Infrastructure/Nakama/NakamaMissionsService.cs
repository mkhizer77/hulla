using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaMissionsService : IMissionsService
    {
        private readonly NakamaConnection _connection;

        public NakamaMissionsService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<(IReadOnlyList<MissionInfo> Missions, bool RerollAvailable)>> MissionsAsync(int tzOffsetMinutes, CancellationToken cancellation)
        {
            var body = await RpcAsync("missions_get", new JObject { ["tzOffsetMinutes"] = tzOffsetMinutes }, cancellation);
            if (!body.IsOk)
            {
                return Result<(IReadOnlyList<MissionInfo> Missions, bool RerollAvailable)>.Fail(body.Error);
            }

            var missions = new List<MissionInfo>();
            Read(body.Value["daily"], true, missions);
            Read(body.Value["weekly"], false, missions);
            return Result<(IReadOnlyList<MissionInfo> Missions, bool RerollAvailable)>.Ok((missions, (bool?)body.Value["rerollAvailable"] == true));
        }

        public async Awaitable<Result<bool>> ClaimAsync(string missionId, CancellationToken cancellation)
        {
            var body = await RpcAsync("missions_claim", new JObject { ["missionId"] = missionId }, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error);
        }

        public async Awaitable<Result<bool>> RerollAsync(string missionId, CancellationToken cancellation)
        {
            var body = await RpcAsync("missions_reroll", new JObject { ["missionId"] = missionId }, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error);
        }

        private static void Read(JToken? list, bool daily, List<MissionInfo> into)
        {
            if (list is not JArray items)
            {
                return;
            }

            foreach (var m in items)
            {
                into.Add(new MissionInfo(
                    (string?)m["id"] ?? "",
                    (string?)m["metric"] ?? "",
                    (long?)m["target"] ?? 0,
                    (long?)m["progress"] ?? 0,
                    (bool?)m["claimed"] == true,
                    (bool?)m["claimable"] == true,
                    (long?)m["reward"]?["coins"] ?? 0,
                    (long?)m["reward"]?["gems"] ?? 0,
                    daily));
            }
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
                    : Result<JObject>.Fail((string?)body["error"]?["code"] ?? "missions.unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{id}: {error.Message}");
                return Result<JObject>.Fail("missions.unavailable", error.GetType().Name);
            }
        }
    }
}
