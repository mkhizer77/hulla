using Hulla.Presentation.Navigation;
using NUnit.Framework;

namespace Hulla.Presentation.Tests
{
    public sealed class KeyboardAvoidanceTests
    {
        [Test]
        public void AFieldAboveTheKeyboardStaysPut()
        {
            Assert.AreEqual(0f, KeyboardAvoidance.Shift(fieldBottom: 300f, panelHeight: 1000f, keyboardHeight: 400f));
        }

        [Test]
        public void AFieldUnderTheKeyboardMovesUpJustEnough()
        {
            // Visible area ends at 1000 - 400 - margin.
            var shift = KeyboardAvoidance.Shift(fieldBottom: 900f, panelHeight: 1000f, keyboardHeight: 400f);
            Assert.AreEqual(900f - (600f - KeyboardAvoidance.Margin), shift, 0.001f);
        }

        [Test]
        public void NoKeyboardNoShift()
        {
            Assert.AreEqual(0f, KeyboardAvoidance.Shift(fieldBottom: 990f, panelHeight: 1000f, keyboardHeight: 0f));
        }
    }
}
