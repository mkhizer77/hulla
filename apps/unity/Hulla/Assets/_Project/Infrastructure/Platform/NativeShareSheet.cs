using Hulla.Application.Services;
using UnityEngine;

namespace Hulla.Infrastructure.Platform
{
    /// <summary>Android's ACTION_SEND chooser; elsewhere (editor, iOS until its plugin lands) the clipboard.</summary>
    public sealed class NativeShareSheet : IShareSheet
    {
        public bool Share(string title, string text)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent");
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND")).Dispose();
                intent.Call<AndroidJavaObject>("setType", "text/plain").Dispose();
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text).Dispose();
                using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, title);
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("startActivity", chooser);
                return true;
            }
            catch (AndroidJavaException error)
            {
                Debug.LogWarning($"share sheet: {error.Message}");
            }
#endif
            GUIUtility.systemCopyBuffer = text;
            return false;
        }
    }
}
