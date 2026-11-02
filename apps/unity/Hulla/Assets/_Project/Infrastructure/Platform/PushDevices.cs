using System;
using System.Collections.Generic;
using Hulla.Application.Social;
using UnityEngine;

namespace Hulla.Infrastructure.Platform
{
    /// <summary>The device for the current platform.</summary>
    public static class PushDevices
    {
        private const string InstallKey = "hulla.push_install";

        /// <summary>"android" or "ios" for push_register; null where the app has no push token yet.</summary>
#if UNITY_ANDROID
        public const string? Platform = "android";
#else
        public const string? Platform = null;
#endif

        /// <summary>
        /// A random id for this install's push token, apart from the sign-in device id, which is a
        /// credential and never leaves for anything else.
        /// </summary>
        public static string InstallId()
        {
            var key = Hulla.Infrastructure.Settings.DeviceProfile.Key(InstallKey);
            var stored = PlayerPrefs.GetString(key, "");
            if (stored.Length > 0)
            {
                return stored;
            }

            var created = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(key, created);
            PlayerPrefs.Save();
            return created;
        }

        /// <param name="channelName">The Android notification channel's name in settings.</param>
        public static IPushDevice Create(string channelName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidPushDevice(channelName);
#else
            return new NoPushDevice();
#endif
        }
    }

    /// <summary>The editor and iOS until it has its notification setup.</summary>
    public sealed class NoPushDevice : IPushDevice
    {
        public bool CanAsk => false;

        public void Ask(Action<bool> answered) => answered(false);

        public IReadOnlyDictionary<string, string>? TakeOpenedData() => null;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Android: creates the "hulla" channel the server's messages name, asks for POST_NOTIFICATIONS
    /// on Android 13 and later, and reads the data FCM puts on the launch intent when a notification
    /// is tapped.
    /// </summary>
    public sealed class AndroidPushDevice : IPushDevice
    {
        private const string Permission = "android.permission.POST_NOTIFICATIONS";
        private const string ChannelId = "hulla";
        private static readonly string[] Keys = { "route", "type", "code", "matchId", "fromUserId", "seasonId" };

        private readonly int _sdk;

        public AndroidPushDevice(string channelName)
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            _sdk = version.GetStatic<int>("SDK_INT");
            try
            {
                CreateChannel(channelName);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"push channel: {error.Message}");
            }
        }

        public bool CanAsk => _sdk >= 33 && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(Permission);

        public void Ask(Action<bool> answered)
        {
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => answered(true);
            callbacks.PermissionDenied += _ => answered(false);
            UnityEngine.Android.Permission.RequestUserPermission(Permission, callbacks);
        }

        public IReadOnlyDictionary<string, string>? TakeOpenedData()
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var intent = activity.Call<AndroidJavaObject>("getIntent");
                if (intent == null || intent.Call<string>("getStringExtra", "route") == null)
                {
                    return null;
                }

                var data = new Dictionary<string, string>();
                foreach (var key in Keys)
                {
                    var value = intent.Call<string>("getStringExtra", key);
                    if (value != null)
                    {
                        data[key] = value;
                    }

                    intent.Call("removeExtra", key);
                }

                return data;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"push tap: {error.Message}");
                return null;
            }
        }

        private void CreateChannel(string channelName)
        {
            if (_sdk < 26)
            {
                return;
            }

            const int importanceHigh = 4;
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var manager = activity.Call<AndroidJavaObject>("getSystemService", "notification");
            using var channel = new AndroidJavaObject("android.app.NotificationChannel", ChannelId, channelName, importanceHigh);
            manager.Call("createNotificationChannel", channel);
        }
    }
#endif
}
