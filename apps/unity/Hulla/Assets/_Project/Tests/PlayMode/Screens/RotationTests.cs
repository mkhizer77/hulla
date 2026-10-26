#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// A tablet turned mid-round re-lays out the table and keeps it: same screen, same hand, same turn
    /// (docs/design/ui-redesign-brief.md §2.1).
    /// </summary>
    public sealed class RotationTests
    {
        [UnityTest]
        public IEnumerator TurningATabletMidRoundKeepsTheTable()
        {
            var host = new GameObject("rotation");
            var texture = new RenderTexture(1920, 1920, 24, RenderTextureFormat.ARGB32);
            var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset"));
            settings.targetTexture = texture;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            try
            {
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                var root = document.rootVisualElement;
                Resize(root, 1440, 1920, SizeClass.Medium);
                var catalog = AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");
                var instance = catalog.Table!.Instantiate();
                instance.style.flexGrow = 1;
                root.Add(instance);
                var gateway = new Fakes.Gateway();
                var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
                new ThullaTableView(instance, client, new Fakes.Chat(), () => 5000, () => { });
                var state = Fakes.InRound(5);
                state.Round!.Seats[0].Hand = new List<string> { "AS", "KS", "9S", "QH", "8H", "2H", "JD", "9D", "5C", "3C", "TC" };
                state.Round.Seats[0].HandCount = 11;
                gateway.Deliver(MessageTypes.TableState, 5, state);
                gateway.Deliver(MessageTypes.TurnStart, 5, new TurnStart { RoundId = "r", Seat = 0, Legal = new List<string> { "QH", "8H", "2H" }, DeadlineTs = 15000 });
                yield return Frames();
                var before = Snapshot(instance);
                var handBefore = instance.Q("hand").worldBound;
                Assert.AreEqual(11, before.Cards, "the hand before turning");

                Resize(root, 1920, 1440, SizeClass.Expanded);
                yield return Frames();

                Assert.AreSame(instance, root[0], "the table screen is the same element");
                Assert.AreEqual(before, Snapshot(instance), "the table after turning");
                var handAfter = instance.Q("hand").worldBound;
                Assert.Greater(handAfter.width, handBefore.width, "the hand takes the wider screen");
                Assert.LessOrEqual(handAfter.yMax, 1440f - 60f + 0.5f, "the hand sits above the bottom inset");

                Resize(root, 1440, 1920, SizeClass.Medium);
                yield return Frames();
                Assert.AreEqual(before, Snapshot(instance), "the table after turning back");
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(settings);
                texture.Release();
                Object.Destroy(texture);
            }
        }

        private static void Resize(VisualElement root, int width, int height, SizeClass size)
        {
            root.style.width = width;
            root.style.height = height;
            ScreenLayout.Apply(root, size, new Insets(70, 0, 60, 0));
        }

        private static IEnumerator Frames()
        {
            for (var frame = 0; frame < 4; frame++)
            {
                yield return null;
            }
        }

        private static (int Cards, int Legal, string Labels) Snapshot(VisualElement table)
        {
            var hand = table.Q("hand");
            var cards = hand.Query(className: "card").ToList();
            var legal = cards.Count(c => c.ClassListContains("card--legal"));
            var labels = string.Join("|", table.Query<Label>().ToList().Where(l => l.resolvedStyle.display == DisplayStyle.Flex).Select(l => l.text));
            return (cards.Count, legal, labels);
        }
    }
}
#endif
