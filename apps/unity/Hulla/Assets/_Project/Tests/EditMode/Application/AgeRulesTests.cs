using System;
using Hulla.Application.Age;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    /// <summary>The first-launch age question (GDD 24.6, #87), counted as the server counts it.</summary>
    public sealed class AgeRulesTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void AYearCountsAsTheYoungestAgeItAllows()
        {
            Assert.AreEqual(15, AgeRules.YoungestAge(2010, Now));
            Assert.IsTrue(AgeRules.TooYoung(2013, Now), "could still be 12");
            Assert.IsFalse(AgeRules.TooYoung(2012, Now));
        }

        [Test]
        public void OnlyRealYearsAreAccepted()
        {
            Assert.IsTrue(AgeRules.Valid(1990, Now));
            Assert.IsFalse(AgeRules.Valid(1899, Now));
            Assert.IsFalse(AgeRules.Valid(2027, Now));
        }

        [Test]
        public void NoYearCountsAsUnderEighteen()
        {
            Assert.IsTrue(AgeRules.UnderEighteen(null, Now));
            Assert.IsTrue(AgeRules.UnderEighteen(2008, Now));
            Assert.IsFalse(AgeRules.UnderEighteen(2007, Now));
        }
    }
}
