using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Economy;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaPurchaseService : IPurchaseService
    {
        private readonly NakamaConnection _connection;

        public NakamaPurchaseService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<(IReadOnlyList<ProductInfo> Products, Entitlements Entitlements)>> ProductsAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("economy_products", new JObject(), cancellation);
            if (!body.IsOk)
            {
                return Result<(IReadOnlyList<ProductInfo>, Entitlements)>.Fail(body.Error);
            }

            var products = new List<ProductInfo>();
            if (body.Value["products"] is JArray list)
            {
                foreach (var p in list)
                {
                    var grants = p["grants"];
                    var removeAds = false;
                    if (grants?["entitlements"] is JArray entitlements)
                    {
                        foreach (var e in entitlements)
                        {
                            removeAds |= (string?)e == "removeAds";
                        }
                    }

                    products.Add(new ProductInfo(
                        (string?)p["id"] ?? "",
                        (string?)p["kind"] == "consumable",
                        (long?)grants?["gems"] ?? 0,
                        (string?)grants?["pass"],
                        removeAds,
                        Strings(grants?["items"])));
                }
            }

            return Result<(IReadOnlyList<ProductInfo>, Entitlements)>.Ok((products, ParseEntitlements(body.Value["entitlements"])));
        }

        public async Awaitable<Result<PurchaseOutcome>> VerifyAsync(string store, string receipt, CancellationToken cancellation) =>
            Outcome(await RpcAsync("iap_verify", new JObject { ["store"] = store, ["receipt"] = receipt }, cancellation));

        public async Awaitable<Result<PurchaseOutcome>> RestoreAsync(CancellationToken cancellation) =>
            Outcome(await RpcAsync("iap_restore", new JObject(), cancellation));

        private static Result<PurchaseOutcome> Outcome(Result<JObject> body)
        {
            if (!body.IsOk)
            {
                return Result<PurchaseOutcome>.Fail(body.Error);
            }

            var grants = new List<PurchaseGrant>();
            if (body.Value["purchases"] is JArray list)
            {
                foreach (var g in list)
                {
                    grants.Add(new PurchaseGrant(
                        (string?)g["productId"] ?? "",
                        (string?)g["transactionId"] ?? "",
                        (bool?)g["applied"] == true,
                        (string?)g["reason"]));
                }
            }

            var balances = body.Value["balances"];
            return Result<PurchaseOutcome>.Ok(new PurchaseOutcome(
                grants,
                (long?)balances?["coins"] ?? 0,
                (long?)balances?["gems"] ?? 0,
                ParseEntitlements(body.Value["entitlements"])));
        }

        private static Entitlements ParseEntitlements(JToken? token)
        {
            if (token == null)
            {
                return Entitlements.None;
            }

            var products = new Dictionary<string, int>();
            if (token["products"] is JObject owned)
            {
                foreach (var pair in owned)
                {
                    products[pair.Key] = (int?)pair.Value ?? 0;
                }
            }

            return new Entitlements((bool?)token["removeAds"] == true, (string?)token["pendingPass"], products);
        }

        private static IReadOnlyList<string> Strings(JToken? token)
        {
            var list = new List<string>();
            if (token is JArray array)
            {
                foreach (var value in array)
                {
                    list.Add((string?)value ?? "");
                }
            }

            return list;
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
                    : Result<JObject>.Fail((string?)body["error"]?["code"] ?? "economy.store_unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                // The receipt never reached the server: it stays with the store and is sent again.
                Debug.LogWarning($"{id}: {error.GetType().Name}");
                return Result<JObject>.Fail("economy.store_unavailable", error.GetType().Name);
            }
        }
    }
}
