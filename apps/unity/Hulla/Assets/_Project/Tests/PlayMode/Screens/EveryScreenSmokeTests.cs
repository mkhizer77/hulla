#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hulla.Presentation.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// A smoke test for every screen and sheet in <see cref="ScreenCases"/> (#78): it opens with its fakes
    /// bound, logs no error, shows some text, and every text is a finished string: no raw string key,
    /// no unfilled {0} and no "?" stand-in for a missing name.
    /// </summary>
    public sealed class EveryScreenSmokeTests
    {
        private static readonly Regex RawKey = new Regex(@"^[a-z]+(\.[a-z0-9_]+)+$");

        [TearDown]
        public void TearDown() => Strings.Use(Strings.Fallback);

        [UnityTest]
        [Timeout(1800000)]
        public IEnumerator EveryScreenOpensWithItsTextBound()
        {
            var problems = new List<string>();
            var opened = 0;
            foreach (var screen in ScreenCases.All())
            {
                yield return Check(screen, problems);
                opened++;
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
            Assert.GreaterOrEqual(opened, 25, "the cases cover every screen of the shell's catalog and the sheets");
        }

        private static IEnumerator Check(ScreenCases.Case screen, List<string> problems)
        {
            var host = new GameObject("smoke-" + screen.Name);
            var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset"));
            var texture = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32);
            settings.targetTexture = texture;
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            yield return null;
            var root = document.rootVisualElement;
            ScreenCases.Skin(root, Hulla.Application.Settings.TableSkin.Basant);
            var instance = screen.Screen!.Instantiate();
            instance.style.flexGrow = 1;
            root.Add(instance);
            screen.Bind(instance);
            for (var frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            var shown = 0;
            root.Query<TextElement>().ForEach(text =>
            {
                var value = text.text ?? "";
                if (value.Trim().Length == 0 || text.resolvedStyle.display == DisplayStyle.None)
                {
                    return;
                }

                shown++;
                if (RawKey.IsMatch(value))
                {
                    problems.Add(screen.Name + ": raw string key \"" + value + "\"");
                }

                if (value.Contains("{0}") || value.Contains("{1}"))
                {
                    problems.Add(screen.Name + ": unfilled placeholder in \"" + value + "\"");
                }

                if (value == "?")
                {
                    problems.Add(screen.Name + ": a name stands in as \"?\" (" + text.name + ")");
                }
            });
            if (shown == 0)
            {
                problems.Add(screen.Name + ": shows no text");
            }

            Object.Destroy(host);
            Object.Destroy(settings);
            texture.Release();
            Object.Destroy(texture);
        }
    }
}
#endif
