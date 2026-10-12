using System;

namespace Hulla.Application.Settings
{
    /// <summary>
    /// The player's settings (GDD 14.12, 27) that exist so far. Every setter saves and raises
    /// <see cref="Changed"/> so screens apply it live. Audio levels, notifications, privacy and
    /// account controls join with their features (#73, #46, #48, #39).
    /// </summary>
    public sealed class SettingsModel
    {
        private const string Prefix = "hulla.settings.";
        private readonly ISettingsStore _store;

        public SettingsModel(ISettingsStore store)
        {
            _store = store;
        }

        public event Action? Changed;

        public bool OneTapPlay
        {
            get => Get("one_tap", 0) == 1;
            set => Set("one_tap", value ? 1 : 0);
        }

        public bool ReduceMotion
        {
            get => Get("reduce_motion", 0) == 1;
            set => Set("reduce_motion", value ? 1 : 0);
        }

        public bool Haptics
        {
            get => Get("haptics", 1) == 1;
            set => Set("haptics", value ? 1 : 0);
        }

        public bool FourColourDeck
        {
            get => Get("four_colour", 0) == 1;
            set => Set("four_colour", value ? 1 : 0);
        }

        public bool HighContrast
        {
            get => Get("high_contrast", 0) == 1;
            set => Set("high_contrast", value ? 1 : 0);
        }

        public bool LargeCards
        {
            get => Get("large_cards", 0) == 1;
            set => Set("large_cards", value ? 1 : 0);
        }

        public bool LeftHanded
        {
            get => Get("left_handed", 0) == 1;
            set => Set("left_handed", value ? 1 : 0);
        }

        public bool ShowRulesRecap
        {
            get => Get("rules_recap", 1) == 1;
            set => Set("rules_recap", value ? 1 : 0);
        }

        public TextScale TextScale
        {
            get => Get("text_scale", (int)TextScale.Normal) switch
            {
                125 => TextScale.Large,
                150 => TextScale.Larger,
                200 => TextScale.Largest,
                _ => TextScale.Normal,
            };
            set => Set("text_scale", (int)value);
        }

        public HandSort HandSort
        {
            get => Get("hand_sort", 0) == 1 ? HandSort.RankThenSuit : HandSort.SuitThenRank;
            set => Set("hand_sort", (int)value);
        }

        private int Get(string key, int fallback) => _store.GetInt(Prefix + key, fallback);

        private void Set(string key, int value)
        {
            if (Get(key, int.MinValue) == value)
            {
                return;
            }

            _store.SetInt(Prefix + key, value);
            _store.Save();
            Changed?.Invoke();
        }
    }
}
