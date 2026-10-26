using System;
using System.Threading;
using Hulla.Application.Voice;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>The `voice_token` RPC (GDD 11.4): refusals come back as `voice.*` codes.</summary>
    public sealed class NakamaVoiceTokens : IVoiceTokens
    {
        private readonly NakamaConnection _connection;

        public NakamaVoiceTokens(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<VoiceTicket>> TokenAsync(string matchId, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<VoiceTicket>.Fail("session.not_signed_in");
            }

            try
            {
                var input = new JObject { ["matchId"] = matchId }.ToString(Newtonsoft.Json.Formatting.None);
                var response = await _connection.Client.RpcAsync(session, "voice_token", input, null, cancellation);
                return Parse(response.Payload);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"voice_token: {error.Message}");
                return Result<VoiceTicket>.Fail("voice.unavailable", error.GetType().Name);
            }
        }

        public static Result<VoiceTicket> Parse(string payload)
        {
            var body = JObject.Parse(payload);
            if ((bool?)body["ok"] != true)
            {
                return Result<VoiceTicket>.Fail((string?)body["error"]?["code"] ?? "voice.unavailable");
            }

            var appId = (string?)body["appId"];
            var channel = (string?)body["channel"];
            var account = (string?)body["account"];
            var token = (string?)body["token"];
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(account) || string.IsNullOrEmpty(token))
            {
                return Result<VoiceTicket>.Fail("voice.unavailable", "payload");
            }

            return Result<VoiceTicket>.Ok(new VoiceTicket(appId!, channel!, account!, token!, (long?)body["expiresTs"] ?? 0));
        }
    }
}
