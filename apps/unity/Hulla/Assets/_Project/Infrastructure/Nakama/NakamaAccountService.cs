using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>account_export, account_delete and account_delete_cancel.</summary>
    public sealed class NakamaAccountService : IAccountService
    {
        private readonly NakamaConnection _connection;

        public NakamaAccountService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<string>> ExportAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("account_export", cancellation);
            return body.IsOk ? Result<string>.Ok(body.Value.ToString(Newtonsoft.Json.Formatting.Indented)) : Result<string>.Fail(body.Error);
        }

        public async Awaitable<Result<long>> DeleteAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("account_delete", cancellation);
            return body.IsOk ? Result<long>.Ok((long?)body.Value["deleteAtTs"] ?? 0) : Result<long>.Fail(body.Error);
        }

        public async Awaitable<Result<bool>> CancelDeleteAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("account_delete_cancel", cancellation);
            return body.IsOk ? Result<bool>.Ok((bool?)body.Value["cancelled"] == true) : Result<bool>.Fail(body.Error);
        }

        private async Awaitable<Result<JObject>> RpcAsync(string id, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<JObject>.Fail("session.not_signed_in");
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, id, "{}", null, cancellation);
                var body = JObject.Parse(response.Payload);
                return (bool?)body["ok"] == true
                    ? Result<JObject>.Ok(body)
                    : Result<JObject>.Fail((string?)body["error"]?["code"] ?? "account.unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{id}: {error.Message}");
                return Result<JObject>.Fail("account.unavailable", error.GetType().Name);
            }
        }
    }
}
