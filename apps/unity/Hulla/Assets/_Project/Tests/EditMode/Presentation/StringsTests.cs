using Hulla.Presentation.Text;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    /// <summary>The string tables (GDD 26.1, ADR-0023): keys, arguments, plurals, numbers and fallbacks.</summary>
    public sealed class StringsTests
    {
        [TearDown]
        public void TearDown() => Strings.Use(Strings.Fallback);

        [Test]
        public void PlainKeysReadTheirEnglish()
        {
            Assert.AreEqual("Back", UiText.Back);
            Assert.AreEqual("Back", Strings.Get("ui.back"));
            Assert.AreEqual("en", Strings.Locale);
        }

        [Test]
        public void PluralsFollowTheCount()
        {
            Assert.AreEqual("Play a round", AchievementText.Goal("rounds", 1));
            Assert.AreEqual("Play 10 rounds", AchievementText.Goal("rounds", 10));
            Assert.AreEqual("Escape first in a round", UiText.MissionText("escaped_first", 1));
            Assert.AreEqual("Win 3 series", UiText.MissionText("series_won", 3));
        }

        [Test]
        public void NumbersAreGroupedForTheLocale()
        {
            Assert.AreEqual("12,500 coins", Strings.Format(UiText.CoinsOnly, 12500));
        }

        [Test]
        public void ArgumentsFillTheirPlaces()
        {
            StringAssert.Contains("Ayesha", Strings.Format(UiText.JoinFriendTable, "Ayesha", 2));
        }

        [Test]
        public void UnknownErrorsAndKeysFallBack()
        {
            Assert.AreEqual("That table is full.", UiText.Error("table.full"));
            Assert.AreEqual("Something went wrong (made.up)", UiText.Error("made.up"));
            Assert.AreEqual("no.such.key", Strings.Get("no.such.key"));
            Assert.AreEqual("Truck art", ItemText.Name("table_truck_art"));
            Assert.AreEqual("Brand new", ItemText.Name("back_brand_new"), "an id the tables don't name reads as words");
        }

        [Test]
        public void AMissingLocaleKeepsEnglish()
        {
            Assert.IsFalse(Strings.Use("xx"));
            Assert.AreEqual("Back", UiText.Back);
            Assert.IsFalse(Strings.RightToLeft);
        }

        [Test]
        public void EveryUiPropertyHasAString()
        {
            foreach (var property in typeof(UiText).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (property.PropertyType != typeof(string))
                {
                    continue;
                }

                var text = (string)property.GetValue(null);
                Assert.IsFalse(text.StartsWith("ui."), property.Name + " has no string in content/strings/en.json");
                Assert.IsNotEmpty(text, property.Name);
            }
        }
    }
}
