using UnityEngine;

namespace Hulla.Presentation.TableKit
{
    /// <summary>The table's haptic events (GDD 16.4).</summary>
    public enum Haptic
    {
        /// <summary>Turn start: a light tick.</summary>
        TurnStart,

        /// <summary>A card played: soft.</summary>
        CardPlay,

        /// <summary>You gave a thulla: medium.</summary>
        ThullaGiven,

        /// <summary>You picked up: heavy.</summary>
        ThullaReceived,

        /// <summary>You escaped: a success pattern.</summary>
        Escape,

        /// <summary>You are the Bhabhi: a failure pattern.</summary>
        Bhabhi,
    }

    /// <summary>
    /// Plays the table's haptics (GDD 16.4). Android gets amplitude and patterns through the Vibrator;
    /// iOS has only the system buzz without a native plugin, so it plays the heavy events alone until
    /// the shim lands. Nothing in the editor. Callers check the Haptics setting first.
    /// </summary>
    public static class Haptics
    {
        public static void Play(Haptic kind)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PlayAndroid(kind);
#elif UNITY_IOS && !UNITY_EDITOR
            if (kind == Haptic.ThullaReceived || kind == Haptic.Bhabhi)
            {
                Handheld.Vibrate();
            }
#endif
        }

        /// <summary>Duration in ms and amplitude 1–255 for one-shot events; a timing pattern for the two-beat ones.</summary>
        public static (long[] Timings, int[] Amplitudes) Pattern(Haptic kind)
        {
            switch (kind)
            {
                case Haptic.TurnStart: return (new long[] { 0, 18 }, new[] { 0, 90 });
                case Haptic.CardPlay: return (new long[] { 0, 10 }, new[] { 0, 60 });
                case Haptic.ThullaGiven: return (new long[] { 0, 30, 60, 30 }, new[] { 0, 160, 0, 160 });
                case Haptic.ThullaReceived: return (new long[] { 0, 60 }, new[] { 0, 255 });
                case Haptic.Escape: return (new long[] { 0, 20, 80, 20 }, new[] { 0, 120, 0, 120 });
                default: return (new long[] { 0, 220 }, new[] { 0, 140 });
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void PlayAndroid(Haptic kind)
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            if (vibrator == null)
            {
                Handheld.Vibrate();
                return;
            }

            var (timings, amplitudes) = Pattern(kind);
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            if (version.GetStatic<int>("SDK_INT") >= 26)
            {
                using var effects = new AndroidJavaClass("android.os.VibrationEffect");
                using var effect = effects.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1);
                vibrator.Call("vibrate", effect);
            }
            else
            {
                vibrator.Call("vibrate", timings, -1);
            }
        }
#endif
    }
}
