using Hulla.Presentation.Navigation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Tests
{
    /// <summary>Scaling, size classes and safe-area insets (docs/design/ui-redesign-brief.md §2.2 to §2.4).</summary>
    public sealed class ScreenLayoutTests
    {
        [TestCase(720, 1600, 1080f, 2400f)]
        [TestCase(1080, 2400, 1080f, 2400f)]
        [TestCase(1080, 1920, 1080f, 1920f)]
        [TestCase(1200, 1920, 1200f, 1920f)]
        [TestCase(1620, 2160, 1440f, 1920f)]
        [TestCase(2048, 2732, 1439.3f, 1920f)]
        [TestCase(1920, 1200, 1920f, 1200f)]
        [TestCase(2732, 2048, 1920f, 1439.3f)]
        public void ExpandFitsTheReferenceAndGrowsTheOtherWay(int width, int height, float canvasWidth, float canvasHeight)
        {
            var canvas = ScreenLayout.Canvas(width, height);
            Assert.AreEqual(canvasWidth, canvas.x, 0.1f);
            Assert.AreEqual(canvasHeight, canvas.y, 0.1f);
        }

        [Test]
        public void TheReferenceTurnsWithTheScreen()
        {
            Assert.AreEqual(new Vector2Int(1080, 1920), ScreenLayout.Reference(1080, 2400));
            Assert.AreEqual(new Vector2Int(1920, 1080), ScreenLayout.Reference(2400, 1080));
            Assert.AreEqual(new Vector2Int(1080, 1920), ScreenLayout.Reference(1000, 1000));
        }

        [TestCase(1080f, SizeClass.Compact)]
        [TestCase(1199f, SizeClass.Compact)]
        [TestCase(1199.9f, SizeClass.Compact)]
        [TestCase(1200f, SizeClass.Medium)]
        [TestCase(1440f, SizeClass.Medium)]
        [TestCase(1799f, SizeClass.Medium)]
        [TestCase(1800f, SizeClass.Expanded)]
        [TestCase(1920f, SizeClass.Expanded)]
        public void SizeClassThresholds(float canvasWidth, SizeClass expected)
        {
            Assert.AreEqual(expected, ScreenLayout.ClassOf(canvasWidth));
        }

        [TestCase(1080, 2400, SizeClass.Compact)]
        [TestCase(1200, 1920, SizeClass.Medium)]
        [TestCase(2048, 2732, SizeClass.Medium)]
        [TestCase(2732, 2048, SizeClass.Expanded)]
        [TestCase(1920, 1200, SizeClass.Expanded)]
        public void DevicesLandInTheBriefsClasses(int width, int height, SizeClass expected)
        {
            Assert.AreEqual(expected, ScreenLayout.ClassOf(ScreenLayout.Canvas(width, height).x));
        }

        [Test]
        public void APhoneNotchAndHomeBarBecomeTopAndBottomInsets()
        {
            // 1179 × 2556 phone: safe area from y 102 to 2394 (bottom-left origin); scale 1179 / 1080.
            var insets = ScreenLayout.SafeInsets(new Rect(0, 102, 1179, 2292), 1179, 2556);
            var scale = 1179f / 1080f;
            Assert.AreEqual(162f / scale, insets.Top, 0.01f);
            Assert.AreEqual(102f / scale, insets.Bottom, 0.01f);
            Assert.AreEqual(0f, insets.Left);
            Assert.AreEqual(0f, insets.Right);
        }

        [Test]
        public void ALandscapeTabletBarOnTheSideBecomesASideInset()
        {
            // 2560 × 1600 tablet, a 3-button bar of 168 px on the right, status bar 80 px on top; scale 2560 / 1920.
            var insets = ScreenLayout.SafeInsets(new Rect(0, 0, 2392, 1520), 2560, 1600);
            var scale = 2560f / 1920f;
            Assert.AreEqual(80f / scale, insets.Top, 0.01f);
            Assert.AreEqual(168f / scale, insets.Right, 0.01f);
            Assert.AreEqual(0f, insets.Bottom);
            Assert.AreEqual(0f, insets.Left);
        }

        [Test]
        public void AFullScreenSafeAreaHasNoInsets()
        {
            var insets = ScreenLayout.SafeInsets(new Rect(0, 0, 1080, 1920), 1080, 1920);
            Assert.AreEqual(0f, insets.Top);
            Assert.AreEqual(0f, insets.Right);
            Assert.AreEqual(0f, insets.Bottom);
            Assert.AreEqual(0f, insets.Left);
        }

        [Test]
        public void AnEmptySafeAreaHasNoInsets()
        {
            var insets = ScreenLayout.SafeInsets(Rect.zero, 1080, 1920);
            Assert.AreEqual(0f, insets.Top);
            Assert.AreEqual(0f, insets.Bottom);
        }

        [Test]
        public void TheRootCarriesOneSizeClassAndThePadding()
        {
            var root = new VisualElement();
            ScreenLayout.Apply(root, SizeClass.Compact, new Insets(10, 0, 20, 0));
            ScreenLayout.Apply(root, SizeClass.Expanded, new Insets(30, 40, 50, 60));
            Assert.IsTrue(root.ClassListContains(ScreenLayout.ExpandedClass));
            Assert.IsFalse(root.ClassListContains(ScreenLayout.CompactClass));
            Assert.IsFalse(root.ClassListContains(ScreenLayout.MediumClass));
            Assert.AreEqual(30f, root.style.paddingTop.value.value);
            Assert.AreEqual(40f, root.style.paddingRight.value.value);
            Assert.AreEqual(50f, root.style.paddingBottom.value.value);
            Assert.AreEqual(60f, root.style.paddingLeft.value.value);
        }

        [TestCase(1080, 2400, 420f, false)]
        [TestCase(1290, 2796, 460f, false)]
        [TestCase(1620, 2160, 264f, true)]
        [TestCase(1200, 1920, 240f, true)]
        [TestCase(2208, 1840, 373f, true)]
        [TestCase(1080, 2400, 0f, false)]
        public void ATabletIsAtLeast600DpOnItsShortSide(int width, int height, float dpi, bool tablet)
        {
            Assert.AreEqual(tablet, OrientationPolicy.IsTablet(width, height, dpi));
        }

        [Test]
        public void ThePanelScalesWithExpandFromThePortraitReference()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            Assert.AreEqual(PanelScaleMode.ScaleWithScreenSize, settings.scaleMode);
            Assert.AreEqual(PanelScreenMatchMode.Expand, settings.screenMatchMode);
            Assert.AreEqual(new Vector2Int(1080, 1920), settings.referenceResolution);
        }
    }
}
