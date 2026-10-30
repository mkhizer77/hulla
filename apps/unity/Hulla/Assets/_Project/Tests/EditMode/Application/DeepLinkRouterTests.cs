using System.Collections.Generic;
using Hulla.Application.Social;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class DeepLinkRouterTests
    {
        [TestCase("hulla://j/K7Q2MX", "K7Q2MX")]
        [TestCase("hulla://j/k7q2mx/", "K7Q2MX")]
        [TestCase("https://playhulla.pages.dev/j/K7Q2MX", "K7Q2MX")]
        [TestCase("https://PlayHulla.pages.dev/j/K7Q2MX?utm_source=whatsapp", "K7Q2MX")]
        public void InviteLinksGiveTheRoomCode(string url, string code)
        {
            Assert.AreEqual(code, InviteLink.CodeFrom(url));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not a link")]
        [TestCase("hulla://profile/K7Q2MX")]
        [TestCase("http://playhulla.pages.dev/j/K7Q2MX")]
        [TestCase("https://evil.example/j/K7Q2MX")]
        [TestCase("https://evil.pages.dev/j/K7Q2MX")]
        [TestCase("https://playhulla.pages.dev/x/K7Q2MX")]
        [TestCase("https://hulla.gg/j/K7Q2MX")]
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
            Assert.AreEqual("https://playhulla.pages.dev/j/K7Q2MX", InviteLink.For("K7Q2MX"));
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

            router.Receive("https://playhulla.pages.dev/j/ABCDEF", 10f);
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
            Assert.AreEqual("none", router.PendingType);
        }

        [TestCase("hulla://r/K7Q2MXAB", "K7Q2MXAB")]
        [TestCase("hulla://r/k7q2mxab/", "K7Q2MXAB")]
        [TestCase("https://playhulla.pages.dev/r/K7Q2MXAB?utm_source=story", "K7Q2MXAB")]
        public void ReferralLinksGiveTheFriendCode(string url, string friendCode)
        {
            Assert.AreEqual(friendCode, InviteLink.FriendCodeFrom(url));
            Assert.IsNull(InviteLink.CodeFrom(url));
        }

        [TestCase("hulla://r/K7Q2MX")]
        [TestCase("hulla://r/K7Q2MXAB1")]
        [TestCase("hulla://j/K7Q2MXAB")]
        [TestCase("https://evil.example/r/K7Q2MXAB")]
        public void OtherLinksGiveNoFriendCode(string url)
        {
            Assert.IsNull(InviteLink.FriendCodeFrom(url));
        }

        [Test]
        public void TheSharedReferralLinkParsesBack()
        {
            Assert.AreEqual("ABCDEFGH", InviteLink.FriendCodeFrom(InviteLink.ReferralFor("ABCDEFGH")));
        }

        [Test]
        public void AColdStartReferralWaitsAndIsNotAnInvite()
        {
            var router = new DeepLinkRouter();
            var invites = new List<string>();
            var referrals = new List<string>();
            router.InviteOpened += (code, _) => invites.Add(code);
            router.ReferralOpened += referrals.Add;

            router.Receive("hulla://r/ABCDEFGH", 0f);
            Assert.AreEqual("referral", router.PendingType);
            router.Ready();

            Assert.AreEqual(new[] { "ABCDEFGH" }, referrals);
            Assert.IsEmpty(invites);
            Assert.AreEqual("none", router.PendingType);
        }

        [Test]
        public void AColdStartInviteIsTypedAsAnInvite()
        {
            var router = new DeepLinkRouter();
            router.Receive("hulla://j/ABCDEF", 0f);
            Assert.AreEqual("invite", router.PendingType);
        }
    }
}
