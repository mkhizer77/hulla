using System;
using Hulla.Infrastructure.Nakama;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class ServerErrorsTests
    {
        [TestCase("Match join rejected: table.full", "table.full")]
        [TestCase("Error from runtime function: auth.banned", "auth.banned")]
        [TestCase("social.not_friends", "social.not_friends")]
        [TestCase("Socket closed unexpectedly", "table.join_failed")]
        public void TheServersCodeIsReadFromTheMessage(string message, string expected) =>
            Assert.AreEqual(expected, ServerErrors.CodeOf(new Exception(message), "table.join_failed"));
    }
}

namespace Hulla.Application.Tests
{
    public sealed class ServerNoticesTests
    {
        [Test]
        public void UnlockedAchievementIdsAreRead()
        {
            CollectionAssert.AreEqual(new[] { "first_thulla", "houdini" }, Hulla.Infrastructure.Nakama.ServerNotices.UnlockedIds("{\"unlocked\":[\"first_thulla\",\"houdini\"]}"));
            Assert.IsEmpty(Hulla.Infrastructure.Nakama.ServerNotices.UnlockedIds("not json"));
        }

        [Test]
        public void ATableInviteIsRead()
        {
            var invite = Hulla.Infrastructure.Nakama.ServerNotices.Invite("{\"matchId\":\"m\",\"code\":\"K7Q2MX\",\"fromName\":\"Ayesha\",\"openSeats\":2}");
            Assert.AreEqual(("Ayesha", "K7Q2MX", 2L), invite);
            Assert.IsNull(Hulla.Infrastructure.Nakama.ServerNotices.Invite("{}"));
        }
    }
}
