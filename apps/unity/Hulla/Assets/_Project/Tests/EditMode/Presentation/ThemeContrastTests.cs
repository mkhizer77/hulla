using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Presentation.Tests
{
    /// <summary>The theme tokens meet WCAG contrast and the 48 dp target (GDD 27) in both themes.</summary>
    public sealed class ThemeContrastTests
    {
        private static Dictionary<string, Color> Tokens(string block)
        {
            var uss = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "_Project/Presentation/Theme/Hulla.uss"));
            var start = uss.IndexOf(block + " {", StringComparison.Ordinal);
            var body = uss.Substring(start, uss.IndexOf('}', start) - start);
            var tokens = new Dictionary<string, Color>();
            foreach (Match m in Regex.Matches(body, @"--([a-z-]+):\s*(#[0-9A-Fa-f]{6})"))
            {
                ColorUtility.TryParseHtmlString(m.Groups[2].Value, out var colour);
                tokens[m.Groups[1].Value] = colour;
            }

            return tokens;
        }

        private static float Luminance(Color c)
        {
            float Channel(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);
        }

        private static float Ratio(Color a, Color b)
        {
            var la = Luminance(a);
            var lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        private static void Check(Dictionary<string, Color> t)
        {
            Assert.GreaterOrEqual(Ratio(t["ink-ivory"], t["surface-ui"]), 4.5f, "body text on screens");
            Assert.GreaterOrEqual(Ratio(t["ink-ivory"], t["surface-table"]), 4.5f, "body text on the table");
            Assert.GreaterOrEqual(Ratio(t["ink-muted"], t["surface-ui"]), 4.5f, "captions on screens");
            Assert.GreaterOrEqual(Ratio(t["ink-muted"], t["surface-table"]), 4.5f, "captions on the table");
            Assert.GreaterOrEqual(Ratio(t["accent-saffron"], t["surface-ui"]), 3f, "large accent text");
            Assert.GreaterOrEqual(Ratio(t["suit-red"], t["card-face"]), 4.5f, "red indices");
            Assert.GreaterOrEqual(Ratio(t["suit-black"], t["card-face"]), 4.5f, "black indices");
        }

        [Test]
        public void TheDefaultThemeMeetsContrast() => Check(Tokens(":root"));

        [Test]
        public void TheHighContrastThemeMeetsContrast()
        {
            var tokens = Tokens(":root");
            foreach (var pair in Tokens(".theme--contrast"))
            {
                tokens[pair.Key] = pair.Value;
            }

            Check(tokens);
        }

        [Test]
        public void FourColourSuitsReadOnTheCardFace()
        {
            var face = Tokens(":root")["card-face"];
            Assert.GreaterOrEqual(Ratio(new Color32(0x1E, 0x6F, 0xD9, 0xFF), face), 4.5f, "diamonds blue");
            Assert.GreaterOrEqual(Ratio(new Color32(0x2E, 0x7D, 0x32, 0xFF), face), 4.5f, "clubs green");
        }

        [Test]
        public void TapTargetsAreAtLeast48Dp()
        {
            var uss = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "_Project/Presentation/Theme/Hulla.uss"));
            var tap = int.Parse(Regex.Match(uss, @"--tap:\s*(\d+)px").Groups[1].Value);
            Assert.GreaterOrEqual(tap, 126, "48 dp is 126 px at 2.625 px per dp on a 1080 px phone");
        }
    }
}
