using Hulla.Application.Table;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class BigThullaOfferTests
    {
        [Test]
        public void SmallPickUpsAreNotOffered()
        {
            var offer = new BigThullaOffer();
            Assert.IsFalse(offer.Consider(4));
            Assert.IsFalse(offer.Consider(9));
        }

        [Test]
        public void TheOfferIsMadeOncePerSession()
        {
            var offer = new BigThullaOffer();
            Assert.IsTrue(offer.Consider(12));
            Assert.IsFalse(offer.Consider(20));
        }

        [Test]
        public void ALaterRoundMustBeatTheNightsBest()
        {
            var offer = new BigThullaOffer();
            Assert.IsFalse(offer.Consider(8));
            Assert.IsFalse(offer.Consider(6));
            Assert.IsTrue(offer.Consider(11));
        }
    }
}
