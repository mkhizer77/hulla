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
#elif UNITY_IOS
        public const string? Platform = "ios";
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
        /// <param name="firebase">Where iOS gets its permission dialog and tapped notifications.</param>
        public static IPushDevice Create(string channelName, FirebaseServices firebase)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidPushDevice(channelName);
#elif UNITY_IOS && !UNITY_EDITOR
            return new IosPushDevice(firebase);
#else
            return new NoPushDevice();
#endif
        }
    }

    /// <summary>The editor and desktop builds.</summary>
    public sealed class NoPushDevice : IPushDevice
    {
        public event Action Opened
        {
            add { }
            remove { }
        }

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

        public event Action Opened
        {
            add { }
            remove { }
        }

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

#if UNITY_IOS && !UNITY_EDITOR
    /// <summary>
    /// iOS: Firebase Messaging shows the system's notification dialog and brings the data of a tapped
    /// notification. Messaging stays off until the player has said yes to the app's own question, so
    /// nothing asks at launch; after that it starts with the app and the token follows.
    /// </summary>
    public sealed class IosPushDevice : IPushDevice
    {
        private const string AskedKey = "hulla.push_asked";

        private readonly FirebaseServices _firebase;
        private IReadOnlyDictionary<string, string>? _opened;

        public event Action? Opened;

        public IosPushDevice(FirebaseServices firebase)
        {
            _firebase = firebase;
            _firebase.NotificationOpened += OnOpened;
            if (PlayerPrefs.GetInt(AskedKey, 0) == 1)
            {
                _firebase.StartMessaging(askPermission: false);
            }
        }

        public bool CanAsk => PlayerPrefs.GetInt(AskedKey, 0) == 0;

        public void Ask(Action<bool> answered)
        {
            PlayerPrefs.SetInt(AskedKey, 1);
            PlayerPrefs.Save();
            _firebase.StartMessaging(askPermission: true);
            // iOS does not tell Firebase the answer; a refusal only means notifications are not shown.
            answered(true);
        }

        public IReadOnlyDictionary<string, string>? TakeOpenedData()
        {
            var data = _opened;
            _opened = null;
            return data;
        }

        private void OnOpened(IReadOnlyDictionary<string, string> data)
        {
            _opened = data;
            Opened?.Invoke();
        }
    }
#endif
}
