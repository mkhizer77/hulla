using Hulla.Domain;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class ResultTests
    {
        [Test]
        public void OkCarriesTheValue()
        {
            var result = Result<int>.Ok(7);
            Assert.IsTrue(result.IsOk);
            Assert.AreEqual(7, result.Value);
        }

        [Test]
        public void FailCarriesTheCodeAndRefusesTheValue()
        {
            var result = Result<int>.Fail("table.not_your_turn");
            Assert.IsFalse(result.IsOk);
            Assert.AreEqual("table.not_your_turn", result.Error.Code);
            Assert.Throws<System.InvalidOperationException>(() => _ = result.Value);
        }
    }
}
