using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Economy;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaShopService : IShopService
    {
        private readonly NakamaConnection _connection;

        public NakamaShopService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<ShopCatalog>> CatalogAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("economy_catalog", new JObject(), cancellation);
            if (!body.IsOk)
            {
                return Result<ShopCatalog>.Fail(body.Error);
            }

            var items = new List<ShopItem>();
            if (body.Value["items"] is JArray list)
            {
                foreach (var i in list)
                {
                    items.Add(new ShopItem(
                        (string?)i["id"] ?? "",
                        (string?)i["category"] ?? "",
                        (string?)i["rarity"] ?? "common",
                        (long?)i["price"]?["coins"] ?? 0,
                        (long?)i["price"]?["gems"] ?? 0,
                        (bool?)i["owned"] == true,
                        (bool?)i["equipped"] == true,
                        (string?)i["season"]));
                }
            }

            var spotlight = new List<string>();
            if (body.Value["spotlight"] is JArray ids)
            {
                foreach (var id in ids)
                {
                    spotlight.Add((string)id!);
                }
            }

            return Result<ShopCatalog>.Ok(new ShopCatalog(items, spotlight));
        }

        public async Awaitable<Result<(long Coins, long Gems)>> PurchaseAsync(string itemId, CancellationToken cancellation)
        {
            var body = await RpcAsync("economy_purchase", new JObject { ["itemId"] = itemId }, cancellation);
            if (!body.IsOk)
            {
                return Result<(long Coins, long Gems)>.Fail(body.Error);
            }

            var balances = body.Value["balances"];
            return Result<(long Coins, long Gems)>.Ok(((long?)balances?["coins"] ?? 0, (long?)balances?["gems"] ?? 0));
        }

        public async Awaitable<Result<bool>> EquipAsync(string itemId, CancellationToken cancellation)
        {
            var body = await RpcAsync("inventory_equip", new JObject { ["itemId"] = itemId }, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error);
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
                    : Result<JObject>.Fail((string?)body["error"]?["code"] ?? "economy.unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{id}: {error.Message}");
                return Result<JObject>.Fail("economy.unavailable", error.GetType().Name);
            }
        }
    }
}
