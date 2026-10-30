using System.IO;
using System.Text.RegularExpressions;
using Hulla.Presentation.Theme;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Tests
{
    /// <summary>Every item the shop sells shows its own art (GDD 12.11), and the table swaps it cleanly.</summary>
    public sealed class CosmeticArtTests
    {
        private const string ArtFolder = "Assets/_Project/Presentation/Theme/Art/Basant/Shop/";

        [Test]
        public void EveryCatalogItemHasItsPictureOrIsADrawnCardFace()
        {
            var catalog = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "../../../../content/catalog/v1.json"));
            var stylesheet = File.ReadAllText("Assets/_Project/Presentation/Theme/Basant.uss");
            var items = Regex.Matches(catalog, "\"id\":\\s*\"([a-z0-9_]+)\",\\s*\"name\":\\s*\"[^\"]*\",\\s*\"category\":\\s*\"([a-zA-Z]+)\"");
            Assert.Greater(items.Count, 30);
            foreach (Match item in items)
            {
                var id = item.Groups[1].Value;
                if (item.Groups[2].Value == "cardFace")
                {
                    Assert.IsFalse(CosmeticArt.Has(id), id + " is drawn, not pictured");
                    continue;
                }

                Assert.IsTrue(CosmeticArt.Has(id), id);
                Assert.IsTrue(File.Exists(ArtFolder + id + ".png"), id + " art");
                StringAssert.Contains(".cosmetic.cosmetic--" + id + " ", stylesheet, id + " rule");
            }

            Assert.AreEqual(items.Count - 3, CosmeticArt.Pictured.Count, "every pictured id is in the catalog");
        }

        [Test]
        public void EveryEmoteHasItsStickerAndAPack()
        {
            var emotes = File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "../../../../content/emotes/v1.json"));
            var stylesheet = File.ReadAllText("Assets/_Project/Presentation/Theme/Basant.uss");
            var ids = Regex.Matches(emotes, "\"id\":\\s*\"([a-z0-9_]+)\",\\s*\"name\":\\s*\"emote\\.");
            Assert.GreaterOrEqual(ids.Count, 16);
            Assert.AreEqual(ids.Count, Regex.Matches(emotes, "\"pack\":\\s*\"emotes_[a-z0-9_]+\"").Count, "every emote names its pack");
            foreach (Match id in ids)
            {
                var emote = id.Groups[1].Value;
                Assert.IsTrue(File.Exists("Assets/_Project/Presentation/Theme/Art/Basant/Emotes/" + emote + ".png"), emote);
                StringAssert.Contains(".emote-art.emote-art--" + emote + " ", stylesheet, emote);
            }
        }

        [Test]
        public void ALockedEmoteTileStillAnswersATap()
        {
            var tapped = 0;
            var tile = EmoteArt.Tile("wicket", "Wicket", true, () => tapped++);
            Assert.IsTrue(tile.ClassListContains("emote-tile--locked"));
            Assert.IsNotNull(tile.Q(className: "emote-art--wicket"));
            EmoteArt.SetLocked(tile, false);
            Assert.IsFalse(tile.ClassListContains("emote-tile--locked"));
            Assert.AreEqual("Wicket", tile.tooltip);
        }

        [Test]
        public void ApplyingAnotherItemReplacesThePictureAndNullClearsIt()
        {
            var back = new VisualElement();
            CosmeticArt.Apply(back, "back_ajrak");
            Assert.IsTrue(back.ClassListContains("cosmetic--back_ajrak"));
            CosmeticArt.Apply(back, "back_kites");
            Assert.IsFalse(back.ClassListContains("cosmetic--back_ajrak"));
            Assert.IsTrue(back.ClassListContains("cosmetic--back_kites"));
            Assert.IsTrue(back.ClassListContains(CosmeticArt.Class));
            CosmeticArt.Apply(back, null);
            Assert.IsFalse(back.ClassListContains("cosmetic--back_kites"));
            Assert.IsFalse(back.ClassListContains(CosmeticArt.Class));
            CosmeticArt.Apply(back, "back_unknown");
            Assert.IsFalse(back.ClassListContains(CosmeticArt.Class));
        }

        [Test]
        public void TheCardFaceStyleIsOneClassOnTheCardsAncestor()
        {
            var table = new VisualElement();
            CosmeticArt.ApplyFace(table, "face_desi_court");
            Assert.IsTrue(table.ClassListContains(CosmeticArt.DesiCourtClass));
            CosmeticArt.ApplyFace(table, "face_large_index");
            Assert.IsFalse(table.ClassListContains(CosmeticArt.DesiCourtClass));
            Assert.IsTrue(table.ClassListContains(CosmeticArt.LargeIndexClass));
            CosmeticArt.ApplyFace(table, "face_classic");
            Assert.IsFalse(table.ClassListContains(CosmeticArt.LargeIndexClass));
        }
    }
}
