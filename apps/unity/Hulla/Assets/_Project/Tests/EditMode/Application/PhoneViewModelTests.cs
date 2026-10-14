using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class PhoneViewModelTests
    {
        private sealed class FakePhone : IPhoneService
        {
            public bool Linked;
            public Result<PhoneVerified> Verify = Result<PhoneVerified>.Ok(new PhoneVerified(true, "+92 ••• 67", null));
            public readonly List<string> Calls = new List<string>();

            private static Awaitable<T> Done<T>(T value)
            {
                var done = new AwaitableCompletionSource<T>();
                done.SetResult(value);
                return done.Awaitable;
            }

            public Awaitable<Result<bool>> StatusAsync(CancellationToken c) => Done(Result<bool>.Ok(Linked));

            public Awaitable<Result<PhoneCodeSent>> StartAsync(string phone, CancellationToken c)
            {
                Calls.Add("start " + phone);
                return Done(phone.StartsWith("+")
                    ? Result<PhoneCodeSent>.Ok(new PhoneCodeSent("+92 ••• 67", 1000, 60))
                    : Result<PhoneCodeSent>.Fail("auth.otp_invalid_phone"));
            }

            public Awaitable<Result<PhoneVerified>> VerifyAsync(string phone, string code, CancellationToken c)
            {
                Calls.Add("verify " + phone + " " + code);
                return Done(Verify);
            }

            public Awaitable<Result<bool>> RemoveAsync(CancellationToken c)
            {
                Calls.Add("remove");
                return Done(Result<bool>.Ok(true));
            }

            public Awaitable<Result<PlayerSession>> SwitchAsync(string token, CancellationToken c)
            {
                Calls.Add("switch " + token);
                return Done(Result<PlayerSession>.Ok(new PlayerSession("owner", "Ayesha")));
            }
        }

        [Test]
        public void ANewNumberIsSentThenConfirmedAndLinked()
        {
            var service = new FakePhone();
            var model = new PhoneViewModel(service);
            _ = model.RefreshAsync(CancellationToken.None);
            Assert.AreEqual(PhoneViewModel.Stage.NoNumber, model.Current);
            _ = model.SendAsync(" +923001234567 ", CancellationToken.None);
            Assert.AreEqual(PhoneViewModel.Stage.CodeSent, model.Current);
            Assert.AreEqual("+92 ••• 67", model.MaskedPhone);
            _ = model.VerifyAsync(" 234567", CancellationToken.None);
            Assert.AreEqual(PhoneViewModel.Stage.Linked, model.Current);
            CollectionAssert.AreEqual(new[] { "start +923001234567", "verify +923001234567 234567" }, service.Calls);
        }

        [Test]
        public void ABadNumberStaysOnTheNumberWithTheReason()
        {
            var model = new PhoneViewModel(new FakePhone());
            _ = model.RefreshAsync(CancellationToken.None);
            _ = model.SendAsync("0300", CancellationToken.None);
            Assert.AreEqual(PhoneViewModel.Stage.NoNumber, model.Current);
            Assert.AreEqual("auth.otp_invalid_phone", model.ErrorCode);
            Assert.IsFalse(model.Busy);
        }

        [Test]
        public void ANumberOfAnotherAccountOffersTheSwitchOnce()
        {
            var service = new FakePhone { Verify = Result<PhoneVerified>.Ok(new PhoneVerified(false, "+92 ••• 67", "otp:t1")) };
            var model = new PhoneViewModel(service);
            PlayerSession? switched = null;
            model.Switched += p => switched = p;
            _ = model.SendAsync("+923001234567", CancellationToken.None);
            _ = model.VerifyAsync("234567", CancellationToken.None);
            Assert.AreEqual(PhoneViewModel.Stage.Offer, model.Current);
            _ = model.SwitchAsync(CancellationToken.None);
            Assert.AreEqual(PhoneViewModel.Stage.Switched, model.Current);
            Assert.AreEqual("Ayesha", switched!.DisplayName);
            _ = model.SwitchAsync(CancellationToken.None);
            Assert.AreEqual(1, service.Calls.FindAll(c => c.StartsWith("switch")).Count, "the token is used once");
        }

        [Test]
        public void ALinkedNumberCanBeRemovedAndADeclinedSwitchStartsOver()
        {
            var service = new FakePhone { Linked = true };
            var model = new PhoneViewModel(service);
            _ = model.RefreshAsync(CancellationToken.None);
            Assert.AreEqual(PhoneViewModel.Stage.Linked, model.Current);
            _ = model.RemoveAsync(CancellationToken.None);
            Assert.AreEqual(PhoneViewModel.Stage.NoNumber, model.Current);
            Assert.IsTrue(model.Removed);
            model.StartOver();
            Assert.AreEqual(PhoneViewModel.Stage.NoNumber, model.Current);
            Assert.IsNull(model.ErrorCode);
        }
    }
}
