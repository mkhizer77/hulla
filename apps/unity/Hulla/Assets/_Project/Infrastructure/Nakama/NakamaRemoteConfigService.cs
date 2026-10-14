using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>config_get (GDD 22.5): the server-side remote config, resolved for this client.</summary>
    public sealed class NakamaRemoteConfigService : IRemoteConfigService
    {
        private readonly NakamaConnection _connection;

        public NakamaRemoteConfigService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<RemoteConfigSnapshot>> FetchAsync(string clientVersion, string cohort, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<RemoteConfigSnapshot>.Fail("session.not_signed_in");
            }

            try
            {
                var input = new JObject { ["clientVersion"] = clientVersion, ["cohort"] = cohort };
                var response = await _connection.Client.RpcAsync(session, "config_get", input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                return Result<RemoteConfigSnapshot>.Ok(Parse(response.Payload));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"config_get: {error.Message}");
                return Result<RemoteConfigSnapshot>.Fail("session.unavailable", error.GetType().Name);
            }
        }

        public static RemoteConfigSnapshot Parse(string payload)
        {
            var body = JObject.Parse(payload);
            var flags = new Dictionary<string, object>();
            if (body["flags"] is JObject list)
            {
                foreach (var pair in list)
                {
                    switch (pair.Value?.Type)
                    {
                        case JTokenType.Boolean:
                            flags[pair.Key] = (bool)pair.Value!;
                            break;
                        case JTokenType.Integer:
                        case JTokenType.Float:
                            flags[pair.Key] = (double)pair.Value!;
                            break;
                        case JTokenType.String:
                            flags[pair.Key] = (string)pair.Value!;
                            break;
                    }
                }
            }

            return new RemoteConfigSnapshot(
                (long?)body["version"] ?? 0,
                (string?)body["region"] ?? "local",
                (string?)body["minClientVersion"] ?? "0.0.0",
                (bool?)body["updateRequired"] == true,
                flags);
        }
    }
}
