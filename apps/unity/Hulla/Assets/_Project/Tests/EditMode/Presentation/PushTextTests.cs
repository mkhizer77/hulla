using System.Text.RegularExpressions;
using Hulla.Presentation.Text;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class PushTextTests
    {
        /// <summary>The types in server/nakama/src/services/push.ts (PUSH_SPECS).</summary>
        private static readonly string[] Types = { "table_invite", "friend_request", "friend_online", "daily_reward", "season_end" };

        [Test]
        public void EveryServerPushTypeHasATitleAndABody()
        {
            foreach (var type in Types)
            {
                Assert.IsTrue(PushText.Keyed.ContainsKey("push_" + type + "_title"), type);
                Assert.IsTrue(PushText.Keyed.ContainsKey("push_" + type + "_body"), type);
            }

            Assert.AreEqual(Types.Length * 2, PushText.Keyed.Count);
        }

        [Test]
        public void KeysAreValidAndroidResourceNames()
        {
            foreach (var key in PushText.Keyed.Keys)
            {
                StringAssert.IsMatch("^[a-z][a-z0-9_]*$", key);
            }
        }

        [TestCase("{0} invites you to their table ({1}).", "%1$s invites you to their table (%2$s).")]
        [TestCase("Ali's table & \"chai\"", "Ali\\'s table &amp; \\\"chai\\\"")]
        [TestCase("<b>", "&lt;b&gt;")]
        public void AndroidStringsAreEscapedAndPositional(string text, string expected)
        {
            Assert.AreEqual(expected, PushText.ToAndroid(text));
        }

        [Test]
        public void NoAndroidStringKeepsABracePlaceholder()
        {
            foreach (var text in PushText.Keyed.Values)
            {
                Assert.IsFalse(Regex.IsMatch(PushText.ToAndroid(text), @"\{\d\}"), text);
            }
        }
    }
}
