using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>age_set (GDD 24.6, #87).</summary>
    public sealed class NakamaAgeService : IAgeService
    {
        private readonly NakamaConnection _connection;

        public NakamaAgeService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<AgeLimits>> SetAsync(int birthYear, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<AgeLimits>.Fail("session.not_signed_in");
            }

            try
            {
                var input = new JObject { ["birthYear"] = birthYear };
                var response = await _connection.Client.RpcAsync(session, "age_set", input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                return (bool?)body["ok"] == true
                    ? Result<AgeLimits>.Ok(new AgeLimits((bool?)body["strangersLocked"] ?? true, (bool?)body["personalisedAds"] ?? false))
                    : Result<AgeLimits>.Fail((string?)body["error"]?["code"] ?? "session.unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"age_set: {error.Message}");
                return Result<AgeLimits>.Fail("session.unavailable", error.GetType().Name);
            }
        }
    }
}
