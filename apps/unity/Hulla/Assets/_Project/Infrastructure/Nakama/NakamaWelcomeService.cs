using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>tutorial_complete (GDD 17.2).</summary>
    public sealed class NakamaWelcomeService : IWelcomeService
    {
        private readonly NakamaConnection _connection;

        public NakamaWelcomeService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<long>> TutorialCompleteAsync(CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<long>.Fail("session.not_signed_in");
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, "tutorial_complete", "{}", null, cancellation);
                var body = JObject.Parse(response.Payload);
                return (bool?)body["ok"] == true
                    ? Result<long>.Ok((long?)body["coins"] ?? 0)
                    : Result<long>.Fail((string?)body["error"]?["code"] ?? "session.unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"tutorial_complete: {error.Message}");
                return Result<long>.Fail("session.unavailable", error.GetType().Name);
            }
        }
    }
}
