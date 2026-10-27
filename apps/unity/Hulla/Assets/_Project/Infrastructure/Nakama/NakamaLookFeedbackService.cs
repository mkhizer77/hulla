using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>look_feedback (the look playtest).</summary>
    public sealed class NakamaLookFeedbackService : ILookFeedbackService
    {
        private readonly NakamaConnection _connection;

        public NakamaLookFeedbackService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<bool>> SendAsync(LookAnswer answer, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<bool>.Fail("session.not_signed_in");
            }

            var payload = new JObject
            {
                ["look"] = answer.Look,
                ["rating"] = answer.Rating,
                ["keep"] = answer.Keep,
                ["rounds"] = answer.Rounds,
            };
            if (answer.Comment.Trim().Length > 0)
            {
                payload["comment"] = answer.Comment.Trim();
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, "look_feedback", payload.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                return (bool?)body["ok"] == true
                    ? Result<bool>.Ok(true)
                    : Result<bool>.Fail((string?)body["error"]?["code"] ?? "session.unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"look_feedback: {error.Message}");
                return Result<bool>.Fail("session.unavailable", error.GetType().Name);
            }
        }
    }
}
