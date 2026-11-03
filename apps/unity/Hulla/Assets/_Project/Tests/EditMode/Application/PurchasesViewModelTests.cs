using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Economy;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class PurchasesViewModelTests
    {
        private static Awaitable<T> Done<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }

        private sealed class FakeStore : IAppStore
        {
            public readonly List<string> Bought = new List<string>();
            public readonly List<string> Finished = new List<string>();
            public bool Restored;
            public bool Connected;

            public bool Supported => true;

            public event Action<StoreReceipt>? Pending;

            public event Action<string, string>? Failed;

            public Awaitable<bool> ConnectAsync(IReadOnlyList<(string Id, bool Consumable)> products, CancellationToken cancellation)
            {
                Connected = true;
                return Done(true);
            }

            public StoreProduct? Product(string id) => Connected ? new StoreProduct(id, "$0.99") : null;

            public void Buy(string productId) => Bought.Add(productId);

            public void Finish(StoreReceipt receipt) => Finished.Add(receipt.TransactionId);

            public void Restore() => Restored = true;

            public void Deliver(string productId, string transaction) =>
                Pending?.Invoke(new StoreReceipt(productId, transaction, "google", "{\"json\":\"" + transaction + "\"}", transaction));

            public void Fail(string productId, string code) => Failed?.Invoke(productId, code);
        }

        private sealed class FakeServer : IPurchaseService
        {
            public readonly List<string> Verified = new List<string>();
            public string? Refuse;
            public bool RemoveAds;
            public long Gems = 100;

            public Awaitable<Result<(IReadOnlyList<ProductInfo> Products, Entitlements Entitlements)>> ProductsAsync(CancellationToken cancellation) =>
                Done(Result<(IReadOnlyList<ProductInfo>, Entitlements)>.Ok((Products, Current())));

            public Awaitable<Result<PurchaseOutcome>> VerifyAsync(string store, string receipt, CancellationToken cancellation)
            {
                Verified.Add(receipt);
                if (Refuse != null)
                {
                    return Done(Result<PurchaseOutcome>.Fail(Refuse));
                }

                var productId = receipt.Contains("ads") ? "remove_ads" : "gems_650";
                if (productId == "remove_ads")
                {
                    RemoveAds = true;
                }
                else
                {
                    Gems += 650;
                }

                var grants = new List<PurchaseGrant> { new PurchaseGrant(productId, receipt, true, null) };
                return Done(Result<PurchaseOutcome>.Ok(new PurchaseOutcome(grants, 0, Gems, Current())));
            }

            public Awaitable<Result<PurchaseOutcome>> RestoreAsync(CancellationToken cancellation) =>
                Done(Result<PurchaseOutcome>.Ok(new PurchaseOutcome(new List<PurchaseGrant>(), 0, Gems, Current())));

            private Entitlements Current() => new Entitlements(
                RemoveAds, null, RemoveAds ? new Dictionary<string, int> { ["remove_ads"] = 1 } : new Dictionary<string, int>());

            private static readonly IReadOnlyList<ProductInfo> Products = new List<ProductInfo>
            {
                new ProductInfo("remove_ads", false, 0, null, true, new List<string>()),
                new ProductInfo("gems_650", true, 650, null, false, new List<string>()),
                new ProductInfo("pass_premium", true, 0, "premium", false, new List<string>()),
            };
        }

        private static (PurchasesViewModel Model, FakeStore Store, FakeServer Server) Make()
        {
            var store = new FakeStore();
            var server = new FakeServer();
            return (new PurchasesViewModel(store, server), store, server);
        }

        [Test]
        public void APurchaseIsFinishedWithTheStoreOnlyAfterTheServerGrantsIt()
        {
            var (model, store, server) = Make();
            var granted = new List<string>();
            model.Granted += g => granted.Add(g.ProductId);
            _ = model.StartAsync(CancellationToken.None);
            Assert.IsTrue(model.Ready);
            Assert.AreEqual("$0.99", model.Price("gems_650"));

            model.Buy("gems_650");
            Assert.AreEqual("gems_650", model.Busy);
            CollectionAssert.AreEqual(new[] { "gems_650" }, store.Bought);
            store.Deliver("gems_650", "t1");

            Assert.AreEqual(1, server.Verified.Count);
            CollectionAssert.AreEqual(new[] { "t1" }, store.Finished);
            CollectionAssert.AreEqual(new[] { "gems_650" }, granted);
            Assert.AreEqual(750, model.Balances?.Gems);
            Assert.IsNull(model.Busy);
        }

        [Test]
        public void APurchaseTheServerCannotReachStaysWithTheStoreAndIsSentOnTheNextStart()
        {
            var (model, store, server) = Make();
            _ = model.StartAsync(CancellationToken.None);
            server.Refuse = "economy.store_unavailable";
            store.Deliver("gems_650", "t1");
            Assert.IsEmpty(store.Finished);
            Assert.AreEqual("economy.store_unavailable", model.ErrorCode);

            server.Refuse = null;
            _ = model.StartAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "t1" }, store.Finished);
        }

        [Test]
        public void ARefusedReceiptIsNeverFinishedSoTheStoreCanRefundIt()
        {
            var (model, store, server) = Make();
            _ = model.StartAsync(CancellationToken.None);
            server.Refuse = "economy.receipt_invalid";
            store.Deliver("gems_650", "t1");
            Assert.IsEmpty(store.Finished);
            Assert.AreEqual("economy.receipt_invalid", model.ErrorCode);

            server.Refuse = null;
            _ = model.StartAsync(CancellationToken.None);
            Assert.IsEmpty(store.Finished, "the refused receipt is not retried in this run");
        }

        [Test]
        public void PurchasesBeforeSignInWaitForIt()
        {
            var (model, store, server) = Make();
            store.Deliver("gems_650", "t1");
            Assert.IsEmpty(server.Verified);
            _ = model.StartAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "t1" }, store.Finished);
        }

        [Test]
        public void ACancelledPurchaseFreesTheButtonWithoutAnError()
        {
            var (model, store, _) = Make();
            _ = model.StartAsync(CancellationToken.None);
            model.Buy("gems_650");
            store.Fail("gems_650", PurchasesViewModel.Cancelled);
            Assert.IsNull(model.Busy);
            Assert.IsNull(model.ErrorCode);
            store.Fail("gems_650", "economy.payment_declined");
            Assert.AreEqual("economy.payment_declined", model.ErrorCode);
        }

        [Test]
        public void RemoveAdsIsBoughtOnceAndThenShowsAsOwned()
        {
            var (model, store, _) = Make();
            _ = model.StartAsync(CancellationToken.None);
            Assert.IsTrue(model.CanBuy("remove_ads"));
            model.Buy("remove_ads");
            store.Deliver("remove_ads", "ads1");
            Assert.IsTrue(model.Entitlements.RemoveAds);
            Assert.IsFalse(model.CanBuy("remove_ads"));
            Assert.IsTrue(model.CanBuy("gems_650"), "consumables stay on sale");
        }

        [Test]
        public void RestoreAsksTheStoreAndTheServer()
        {
            var (model, store, _) = Make();
            _ = model.StartAsync(CancellationToken.None);
            _ = model.RestoreAsync(CancellationToken.None);
            Assert.IsTrue(store.Restored);
            Assert.IsNull(model.Busy);
            Assert.IsNull(model.ErrorCode);
        }

        [Test]
        public void NothingIsOnSaleWhereThePhoneHasNoStore()
        {
            var model = new PurchasesViewModel(new NoAppStore(), new FakeServer());
            _ = model.StartAsync(CancellationToken.None);
            Assert.IsFalse(model.Supported);
            Assert.IsFalse(model.Ready);
            Assert.IsFalse(model.CanBuy("gems_650"));
            Assert.IsNull(model.Price("gems_650"));
        }
    }
}
