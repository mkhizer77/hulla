#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hulla.Presentation.Accessibility;
using Hulla.Presentation.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Accessibility;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// What TalkBack and VoiceOver read (GDD 27, #83): on every screen each visible button has words,
    /// the hand reads its cards by name, and a double tap runs the button's own action.
    /// </summary>
    public sealed class ScreenReaderTests
    {
        [TearDown]
        public void TearDown() => Strings.Use(Strings.Fallback);

        private static IEnumerator Open(ScreenCases.Case screen, Action<VisualElement> check)
        {
            var host = new GameObject("reader-" + screen.Name);
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
            // The shell names its top-bar icons as it shows a tab (AppFlow.TabTop, the Ranked rules button).
            if (instance.Q<Button>("gear") != null)
            {
                Hulla.Presentation.Navigation.TopBar.Tab(instance, "Tab", () => { });
            }

            Hulla.Presentation.Navigation.TopBar.On(instance, "rules", Hulla.Games.Thulla.RulebookText.Title, () => { });
            for (var frame = 0; frame < 3; frame++)
            {
                yield return null;
            }

            check(root);
            Object.Destroy(host);
            Object.Destroy(settings);
            texture.Release();
            Object.Destroy(texture);
        }

        [UnityTest]
        [Timeout(1800000)]
        public IEnumerator EveryVisibleButtonHasWordsOnEveryScreen()
        {
            var problems = new List<string>();
            foreach (var screen in ScreenCases.All())
            {
                yield return Open(screen, root =>
                {
                    foreach (var button in ScreenReaderTree.Unlabelled(root))
                    {
                        problems.Add(screen.Name + ": button \"" + button.name + "\" (" + string.Join(" ", button.GetClasses()) + ") has no words");
                    }

                    if (ScreenReaderTree.Read(root).Count == 0)
                    {
                        problems.Add(screen.Name + ": the reader finds nothing");
                    }
                });
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [UnityTest]
        public IEnumerator TheHandReadsItsCardsByName()
        {
            var table = ScreenCases.All().First(c => c.Name == "table");
            List<ScreenReaderTree.Item>? items = null;
            yield return Open(table, root => items = ScreenReaderTree.Read(root));
            var cards = items!.Where(i => i.Label.Contains(" of ")).Select(i => i.Label).ToList();
            CollectionAssert.IsNotEmpty(cards, "the hand's cards are read");
            Assert.IsTrue(cards.All(label => label.EndsWith("Spades") || label.EndsWith("Hearts") || label.EndsWith("Diamonds") || label.EndsWith("Clubs") || label.Contains("can't be played")),
                string.Join(", ", cards));
        }

        [UnityTest]
        public IEnumerator ADoubleTapRunsTheButtonsOwnAction()
        {
            var clicked = 0;
            var screen = new ScreenCases.Case("one-button", ScriptableObject.CreateInstance<VisualTreeAsset>(), root =>
            {
                var button = new Button(() => clicked++) { text = "Play now" };
                root.Add(button);
            });
            AccessibilityRole? role = null;
            yield return Open(screen, root =>
            {
                var item = ScreenReaderTree.Read(root).First(i => i.Label == "Play now");
                role = item.Role;
                item.Invoke!();
            });
            Assert.AreEqual(AccessibilityRole.Button, role);
            Assert.AreEqual(1, clicked);
        }

        [Test]
        public void CardsAndMomentsAreSpokenInWords()
        {
            Assert.AreEqual("King of Hearts", Speech.Card("KH"));
            Assert.AreEqual("Ten of Clubs", Speech.Card("TC"));
            Assert.AreEqual("Sara played Ace of Spades", Speech.Played("Sara", "AS", false));
            Assert.AreEqual("Ali picks up 1 card", Speech.PickedUp("Ali", 1));
            Assert.AreEqual("Ali picks up 7 cards", Speech.PickedUp("Ali", 7));
        }
    }
}
#endif
