using Hulla.Infrastructure.Content;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    /// <summary>Remote packs load from the CDN root that remote config names (ADR-0011).</summary>
    public sealed class AssetPacksTests
    {
        [Test]
        public void ThePlaceholderHostBecomesTheCdnRootAndNothingElseChanges()
        {
            const string bundle = "https://cdn.invalid/hulla/Android/tables_assets_all.bundle";
            Assert.AreEqual("https://cdn.hulla.gg/packs/Android/tables_assets_all.bundle", AddressablePacks.Rewrite(bundle, "https://cdn.hulla.gg/packs"));
            Assert.AreEqual(bundle, AddressablePacks.Rewrite(bundle, ""), "no root keeps remote packs off");
            const string local = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/Android/avatars-base_assets_all.bundle";
            Assert.AreEqual(local, AddressablePacks.Rewrite(local, "https://cdn.hulla.gg/packs"));
        }
    }
}
