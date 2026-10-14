using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Hulla.Architecture.Tests
{
    /// <summary>The Addressables groups of PROJECT_STRUCTURE 5 (ADR-0011): base art in the binary, cosmetics streamed and cached.</summary>
    public sealed class AddressablesLayoutTests
    {
        private static readonly string[] Local = { "ui-core", "game-thulla", "avatars-base" };
        private static readonly string[] Remote = { "avatars", "tables", "cards-classic", "cards-desi", "emotes", "audio-sfx", "audio-music" };

        private static AddressableAssetSettings Settings
        {
            get
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                Assert.IsNotNull(settings, "run Hulla > Set up Addressables");
                return settings;
            }
        }

        [Test]
        public void BaseGroupsShipInTheBinaryAndCosmeticsStreamFromTheCdnIntoTheCache()
        {
            var settings = Settings;
            foreach (var name in Local.Concat(Remote))
            {
                var group = settings.FindGroup(name);
                Assert.IsNotNull(group, name);
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                var remote = Remote.Contains(name);
                Assert.AreEqual(remote ? AddressableAssetSettings.kRemoteLoadPath : AddressableAssetSettings.kLocalLoadPath, schema.LoadPath.GetName(settings), name);
                Assert.AreEqual(remote, schema.UseAssetBundleCache, name);
                Assert.AreEqual(!remote, group.GetSchema<ContentUpdateGroupSchema>().StaticContent, name);
            }

            var remotePath = settings.profileSettings.GetValueByName(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath);
            StringAssert.StartsWith("https://cdn.invalid/hulla/", remotePath, "the client swaps the placeholder host for the CDN from remote config");
            Assert.AreEqual(AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer, settings.BuildAddressablesWithPlayerBuild);
        }

        [Test]
        public void EveryAvatarHasItsAddressInTheBaseGroup()
        {
            var group = Settings.FindGroup("avatars-base");
            var files = Directory.GetFiles("Assets/_Project/Presentation/Art/Avatars", "*.png");
            Assert.Greater(files.Length, 0);
            foreach (var file in files)
            {
                var entry = Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(file.Replace('\\', '/')));
                Assert.IsNotNull(entry, file);
                Assert.AreSame(group, entry.parentGroup, file);
                Assert.AreEqual("avatars-base/" + Path.GetFileNameWithoutExtension(file), entry.address);
            }
        }
    }
}
