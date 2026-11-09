using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Hulla.Architecture.Tests
{
    /// <summary>
    /// The store icon, the Android layers, the splash logo and the notification icon a build ships
    /// with. They live in ProjectSettings and in an Android library, where nothing else checks them.
    /// </summary>
    public sealed class BrandAssetsTests
    {
        private const string AndroidLibrary = "Assets/Plugins/Android/hullabrand.androidlib";

        [Test]
        public void TheDefaultIconIsSet()
        {
            var icons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            Assert.That(icons, Is.Not.Empty);
            Assert.IsNotNull(icons[0], "default icon texture");
            Assert.That(icons[0].width, Is.EqualTo(1024));
        }

        [Test]
        public void AndroidAdaptiveIconsHaveBothLayers()
        {
            var adaptive = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, UnityEditor.Android.AndroidPlatformIconKind.Adaptive);
            Assert.That(adaptive, Is.Not.Empty);
            foreach (var icon in adaptive)
            {
                var layers = icon.GetTextures();
                Assert.That(layers.Length, Is.EqualTo(2), "two layers at " + icon.width);
                Assert.IsNotNull(layers[0], "background at " + icon.width);
                Assert.IsNotNull(layers[1], "foreground at " + icon.width);
            }
        }

        [Test]
        public void IosIconsAreSet()
        {
            foreach (var kind in new[] { UnityEditor.iOS.iOSPlatformIconKind.Application, UnityEditor.iOS.iOSPlatformIconKind.Marketing })
            {
                foreach (var icon in PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind))
                {
                    Assert.IsNotNull(icon.GetTexture(), kind + " at " + icon.width);
                }
            }
        }

        [Test]
        public void TheSplashShowsTheWordmarkOnHullaRed()
        {
            Assert.That(PlayerSettings.SplashScreen.logos.Length, Is.EqualTo(1));
            Assert.IsNotNull(PlayerSettings.SplashScreen.logos[0].logo, "splash logo sprite");
            var ground = (Color32)PlayerSettings.SplashScreen.backgroundColor;
            Assert.That(new[] { (int)ground.r, (int)ground.g, (int)ground.b }, Is.EqualTo(new[] { 0xE8, 0x41, 0x2C }), "Hulla red");
        }

        [Test]
        public void TheAndroidLibraryCarriesTheNotificationIcon()
        {
            foreach (var density in new[] { "mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi" })
            {
                Assert.IsTrue(File.Exists(Path.Combine(AndroidLibrary, "res", "drawable-" + density, "ic_stat_hulla.png")), density);
            }

            var manifest = File.ReadAllText(Path.Combine(AndroidLibrary, "AndroidManifest.xml"));
            StringAssert.Contains("com.google.firebase.messaging.default_notification_icon", manifest);
            StringAssert.Contains("@drawable/ic_stat_hulla", manifest);
            StringAssert.Contains("com.google.firebase.messaging.default_notification_color", manifest);
            Assert.IsTrue(File.Exists(Path.Combine(AndroidLibrary, "res", "values", "hulla_brand_colors.xml")));
        }
    }
}
