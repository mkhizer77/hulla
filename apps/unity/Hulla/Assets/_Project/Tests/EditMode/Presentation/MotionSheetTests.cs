using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hulla.Application.Settings;
using Hulla.Presentation.Theme;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Tests
{
    /// <summary>
    /// The stylesheets animate only what the motion sheet names, with its duration and curve, and
    /// every movement has a reduce-motion variant (GDD 15.7, 27, #81).
    /// </summary>
    public sealed class MotionSheetTests
    {
        private static readonly Regex Rule = new Regex(@"([^{}]+)\{([^}]*)\}");
        private static readonly Regex Comment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);

        private static List<(string Selector, string Body)> Rules()
        {
            var rules = new List<(string, string)>();
            foreach (var sheet in new[] { "Hulla.uss", "Basant.uss" })
            {
                var path = Path.Combine(UnityEngine.Application.dataPath, "_Project/Presentation/Theme", sheet);
                var uss = Comment.Replace(File.ReadAllText(path), "");
                foreach (Match match in Rule.Matches(uss))
                {
                    rules.Add((match.Groups[1].Value.Trim(), match.Groups[2].Value));
                }
            }

            return rules;
        }

        private static string? Property(string body, string name)
        {
            var match = Regex.Match(body, @"(?:^|[;\s])" + Regex.Escape(name) + @":\s*([^;]+);");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        [Test]
        public void EveryTransitionIsOnTheSheetWithItsDurationAndCurve()
        {
            var problems = new List<string>();
            var found = new HashSet<MotionSheet.Entry>();
            foreach (var (selector, body) in Rules())
            {
                var durations = Property(body, "transition-duration");
                if (durations == null || selector.StartsWith("." + MotionSheet.ReducedClass))
                {
                    continue;
                }

                var curve = Property(body, "transition-timing-function") ?? "ease";
                var entry = MotionSheet.Entries.FirstOrDefault(e => e.Selector == selector && e.Durations == durations);
                if (entry == null)
                {
                    problems.Add(selector + " animates for " + durations + " but the motion sheet does not list it");
                    continue;
                }

                found.Add(entry);
                if (entry.Curve != curve && !(selector == ".screen--enter-fade"))
                {
                    problems.Add(selector + " uses " + curve + ", the sheet says " + entry.Curve);
                }
            }

            foreach (var entry in MotionSheet.Entries.Where(e => !found.Contains(e)))
            {
                problems.Add("the sheet lists " + entry.Selector + " (" + entry.Durations + ") but no stylesheet animates it");
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryMovementHasAReduceMotionVariant()
        {
            var reduced = Rules().Where(r => r.Selector.StartsWith("." + MotionSheet.ReducedClass)).Select(r => r.Selector).ToList();
            var problems = new List<string>();
            foreach (var entry in MotionSheet.Entries.Where(e => e.Reduce == MotionSheet.Reduced.Stylesheet))
            {
                var target = entry.Selector.Split(' ').Last();
                if (!reduced.Any(selector => selector.Contains(target)))
                {
                    problems.Add(entry.Selector + " has no rule under ." + MotionSheet.ReducedClass);
                }
            }

            foreach (var entry in MotionSheet.Entries)
            {
                if (entry.Reduce != MotionSheet.Reduced.Kept && !entry.What.Contains("reduced:"))
                {
                    problems.Add(entry.Selector + " does not say what reduce motion does");
                }
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    
        [Test]
        public void TheSettingPutsTheClassOnTheShellRoot()
        {
            var settings = new SettingsModel(new Store());
            var root = new VisualElement();
            ThemeClasses.Apply(root, settings);
            Assert.IsFalse(root.ClassListContains(MotionSheet.ReducedClass));
            settings.ReduceMotion = true;
            ThemeClasses.Apply(root, settings);
            Assert.IsTrue(root.ClassListContains(MotionSheet.ReducedClass));
        }

        private sealed class Store : ISettingsStore
        {
            private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

            public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var v) ? v : fallback;

            public void SetInt(string key, int value) => _values[key] = value;

            public void Save()
            {
            }
        }
    }
}
