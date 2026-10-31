using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Social;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class PushRegistrationTests
    {
        private sealed class FakeRegistry : IPushRegistry
        {
            public readonly List<PushDeviceInput> Sent = new List<PushDeviceInput>();
            public bool Refuse;

            public Awaitable<Result<bool>> RegisterAsync(PushDeviceInput device, CancellationToken cancellation)
            {
                Sent.Add(device);
                var source = new AwaitableCompletionSource<Result<bool>>();
                source.SetResult(Refuse ? Result<bool>.Fail("network.unavailable") : Result<bool>.Ok(true));
                return source.Awaitable;
            }
        }

        private static PushRegistration Make(FakeRegistry registry) =>
            new PushRegistration(registry, "install-1", "android", () => "en");

        [Test]
        public void NothingIsSentBeforeBothTheTokenAndThePlayer()
        {
            var registry = new FakeRegistry();
            var push = Make(registry);
            push.TokenReceived("tok-a", CancellationToken.None);
            Assert.IsEmpty(registry.Sent, "token only");
            push.SignedIn("user-1", CancellationToken.None);
            Assert.AreEqual(1, registry.Sent.Count);
            Assert.AreEqual("install-1", registry.Sent[0].InstallId);
            Assert.AreEqual("tok-a", registry.Sent[0].Token);
            Assert.AreEqual("android", registry.Sent[0].Platform);
            Assert.AreEqual("en", registry.Sent[0].Locale);
        }

        [Test]
        public void TheSamePairIsSentOnce()
        {
            var registry = new FakeRegistry();
            var push = Make(registry);
            push.SignedIn("user-1", CancellationToken.None);
            push.TokenReceived("tok-a", CancellationToken.None);
            push.SignedIn("user-1", CancellationToken.None);
            push.TokenReceived("tok-a", CancellationToken.None);
            Assert.AreEqual(1, registry.Sent.Count);
        }

        [Test]
        public void ANewTokenOrPlayerIsSentAgain()
        {
            var registry = new FakeRegistry();
            var push = Make(registry);
            push.SignedIn("user-1", CancellationToken.None);
            push.TokenReceived("tok-a", CancellationToken.None);
            push.TokenReceived("tok-b", CancellationToken.None);
            push.SignedIn("user-2", CancellationToken.None);
            Assert.AreEqual(3, registry.Sent.Count);
            Assert.AreEqual("tok-b", registry.Sent[2].Token);
        }

        [Test]
        public void AFailedSendIsTriedAtTheNextSignIn()
        {
            var registry = new FakeRegistry { Refuse = true };
            var push = Make(registry);
            push.TokenReceived("tok-a", CancellationToken.None);
            push.SignedIn("user-1", CancellationToken.None);
            registry.Refuse = false;
            push.SignedIn("user-1", CancellationToken.None);
            push.SignedIn("user-1", CancellationToken.None);
            Assert.AreEqual(2, registry.Sent.Count);
        }
    }
}
