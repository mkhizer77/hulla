using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Hulla.BuildTools
{
    /// <summary>
    /// The app icon, the Android adaptive layers, the iOS icon set and the splash logo, from the
    /// brand kit's textures. Runs once after the textures change:
    /// <c>Unity -batchmode -quit -nographics -projectPath apps/unity/Hulla -executeMethod Hulla.BuildTools.BrandSetup.Apply</c>.
    /// The result lives in ProjectSettings.asset, so the build machines need nothing else.
    /// </summary>
    public static class BrandSetup
    {
        private const string Folder = "Assets/_Project/Presentation/Theme/Art/Brand/";
        private const string Icon = Folder + "app-icon-1024.png";
        private const string AdaptiveForeground = Folder + "app-icon-adaptive-foreground.png";
        private const string AdaptiveBackground = Folder + "app-icon-adaptive-background.png";
        private const string SplashWordmark = Folder + "splash-wordmark.png";

        /// <summary>Hulla red, the splash ground and the studio's colour.</summary>
        private static readonly Color HullaRed = new Color32(0xE8, 0x41, 0x2C, 0xFF);

        [MenuItem("Hulla/Apply brand icons and splash")]
        public static void Apply()
        {
            var ok = true;
            try
            {
                ImportAsIcon(Icon);
                ImportAsIcon(AdaptiveForeground);
                ImportAsIcon(AdaptiveBackground);
                ImportAsSprite(SplashWordmark);

                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Icon);
                var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveForeground);
                var background = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveBackground);
                var wordmark = AssetDatabase.LoadAssetAtPath<Sprite>(SplashWordmark);
                if (icon == null || foreground == null || background == null || wordmark == null)
                {
                    Debug.LogError("brand: a texture is missing under " + Folder);
                    ok = false;
                    return;
                }

                SetDefaultIcon(icon);
                SetPlatformIcons(NamedBuildTarget.Android, icon, background, foreground);
                SetPlatformIcons(NamedBuildTarget.iOS, icon, background, foreground);
                SetSplash(wordmark);

                AssetDatabase.SaveAssets();
                Debug.Log("brand: icons and splash applied");
            }
            finally
            {
                if (UnityEngine.Application.isBatchMode)
                {
                    EditorApplication.Exit(ok ? 0 : 1);
                }
            }
        }

        private static void SetDefaultIcon(Texture2D icon)
        {
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        /// <summary>
        /// Every icon kind the platform supports: single-layer kinds (legacy, round, the iOS set) get
        /// the icon; the two-layer kind (Android adaptive) gets the background and the foreground.
        /// The kinds come from the editor, so this compiles without the Android and iOS modules,
        /// which the CI test image does not have; there it simply finds no kinds.
        /// </summary>
        private static void SetPlatformIcons(NamedBuildTarget target, Texture2D icon, Texture2D background, Texture2D foreground)
        {
            PlatformIconKind[] kinds;
            try
            {
                kinds = PlayerSettings.GetSupportedIconKinds(target);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"brand: {target.TargetName} icons skipped, module not installed ({e.Message})");
                return;
            }

            foreach (var kind in kinds)
            {
                var icons = PlayerSettings.GetPlatformIcons(target, kind);
                foreach (var entry in icons)
                {
                    if (entry.maxLayerCount >= 2)
                    {
                        entry.SetTextures(background, foreground);   // layer 0 under layer 1
                    }
                    else
                    {
                        entry.SetTexture(icon);
                    }
                }

                PlayerSettings.SetPlatformIcons(target, kind, icons);
            }
        }

        private static void SetSplash(Sprite wordmark)
        {
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.backgroundColor = HullaRed;
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.UnityLogoBelow;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Dolly;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, wordmark) };
        }

        private static void ImportAsIcon(string path)
        {
            var importer = Importer(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        private static void ImportAsSprite(string path)
        {
            var importer = Importer(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        private static TextureImporter Importer(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(path);
            }

            AssetDatabase.ImportAsset(path);
            return (TextureImporter)AssetImporter.GetAtPath(path);
        }
    }
}
