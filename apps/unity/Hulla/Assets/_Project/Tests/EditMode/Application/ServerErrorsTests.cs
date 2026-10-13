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
