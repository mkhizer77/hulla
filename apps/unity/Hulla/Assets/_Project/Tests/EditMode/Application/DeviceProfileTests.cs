using Hulla.Infrastructure.Settings;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class DeviceProfileTests
    {
        [Test]
        public void EachLocalCopyKeepsItsOwnPreferences()
        {
            Assert.AreEqual("profile.hulla-mac-ayesha.", DeviceProfile.PrefixFrom(new[] { "Hulla", "-hulla-device", "hulla-mac-ayesha" }));
            Assert.AreEqual("", DeviceProfile.PrefixFrom(new[] { "Hulla" }), "a phone has one player and no prefix");
            Assert.AreEqual("", DeviceProfile.PrefixFrom(new[] { "Hulla", "-hulla-device", " " }));
        }
    }
}
