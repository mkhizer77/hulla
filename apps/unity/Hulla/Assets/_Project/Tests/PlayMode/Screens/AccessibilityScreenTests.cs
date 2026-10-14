#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Hulla.Presentation.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// The accessibility baseline on every screen (GDD 27, #64), laid out on a 1080 × 1920 phone panel:
    /// every visible control is at least 48 dp (132 px at this width) high, and text on a solid
    /// background reaches 4.5:1 contrast, 3:1 when it is large. Text over an image is left to the
    /// theme's own token check, since its background is not one colour.
    /// </summary>
    public sealed class AccessibilityScreenTests
    {
        private const int Width = 1080;
        private const int Height = 1920;
        private const float Tap = 132f;
        /// <summary>WCAG 2.2 target size (minimum), 24 dp at this width.</summary>
        private const float WcagMinimum = 66f;
        /// <summary>18 pt at this panel's scale: the WCAG line between normal and large text.</summary>
        private const float LargeText = 66f;

        /// <summary>
        /// The table's top bar and quick-chat strip are 26 dp pills: above WCAG 2.2's 24 dp minimum, under
        /// the GDD's 48 dp. Whether to grow them is the owner's call on the themed table (OQ-043).
        /// </summary>
        private static readonly string[] SmallByDesign = { "#leave", "#chat", "quick-strip__chip", "quick-strip__more" };

        [TearDown]
        public void TearDown() => Strings.Use(Strings.Fallback);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator EveryControlIsBigEnoughAndEveryTextReadable()
        {
            var problems = new List<string>();
            foreach (Hulla.Application.Settings.TableSkin skin in System.Enum.GetValues(typeof(Hulla.Application.Settings.TableSkin)))
            {
                foreach (var screen in ScreenCases.All())
                {
                    yield return Check(screen, skin, problems);
                }
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        private static IEnumerator Check(ScreenCases.Case screen, Hulla.Application.Settings.TableSkin skin, List<string> problems)
        {
            {
                var host = new GameObject("a11y-" + screen.Name);
                var texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset"));
                settings.targetTexture = texture;
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                settings.scale = 1;
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                var root = document.rootVisualElement;
                root.style.width = Width;
                root.style.height = Height;
                ScreenCases.Skin(root, skin);
                var instance = screen.Screen!.Instantiate();
                instance.style.flexGrow = 1;
                root.Add(instance);
                screen.Bind(instance);
                for (var frame = 0; frame < 4; frame++)
                {
                    yield return null;
                }

                var where = skin + " " + screen.Name;
                problems.AddRange(Targets(root, where));
                problems.AddRange(Contrast(root, where));
                Object.Destroy(host);
                Object.Destroy(settings);
                texture.Release();
                Object.Destroy(texture);
            }
        }

        private static IEnumerable<string> Targets(VisualElement root, string screen)
        {
            foreach (var control in root.Query<VisualElement>().Where(e => e is Button || e is Toggle || e is TextField || e is Slider || e is SliderInt).ToList())
            {
                if (!Shown(control) || !control.enabledInHierarchy || control.pickingMode == PickingMode.Ignore)
                {
                    continue;
                }

                var box = control.worldBound;
                var name = Name(control);
                if (System.Array.Exists(SmallByDesign, n => name.Contains(n)) && box.height + 1 >= WcagMinimum)
                {
                    continue;
                }

                if (box.height + 1 < Tap)
                {
                    yield return $"{screen} {name}: {box.height:F0}px high, needs {Tap:F0}";
                }
            }
        }

        private static IEnumerable<string> Contrast(VisualElement root, string screen)
        {
            foreach (var element in root.Query<TextElement>().ToList())
            {
                if (string.IsNullOrEmpty(element.text) || !Shown(element))
                {
                    continue;
                }

                var background = SolidBackground(element);
                if (background == null)
                {
                    continue;
                }

                var text = element.resolvedStyle.color;
                text.a *= element.resolvedStyle.opacity;
                if (text.a < 0.99f)
                {
                    text = Color.Lerp(background.Value, new Color(text.r, text.g, text.b, 1), text.a);
                }

                var ratio = Ratio(text, background.Value);
                var needed = element.resolvedStyle.fontSize >= LargeText ? 3f : 4.5f;
                if (ratio + 0.01f < needed)
                {
                    yield return $"{screen} {Name(element)} \"{element.text}\": contrast {ratio:F2}, needs {needed}";
                }
            }
        }

        /// <summary>The first opaque background colour behind an element, or null when an image or nothing is behind it.</summary>
        private static Color? SolidBackground(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                var style = e.resolvedStyle;
                if (style.backgroundImage.texture != null || style.backgroundImage.sprite != null || style.backgroundImage.vectorImage != null || style.backgroundImage.renderTexture != null)
                {
                    return null;
                }

                if (style.backgroundColor.a >= 0.99f)
                {
                    return style.backgroundColor;
                }

                if (style.backgroundColor.a > 0.01f)
                {
                    return null;
                }
            }

            return null;
        }

        private static float Ratio(Color a, Color b)
        {
            var la = Luminance(a);
            var lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        private static float Luminance(Color c)
        {
            static float Channel(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);
        }

        private static bool Shown(VisualElement element)
        {
            if (element.layout.width <= 0 || element.layout.height <= 0)
            {
                return false;
            }

            for (var e = element; e != null; e = e.parent)
            {
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.visibility == UnityEngine.UIElements.Visibility.Hidden || e.resolvedStyle.opacity <= 0.01f)
                {
                    return false;
                }
            }

            return true;
        }

        private static string Name(VisualElement element) =>
            string.IsNullOrEmpty(element.name) ? element.GetType().Name + "." + string.Join(".", element.GetClasses()) : "#" + element.name;
    }
}
#endif
