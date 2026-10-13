using System.Collections.Generic;
using Hulla.Application.Social;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class DeepLinkRouterTests
    {
        [TestCase("hulla://j/K7Q2MX", "K7Q2MX")]
        [TestCase("hulla://j/k7q2mx/", "K7Q2MX")]
        [TestCase("https://hulla.gg/j/K7Q2MX", "K7Q2MX")]
        [TestCase("https://www.hulla.gg/j/K7Q2MX?utm_source=whatsapp", "K7Q2MX")]
        public void InviteLinksGiveTheRoomCode(string url, string code)
        {
            Assert.AreEqual(code, InviteLink.CodeFrom(url));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not a link")]
        [TestCase("hulla://profile/K7Q2MX")]
        [TestCase("http://hulla.gg/j/K7Q2MX")]
        [TestCase("https://evil.example/j/K7Q2MX")]
        [TestCase("https://hulla.gg/x/K7Q2MX")]
        [TestCase("hulla://j/K7Q2M")]
        [TestCase("hulla://j/K7Q2MXX")]
        [TestCase("hulla://j/K7Q2M0")]
        public void OtherLinksAreIgnored(string? url)
        {
            Assert.IsNull(InviteLink.CodeFrom(url));
        }

        [Test]
        public void TheSharedLinkParsesBack()
        {
            Assert.AreEqual("K7Q2MX", InviteLink.CodeFrom(InviteLink.For("K7Q2MX")));
        }

        [Test]
        public void AColdStartLinkWaitsUntilTheFlowIsReady()
        {
            var router = new DeepLinkRouter();
            var opened = new List<(string, float)>();
            router.InviteOpened += (code, at) => opened.Add((code, at));

            router.Receive("hulla://j/K7Q2MX", 0.5f);
            Assert.IsTrue(router.HasPending);
            Assert.IsEmpty(opened);

            Assert.IsTrue(router.Ready());
            Assert.AreEqual(new[] { ("K7Q2MX", 0.5f) }, opened);
            Assert.IsFalse(router.HasPending);
        }

        [Test]
        public void AWarmLinkGoesStraightThrough()
        {
            var router = new DeepLinkRouter();
            var opened = new List<string>();
            router.InviteOpened += (code, _) => opened.Add(code);
            Assert.IsFalse(router.Ready());

            router.Receive("https://hulla.gg/j/ABCDEF", 10f);
            Assert.AreEqual(new[] { "ABCDEF" }, opened);
        }

        [Test]
        public void TheSameLinkTwiceInARowOpensOnce()
        {
            var router = new DeepLinkRouter();
            var opened = new List<string>();
            router.InviteOpened += (code, _) => opened.Add(code);
            router.Ready();

            router.Receive("hulla://j/ABCDEF", 1f);
            router.Receive("hulla://j/ABCDEF", 2f);
            Assert.AreEqual(1, opened.Count);

            // Tapping the same invite again later opens it again.
            router.Receive("hulla://j/ABCDEF", 30f);
            Assert.AreEqual(2, opened.Count);
        }

        [Test]
        public void ALinkThatIsNotAnInviteIsDropped()
        {
            var router = new DeepLinkRouter();
            router.Receive("https://example.com", 0f);
            Assert.IsFalse(router.HasPending);
        }
    }
}
