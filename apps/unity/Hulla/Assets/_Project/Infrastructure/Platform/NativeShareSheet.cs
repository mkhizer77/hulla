using System.IO;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using Hulla.Application.Services;
using UnityEngine;

namespace Hulla.Infrastructure.Platform
{
    /// <summary>
    /// Android's ACTION_SEND chooser and iOS's activity sheet (Plugins/iOS/HullaShare.mm); elsewhere (the
    /// editor and desktop builds) the clipboard.
    /// </summary>
    public sealed class NativeShareSheet : IShareSheet
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern bool HullaShareText(string text);

        [DllImport("__Internal")]
        private static extern bool HullaShareImage(string path, string text);
#endif

        public bool Share(string title, string text)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (HullaShareText(text))
            {
                return true;
            }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent");
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND")).Dispose();
                intent.Call<AndroidJavaObject>("setType", "text/plain").Dispose();
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text).Dispose();
                StartChooser(intentClass, intent, title);
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

        public bool ShareImage(string title, string text, byte[] png, string fileName)
        {
            // The card goes to the cache's share folder, which HullaShareProvider serves read-only.
            var folder = Path.Combine(UnityEngine.Application.temporaryCachePath, "share");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, png);
#if UNITY_IOS && !UNITY_EDITOR
            if (HullaShareImage(path, text))
            {
                return true;
            }
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                var authority = activity.Call<string>("getPackageName") + ".share";
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "content://" + authority + "/" + fileName);
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent");
                intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND")).Dispose();
                intent.Call<AndroidJavaObject>("setType", "image/png").Dispose();
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_STREAM"), uri).Dispose();
                intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text).Dispose();
                using var clipClass = new AndroidJavaClass("android.content.ClipData");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var clip = clipClass.CallStatic<AndroidJavaObject>("newUri", resolver, title, uri);
                intent.Call("setClipData", clip);
                intent.Call<AndroidJavaObject>("addFlags", intentClass.GetStatic<int>("FLAG_GRANT_READ_URI_PERMISSION")).Dispose();
                StartChooser(intentClass, intent, title);
                return true;
            }
            catch (AndroidJavaException error)
            {
                Debug.LogWarning($"share sheet: {error.Message}");
            }
#else
            Debug.Log($"share card written to {path}");
#endif
            GUIUtility.systemCopyBuffer = text;
            return false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void StartChooser(AndroidJavaClass intentClass, AndroidJavaObject intent, string title)
        {
            using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, title);
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            activity.Call("startActivity", chooser);
        }
#endif
    }
}
