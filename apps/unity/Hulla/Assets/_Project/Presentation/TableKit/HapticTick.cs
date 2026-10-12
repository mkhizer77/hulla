using UnityEngine;

namespace Hulla.Presentation.TableKit
{
    /// <summary>The soft tick at the start of the player's turn (GDD 14.5); nothing in the editor.</summary>
    public static class HapticTick
    {
        public static void Play()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            if (vibrator != null)
            {
                vibrator.Call("vibrate", 20L);
            }
            else
            {
                Handheld.Vibrate();
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }
    }
}
