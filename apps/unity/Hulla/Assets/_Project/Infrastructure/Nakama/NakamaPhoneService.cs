using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>phone_start, phone_verify and phone_status (GDD 10.2, #99), and the switch to a number's account.</summary>
    public sealed class NakamaPhoneService : IPhoneService
    {
        private readonly NakamaConnection _connection;
        private readonly NakamaSessionService _sessions;

        public NakamaPhoneService(NakamaConnection connection, NakamaSessionService sessions)
        {
            _connection = connection;
            _sessions = sessions;
        }

        public async Awaitable<Result<bool>> StatusAsync(CancellationToken cancellation)
        {
            var body = await CallAsync("phone_status", new JObject(), cancellation);
            return body.IsOk ? Result<bool>.Ok((bool?)body.Value["linked"] == true) : Result<bool>.Fail(body.Error.Code);
        }

        public async Awaitable<Result<PhoneCodeSent>> StartAsync(string phone, CancellationToken cancellation)
        {
            var body = await CallAsync("phone_start", new JObject { ["phone"] = phone }, cancellation);
            if (!body.IsOk)
            {
                return Result<PhoneCodeSent>.Fail(body.Error.Code);
            }

            return Result<PhoneCodeSent>.Ok(new PhoneCodeSent((string?)body.Value["phone"] ?? "", (long?)body.Value["expiresTs"] ?? 0, (long?)body.Value["resendAfterTs"] ?? 0));
        }

        public async Awaitable<Result<PhoneVerified>> VerifyAsync(string phone, string code, CancellationToken cancellation)
        {
            var body = await CallAsync("phone_verify", new JObject { ["phone"] = phone, ["code"] = code }, cancellation);
            if (!body.IsOk)
            {
                return Result<PhoneVerified>.Fail(body.Error.Code);
            }

            var linked = (string?)body.Value["result"] == "linked";
            return Result<PhoneVerified>.Ok(new PhoneVerified(linked, (string?)body.Value["phone"] ?? "", (string?)body.Value["token"]));
        }

        public async Awaitable<Result<bool>> RemoveAsync(CancellationToken cancellation)
        {
            var body = await CallAsync("phone_status", new JObject { ["remove"] = true }, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error.Code);
        }

        public async Awaitable<Result<PlayerSession>> SwitchAsync(string token, CancellationToken cancellation)
        {
            // While still the guest, a merge code hands this phone's progress to the number's account;
            // the server then moves the device id over too, so the next start signs in there.
            var merge = await CallAsync("account_merge_prepare", new JObject(), cancellation);
            var code = merge.IsOk ? (string?)merge.Value["code"] : null;
            try
            {
                var session = await _connection.Client.AuthenticateCustomAsync(token, null, false, null, null, cancellation);
                if (code != null)
                {
                    try
                    {
                        await _connection.Client.RpcAsync(session, "account_merge", new JObject { ["code"] = code }.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                    }
                    catch (Exception error) when (!(error is OperationCanceledException))
                    {
                        Debug.LogWarning($"account_merge: {error.Message}");
                    }
                }

                return await _sessions.AdoptAsync(session, cancellation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"phone sign-in: {error.Message}");
                return Result<PlayerSession>.Fail(ServerErrors.CodeOf(error, "auth.otp_expired"), error.GetType().Name);
            }
        }

        private async Awaitable<Result<JObject>> CallAsync(string rpc, JObject input, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<JObject>.Fail("session.not_signed_in");
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, rpc, input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                if ((bool?)body["ok"] != true)
                {
                    return Result<JObject>.Fail((string?)body["error"]?["code"] ?? "session.unavailable");
                }

                return Result<JObject>.Ok(body);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{rpc}: {error.Message}");
                return Result<JObject>.Fail("session.unavailable", error.GetType().Name);
            }
        }
    }
}
