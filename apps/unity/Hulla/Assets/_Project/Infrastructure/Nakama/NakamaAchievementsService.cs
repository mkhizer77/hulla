using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaAchievementsService : IAchievementsService
    {
        private readonly NakamaConnection _connection;

        public NakamaAchievementsService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<(IReadOnlyList<AchievementInfo> Achievements, IReadOnlyList<string> UnlockedNow)>> AchievementsAsync(CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<(IReadOnlyList<AchievementInfo>, IReadOnlyList<string>)>.Fail("session.not_signed_in");
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, "achievements_get", "{}", null, cancellation);
                var body = JObject.Parse(response.Payload);
                if ((bool?)body["ok"] != true)
                {
                    return Result<(IReadOnlyList<AchievementInfo>, IReadOnlyList<string>)>.Fail((string?)body["error"]?["code"] ?? "economy.unavailable");
                }

                var list = new List<AchievementInfo>();
                if (body["achievements"] is JArray items)
                {
                    foreach (var a in items)
                    {
                        list.Add(new AchievementInfo(
                            (string?)a["id"] ?? "",
                            (string?)a["category"] ?? "play",
                            (string?)a["metric"] ?? "",
                            (long?)a["target"] ?? 1,
                            (long?)a["progress"] ?? 0,
                            (long?)a["unlockedTs"],
                            (long?)a["reward"]?["gems"] ?? 0,
                            (string?)a["reward"]?["title"]));
                    }
                }

                var now = new List<string>();
                if (body["unlockedNow"] is JArray ids)
                {
                    foreach (var id in ids)
                    {
                        now.Add((string)id!);
                    }
                }

                return Result<(IReadOnlyList<AchievementInfo>, IReadOnlyList<string>)>.Ok((list, now));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"achievements_get: {error.Message}");
                return Result<(IReadOnlyList<AchievementInfo>, IReadOnlyList<string>)>.Fail("economy.unavailable", error.GetType().Name);
            }
        }
    }
}
