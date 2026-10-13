using Hulla.Application.Settings;

namespace Hulla.Application.Social
{
    /// <summary>
    /// When to ask for notification permission (GDD 10.10). Never on first launch: back home after
    /// the player has finished a round, so the value of invites is clear. A "Not now"
    /// is asked again a week later, and twice at most, since the system dialog itself only shows twice
    /// on Android. Interim choice, OQ-035.
    /// </summary>
    public sealed class PushPrompt
    {
        public const string RoundsKey = "hulla.push.rounds";
        public const string AskedKey = "hulla.push.asked";
        public const string AskedDayKey = "hulla.push.askedDay";

        private const int MaxAsks = 2;
        private const int DaysBetweenAsks = 7;

        private readonly ISettingsStore _store;

        public PushPrompt(ISettingsStore store)
        {
            _store = store;
        }

        public void RoundFinished()
        {
            _store.SetInt(RoundsKey, _store.GetInt(RoundsKey, 0) + 1);
            _store.Save();
        }

        /// <param name="day">Days since the Unix epoch, local time.</param>
        public bool ShouldAsk(bool deviceCanAsk, int day)
        {
            if (!deviceCanAsk || _store.GetInt(RoundsKey, 0) == 0)
            {
                return false;
            }

            var asked = _store.GetInt(AskedKey, 0);
            return asked == 0 || (asked < MaxAsks && day - _store.GetInt(AskedDayKey, 0) >= DaysBetweenAsks);
        }

        public void Asked(int day)
        {
            _store.SetInt(AskedKey, _store.GetInt(AskedKey, 0) + 1);
            _store.SetInt(AskedDayKey, day);
            _store.Save();
        }
    }
}
