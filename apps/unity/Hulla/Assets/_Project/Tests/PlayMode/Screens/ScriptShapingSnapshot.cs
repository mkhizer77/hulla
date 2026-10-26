#if UNITY_EDITOR
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using Hulla.Presentation.Share;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// The #132 spike: Urdu in Nastaliq, Hindi in Devanagari and Bengali through UI Toolkit, each with the
    /// standard and the advanced text generator, a right-to-left row, a Latin font falling back to each
    /// script, and line heights and text update times written next to the PNGs. Explicit:
    /// <c>-testFilter Hulla.Screens.Tests.ScriptShapingSnapshot</c>, files in HULLA_SNAPSHOT_DIR.
    /// </summary>
    public sealed class ScriptShapingSnapshot
    {
        private const string Fonts = "Assets/_Project/Presentation/Theme/Fonts/";
        private const int FontSize = 44;

        private static readonly Sample[] Samples =
        {
            new Sample("urdu", "NotoNastaliqUrdu.ttf", true,
                "آپ کی باری ہے۔ پتّا چلیں یا ٹھُلّا لیں!",
                "نستعلیق میں لکھائی: ۳ کھلاڑی، ۲ منٹ، Rung",
                "کھیلیں"),
            new Sample("hindi", "NotoSansDevanagari.ttf", false,
                "आपकी बारी है। पत्ता चलें या ठुल्ला लें!",
                "संयुक्ताक्षर: क्षत्रिय, द्वार, श्रृंगार, ह्रदय",
                "खेलें"),
            new Sample("bengali", "NotoSansBengali.ttf", false,
                "আপনার পালা। একটা তাস খেলুন বা ঠুল্লা নিন!",
                "যুক্তাক্ষর: ক্ষমা, সঙ্গী, স্ত্রী, রাষ্ট্র",
                "খেলুন"),
        };

        [UnityTest, Explicit]
        public IEnumerator Standard() => Snap(TextGeneratorType.Standard);

        [UnityTest, Explicit]
        public IEnumerator Advanced() => Snap(TextGeneratorType.Advanced);

        private static IEnumerator Snap(TextGeneratorType generator)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var latin = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "Nunito.ttf");
            var notes = new StringBuilder();
            var labels = new System.Collections.Generic.List<(string? Name, Label Label)>();
            var fallback = FontAsset.CreateFontAsset(latin);
            fallback.fallbackFontAssetTable ??= new System.Collections.Generic.List<FontAsset>();
            foreach (var sample in Samples)
            {
                fallback.fallbackFontAssetTable.Add(FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(Fonts + sample.Font)));
            }

            var clock = Stopwatch.StartNew();
            var task = CardRenderer.RenderAsync(settings, 1080, 1920, root =>
            {
                root.style.backgroundColor = Color.white;
                root.style.paddingLeft = root.style.paddingRight = 32;
                root.style.paddingTop = 24;
                root.style.unityTextGenerator = generator;
                Add(root, Text("Latin reference: Your turn. Play a card or take the thulla!", latin, null), labels, "latin");
                foreach (var sample in Samples)
                {
                    var font = AssetDatabase.LoadAssetAtPath<Font>(Fonts + sample.Font);
                    Add(root, Heading(sample.Name), labels, null);
                    Add(root, Text(sample.Line, font, sample.RightToLeft), labels, sample.Name);
                    Add(root, Text(sample.Hard, font, sample.RightToLeft), labels, sample.Name + "-hard");
                    var row = new VisualElement();
                    row.style.flexDirection = sample.RightToLeft ? FlexDirection.RowReverse : FlexDirection.Row;
                    row.style.marginBottom = 8;
                    var button = new Button { text = sample.Button };
                    button.style.unityFontDefinition = FontDefinition.FromFont(font);
                    button.style.fontSize = FontSize;
                    button.style.minHeight = 96;
                    row.Add(button);
                    var mixed = Text("Nunito → " + sample.Button, null, sample.RightToLeft);
                    mixed.style.unityFontDefinition = FontDefinition.FromSDFFont(fallback);
                    row.Add(mixed);
                    root.Add(row);
                    labels.Add((sample.Name + "-fallback", mixed));
                }
            });
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            notes.AppendLine($"generator {generator}; first render {clock.ElapsedMilliseconds} ms (fonts loaded, three frames)");
            foreach (var (name, label) in labels)
            {
                if (name != null)
                {
                    notes.AppendLine($"{name}: height {label.layout.height:0} px at {FontSize} px type, width {label.layout.width:0}");
                }
            }

            var dir = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_DIR") ?? "Temp";
            Directory.CreateDirectory(dir);
            var stem = "scripts-" + generator.ToString().ToLowerInvariant();
            File.WriteAllBytes(Path.Combine(dir, stem + ".png"), awaiter.GetResult());
            File.WriteAllText(Path.Combine(dir, stem + ".txt"), notes.ToString());
            Assert.Pass(notes.ToString());
        }

        /// <summary>Times text changes on thirty labels a frame, the busiest a table gets (names, counts, chat).</summary>
        [UnityTest, Explicit]
        public IEnumerator UpdateCost()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var host = new GameObject("shaping-cost") { hideFlags = HideFlags.HideAndDontSave };
            var texture = new RenderTexture(1080, 1920, 24);
            var copy = Object.Instantiate(settings);
            copy.targetTexture = texture;
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = copy;
            var notes = new StringBuilder();
            foreach (var generator in new[] { TextGeneratorType.Standard, TextGeneratorType.Advanced })
            {
                foreach (var sample in Samples)
                {
                    var root = document.rootVisualElement;
                    root.Clear();
                    root.style.unityTextGenerator = generator;
                    var font = AssetDatabase.LoadAssetAtPath<Font>(Fonts + sample.Font);
                    var labels = new Label[30];
                    for (var i = 0; i < labels.Length; i++)
                    {
                        labels[i] = Text(sample.Line, font, sample.RightToLeft);
                        root.Add(labels[i]);
                    }

                    yield return null;
                    const int frames = 60;
                    var clock = Stopwatch.StartNew();
                    for (var frame = 0; frame < frames; frame++)
                    {
                        for (var i = 0; i < labels.Length; i++)
                        {
                            labels[i].text = (frame % 2 == 0 ? sample.Line : sample.Hard) + " " + (frame + i);
                        }

                        yield return null;
                    }

                    notes.AppendLine($"{generator} {sample.Name}: {clock.Elapsed.TotalMilliseconds / frames:0.00} ms a frame with 30 labels changing");
                }
            }

            Object.Destroy(host);
            Object.Destroy(copy);
            texture.Release();
            var dir = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_DIR") ?? "Temp";
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "scripts-update-cost.txt"), notes.ToString());
            Assert.Pass(notes.ToString());
        }

        private static void Add(VisualElement root, Label label, System.Collections.Generic.List<(string? Name, Label Label)> labels, string? name)
        {
            root.Add(label);
            if (name != null)
            {
                labels.Add((name, label));
            }
        }

        private static Label Heading(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 24;
            label.style.color = Color.gray;
            label.style.marginTop = 16;
            return label;
        }

        private static Label Text(string text, Font? font, bool? rightToLeft)
        {
            var label = new Label(text);
            if (font != null)
            {
                label.style.unityFontDefinition = FontDefinition.FromFont(font);
            }

            label.style.fontSize = FontSize;
            label.style.color = Color.black;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = rightToLeft == true ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            if (rightToLeft == true)
            {
                // The paragraph direction: without it a line ending in Latin or punctuation lays out left to right.
                label.languageDirection = LanguageDirection.RTL;
            }

            label.style.borderBottomWidth = 1;
            label.style.borderBottomColor = new Color(0.85f, 0.85f, 0.85f);
            return label;
        }

        private sealed class Sample
        {
            public Sample(string name, string font, bool rightToLeft, string line, string hard, string button)
            {
                Name = name;
                Font = font;
                RightToLeft = rightToLeft;
                Line = line;
                Hard = hard;
                Button = button;
            }

            public string Name { get; }

            public string Font { get; }

            public bool RightToLeft { get; }

            public string Line { get; }

            public string Hard { get; }

            public string Button { get; }
        }
    }
}
#endif
