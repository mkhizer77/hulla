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
    /// The walkthrough with the server down, and with nothing to show (#82, GDD 14.13): every list
    /// screen in <see cref="ScreenCases.States"/> says what happened in plain words with Retry, or has
    /// one sentence and one action when empty. No screen falls back to the catch-all error line.
    /// </summary>
    public sealed class ListStatesTests
    {
        [TearDown]
        public void TearDown() => Strings.Use(Strings.Fallback);

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator EveryListSaysWhatHappenedAndOffersOneAction()
        {
            var problems = new List<string>();
            var checkedCases = 0;
            foreach (var screen in ScreenCases.States())
            {
                yield return Check(screen, problems);
                checkedCases++;
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
            Assert.GreaterOrEqual(checkedCases, 12);
        }

        private static IEnumerator Check(ScreenCases.Case screen, List<string> problems)
        {
            var host = new GameObject("states-" + screen.Name);
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

            var unknown = Strings.Text("error.unknown");
            var offline = Strings.Text("error.offline");
            var sawOffline = false;
            root.Query<TextElement>().ForEach(text =>
            {
                var value = text.text ?? "";
                if (text.resolvedStyle.display == DisplayStyle.None)
                {
                    return;
                }

                if (value == unknown)
                {
                    problems.Add(screen.Name + ": falls back to the catch-all error line");
                }

                if (value.Contains(".unavailable") || value.Contains("Exception"))
                {
                    problems.Add(screen.Name + ": shows a raw error \"" + value + "\"");
                }

                sawOffline |= value == offline;
            });

            var empties = root.Query<VisualElement>(className: "empty").ToList();
            if (screen.Name.EndsWith("-down"))
            {
                if (!sawOffline)
                {
                    problems.Add(screen.Name + ": does not say the connection is down");
                }

                if (!HasButton(root, UiText.Retry))
                {
                    problems.Add(screen.Name + ": has no Retry");
                }
            }
            else if (empties.Count == 0)
            {
                problems.Add(screen.Name + ": has no empty state");
            }
            else if (empties[0].Query<Button>().ToList().Count != 1)
            {
                problems.Add(screen.Name + ": the empty state needs exactly one action");
            }

            Object.Destroy(host);
            Object.Destroy(settings);
            texture.Release();
            Object.Destroy(texture);
        }

        private static bool HasButton(VisualElement root, string text)
        {
            var found = false;
            root.Query<Button>().ForEach(button => found |= button.text == text && button.resolvedStyle.display != DisplayStyle.None);
            return found;
        }
    }
}
#endif
