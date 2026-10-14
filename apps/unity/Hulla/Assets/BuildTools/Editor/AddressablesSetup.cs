using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Hulla.BuildTools
{
    /// <summary>
    /// Creates the Addressables groups of PROJECT_STRUCTURE 5 (ADR-0011). Local groups ship in the
    /// binary; remote groups stream on demand from the content CDN and stay in the download cache.
    /// Idempotent: run it again after adding a group. Batch: -executeMethod Hulla.BuildTools.AddressablesSetup.Run
    /// </summary>
    public static class AddressablesSetup
    {
        /// <summary>The remote load path's placeholder host; the client swaps in the CDN root from remote config.</summary>
        public const string RemoteRoot = "https://cdn.invalid/hulla";

        public const string AvatarFolder = "Assets/_Project/Presentation/Art/Avatars";

        public static readonly string[] LocalGroups = { "ui-core", "game-thulla", "avatars-base" };

        public static readonly string[] RemoteGroups = { "avatars", "tables", "cards-classic", "cards-desi", "emotes", "audio-sfx", "audio-music" };

        [MenuItem("Hulla/Set up Addressables")]
        public static void Run()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, RemoteRoot + "/[BuildTarget]");
            settings.BuildRemoteCatalog = false;
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            foreach (var name in LocalGroups)
            {
                Group(settings, name, false);
            }

            foreach (var name in RemoteGroups)
            {
                Group(settings, name, true);
            }

            settings.DefaultGroup = settings.FindGroup("ui-core");
            var stock = settings.FindGroup("Default Local Group");
            if (stock != null && stock.entries.Count == 0)
            {
                settings.RemoveGroup(stock);
            }

            var avatars = settings.FindGroup("avatars-base");
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { AvatarFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var entry = settings.CreateOrMoveEntry(guid, avatars, false, false);
                entry.address = "avatars-base/" + System.IO.Path.GetFileNameWithoutExtension(path);
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            Debug.Log("Addressables groups: " + string.Join(", ", settings.groups.Select(g => g.Name)));
        }

        private static void Group(AddressableAssetSettings settings, string name, bool remote)
        {
            var group = settings.FindGroup(name)
                ?? settings.CreateGroup(name, false, false, false, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var bundled = group.GetSchema<BundledAssetGroupSchema>();
            bundled.BuildPath.SetVariableByName(settings, remote ? AddressableAssetSettings.kRemoteBuildPath : AddressableAssetSettings.kLocalBuildPath);
            bundled.LoadPath.SetVariableByName(settings, remote ? AddressableAssetSettings.kRemoteLoadPath : AddressableAssetSettings.kLocalLoadPath);
            bundled.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            bundled.UseAssetBundleCache = remote;
            bundled.UseAssetBundleCrc = true;
            bundled.IncludeInBuild = true;
            var update = group.GetSchema<ContentUpdateGroupSchema>();
            update.StaticContent = !remote;
            EditorUtility.SetDirty(bundled);
            EditorUtility.SetDirty(update);
            EditorUtility.SetDirty(group);
        }
    }
}
