using System;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The turn timer's three phases (GDD 14.5, design 7.3): calm, amber with a pulse in the last 5 s,
    /// red with a faster pulse in the last 2 s. Pure, so the ring and the ticks can be tested.
    /// </summary>
    public static class TimerPhase
    {
        public const float WarnSeconds = 5f;
        public const float CriticalSeconds = 2f;

        public enum Phase
        {
            Calm,
            Warn,
            Critical,
        }

        public static Phase For(float secondsLeft) =>
            secondsLeft <= CriticalSeconds ? Phase.Critical : secondsLeft <= WarnSeconds ? Phase.Warn : Phase.Calm;

        /// <summary>The ring's scale: 1 while calm, a soft pulse (up to 1.06) when amber, a quicker one (1.1) when red.</summary>
        public static float Pulse(float secondsLeft, float elapsedSeconds, bool reduceMotion)
        {
            if (reduceMotion)
            {
                return 1f;
            }

            switch (For(secondsLeft))
            {
                case Phase.Warn:
                    return 1f + 0.06f * Wave(elapsedSeconds, 1f);
                case Phase.Critical:
                    return 1f + 0.1f * Wave(elapsedSeconds, 2.5f);
                default:
                    return 1f;
            }
        }

        /// <summary>The seconds at which a soft tick plays, once each (GDD 16.2): 5 and 2.</summary>
        public static bool TickAt(long shownSeconds) => shownSeconds == 5 || shownSeconds == 2;

        private static float Wave(float t, float hertz) => (float)(0.5 + 0.5 * Math.Sin(t * hertz * 2 * Math.PI));
    }
}
