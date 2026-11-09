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
                SetAndroidIcons(icon, foreground, background);
                SetIosIcons(icon);
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

        private static void SetAndroidIcons(Texture2D icon, Texture2D foreground, Texture2D background)
        {
            var target = NamedBuildTarget.Android;
            var adaptive = PlayerSettings.GetPlatformIcons(target, UnityEditor.Android.AndroidPlatformIconKind.Adaptive);
            foreach (var entry in adaptive)
            {
                // layer 0 is the background, layer 1 the foreground
                entry.SetTextures(background, foreground);
            }

            PlayerSettings.SetPlatformIcons(target, UnityEditor.Android.AndroidPlatformIconKind.Adaptive, adaptive);

            foreach (var kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Round, UnityEditor.Android.AndroidPlatformIconKind.Legacy })
            {
                var icons = PlayerSettings.GetPlatformIcons(target, kind);
                foreach (var entry in icons)
                {
                    entry.SetTexture(icon);
                }

                PlayerSettings.SetPlatformIcons(target, kind, icons);
            }
        }

        private static void SetIosIcons(Texture2D icon)
        {
            var target = NamedBuildTarget.iOS;
            foreach (var kind in new[]
                     {
                         UnityEditor.iOS.iOSPlatformIconKind.Application,
                         UnityEditor.iOS.iOSPlatformIconKind.Spotlight,
                         UnityEditor.iOS.iOSPlatformIconKind.Settings,
                         UnityEditor.iOS.iOSPlatformIconKind.Notification,
                         UnityEditor.iOS.iOSPlatformIconKind.Marketing,
                     })
            {
                var icons = PlayerSettings.GetPlatformIcons(target, kind);
                foreach (var entry in icons)
                {
                    entry.SetTexture(icon);
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
