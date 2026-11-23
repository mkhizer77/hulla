using System;
using System.Collections.Generic;
using Hulla.Protocol;
using UnityEngine;
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Firebase.Messaging;
#endif

namespace Hulla.Infrastructure.Platform
{
    /// <summary>
    /// Firebase on the phone (#71, #72): Analytics, Crashlytics and the FCM token for push_register
    /// (on iOS only after <see cref="StartMessaging"/>). Nothing is collected before the player says yes to usage data: the manifest and
    /// Info.plist start with collection off (FirebaseConfigBuild) and only <see cref="SetConsent"/> turns
    /// it on. Ad storage and personalisation stay denied. The editor and desktop builds skip Firebase.
    /// </summary>
    public sealed class FirebaseServices
    {
        private bool _ready;
        private bool _consented;
#pragma warning disable CS0067, CS0169, CS0414 // used only in phone players
        private bool _messagingWanted;
        private bool _permissionWanted;
        private bool _messaging;

        /// <summary>The FCM token, on the main thread, whenever Firebase issues or rotates it.</summary>
        public event Action<string>? TokenReceived;

        /// <summary>The data of a notification the player tapped, on the main thread (iOS).</summary>
        public event Action<IReadOnlyDictionary<string, string>>? NotificationOpened;
#pragma warning restore CS0067, CS0169, CS0414

        public bool Ready => _ready;

        public void Start()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            StartAsync();
#endif
        }

        /// <summary>
        /// Starts Firebase Messaging, which on iOS registers with APNs, and with
        /// <paramref name="askPermission"/> shows the system's notification dialog. Android starts it
        /// with Firebase and asks through its own permission.
        /// </summary>
        public void StartMessaging(bool askPermission)
        {
            _messagingWanted = true;
            _permissionWanted |= askPermission;
            if (_ready)
            {
                ApplyMessaging();
            }
        }

        public void SetConsent(bool consented)
        {
            _consented = consented;
            if (_ready)
            {
                Apply();
            }
        }

        /// <summary>One event as Firebase takes it: strings cut to 100 characters, flags as 0 or 1.</summary>
        public void Log(AnalyticsEvent analyticsEvent)
        {
            if (!_ready || !_consented)
            {
                return;
            }

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            var parameters = new List<Parameter>();
            foreach (var pair in analyticsEvent.Parameters)
            {
                switch (pair.Value)
                {
                    case string text:
                        parameters.Add(new Parameter(pair.Key, text.Length > 100 ? text.Substring(0, 100) : text));
                        break;
                    case bool flag:
                        parameters.Add(new Parameter(pair.Key, flag ? 1L : 0L));
                        break;
                    case int or long:
                        parameters.Add(new Parameter(pair.Key, Convert.ToInt64(pair.Value)));
                        break;
                    case float or double:
                        parameters.Add(new Parameter(pair.Key, Convert.ToDouble(pair.Value)));
                        break;
                }
            }

            FirebaseAnalytics.LogEvent(analyticsEvent.Name, parameters.ToArray());
#endif
        }

        private void Apply()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            FirebaseAnalytics.SetConsent(new Dictionary<ConsentType, ConsentStatus>
            {
                [ConsentType.AnalyticsStorage] = _consented ? ConsentStatus.Granted : ConsentStatus.Denied,
                [ConsentType.AdStorage] = ConsentStatus.Denied,
                [ConsentType.AdUserData] = ConsentStatus.Denied,
                [ConsentType.AdPersonalization] = ConsentStatus.Denied,
            });
            FirebaseAnalytics.SetAnalyticsCollectionEnabled(_consented);
            Crashlytics.IsCrashlyticsCollectionEnabled = _consented;
#endif
        }

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        private async void StartAsync()
        {
            try
            {
                var status = await FirebaseApp.CheckAndFixDependenciesAsync();
                await Awaitable.MainThreadAsync();
                if (status != DependencyStatus.Available)
                {
                    Debug.LogWarning($"firebase: {status}");
                    return;
                }

                _ready = true;
                Apply();
#if UNITY_ANDROID
                _messagingWanted = true;
#endif
                // iOS waits for StartMessaging: Messaging may show the notification dialog when it starts.
                ApplyMessaging();
            }
            catch (Exception error)
            {
                Debug.LogWarning($"firebase: {error.Message}");
            }
        }
#endif

        private void ApplyMessaging()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            if (!_messagingWanted)
            {
                return;
            }

            try
            {
                if (!_messaging)
                {
                    _messaging = true;
                    FirebaseMessaging.TokenReceived += OnTokenReceived;
#if UNITY_IOS
                    FirebaseMessaging.MessageReceived += OnMessageReceived;
#endif
                }

#if UNITY_IOS
                if (_permissionWanted)
                {
                    _permissionWanted = false;
                    _ = FirebaseMessaging.RequestPermissionAsync();
                }
#endif
            }
            catch (Exception error)
            {
                Debug.LogWarning($"firebase messaging: {error.Message}");
            }
#endif
        }

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        private async void OnTokenReceived(object sender, TokenReceivedEventArgs args)
        {
            await Awaitable.MainThreadAsync();
            TokenReceived?.Invoke(args.Token);
        }
#endif

#if !UNITY_EDITOR && UNITY_IOS
        private async void OnMessageReceived(object sender, MessageReceivedEventArgs args)
        {
            if (!args.Message.NotificationOpened || args.Message.Data == null)
            {
                return;
            }

            var data = new Dictionary<string, string>(args.Message.Data);
            await Awaitable.MainThreadAsync();
            NotificationOpened?.Invoke(data);
        }
#endif
    }
}
