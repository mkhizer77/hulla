using Hulla.Presentation.Text;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class ItemTextTests
    {
        [TestCase("back_truck_art", "Truck art")]
        [TestCase("table_lahore_rooftop", "Lahore rooftop")]
        [TestCase("emotes_shaadi", "Shaadi")]
        [TestCase("mystery", "Mystery")]
        public void ItemIdsReadAsNames(string id, string expected) => Assert.AreEqual(expected, ItemText.Name(id));
    }
}
