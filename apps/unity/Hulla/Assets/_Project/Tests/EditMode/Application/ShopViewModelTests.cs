using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Economy;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class ShopViewModelTests
    {
        private sealed class FakeShop : IShopService
        {
            public readonly HashSet<string> Owned = new HashSet<string> { "back_classic_blue" };
            public string Equipped = "back_classic_blue";
            public long Coins = 800;

            private static Awaitable<T> Done<T>(T value)
            {
                var source = new AwaitableCompletionSource<T>();
                source.SetResult(value);
                return source.Awaitable;
            }

            private static readonly (string Id, string Category, long Coins, long Gems)[] Items =
            {
                ("back_classic_blue", "cardBack", 0, 0),
                ("back_classic_red", "cardBack", 500, 0),
                ("back_mughal", "cardBack", 0, 400),
                ("table_marble", "table", 4000, 0),
            };

            public Awaitable<Result<ShopCatalog>> CatalogAsync(CancellationToken c)
            {
                var list = new List<ShopItem>();
                foreach (var (id, category, coins, gems) in Items)
                {
                    list.Add(new ShopItem(id, category, "common", coins, gems, Owned.Contains(id), Equipped == id, null));
                }

                return Done(Result<ShopCatalog>.Ok(new ShopCatalog(list, new List<string> { "table_marble", "back_classic_blue" })));
            }

            public Awaitable<Result<(long Coins, long Gems)>> PurchaseAsync(string itemId, CancellationToken c)
            {
                if (itemId != "back_classic_red")
                {
                    return Done(Result<(long, long)>.Fail("economy.insufficient_funds"));
                }

                Owned.Add(itemId);
                Coins -= 500;
                return Done(Result<(long, long)>.Ok((Coins, 0L)));
            }

            public Awaitable<Result<bool>> EquipAsync(string itemId, CancellationToken c)
            {
                Equipped = itemId;
                return Done(Result<bool>.Ok(true));
            }
        }

        [Test]
        public void ACategoryShowsOnlyItsItemsAndTheSpotlightSkipsOwned()
        {
            var model = new ShopViewModel(new FakeShop());
            _ = model.RefreshAsync(CancellationToken.None);
            Assert.AreEqual(3, model.Shown.Count, "card backs first");
            model.Show("table");
            Assert.AreEqual("table_marble", model.Shown[0].Id);
            Assert.AreEqual(1, model.Spotlight.Count, "the owned spotlight item is left out");
        }

        [Test]
        public void BuyingUpdatesTheBalanceAndEquippingMovesTheTick()
        {
            var shop = new FakeShop();
            var model = new ShopViewModel(shop);
            model.SetBalances(800, 0);
            _ = model.RefreshAsync(CancellationToken.None);
            var red = model.Shown[1];
            Assert.IsTrue(model.CanAfford(red));
            Assert.IsFalse(model.CanAfford(model.Shown[2]), "no gems for the epic back");

            _ = model.BuyAsync("back_classic_red", CancellationToken.None);
            Assert.AreEqual(300, model.Coins);
            Assert.IsTrue(model.Shown[1].Owned);

            _ = model.EquipAsync("back_classic_red", CancellationToken.None);
            Assert.IsTrue(model.Shown[1].Equipped);
            Assert.IsFalse(model.Shown[0].Equipped);
        }

        [Test]
        public void ARefusedPurchaseShowsTheReason()
        {
            var model = new ShopViewModel(new FakeShop());
            _ = model.RefreshAsync(CancellationToken.None);
            _ = model.BuyAsync("back_mughal", CancellationToken.None);
            Assert.AreEqual("economy.insufficient_funds", model.ErrorCode);
            Assert.IsNull(model.Busy);
        }
    }
}
