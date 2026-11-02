using System;
using System.Collections.Generic;
using Hulla.Protocol;
using UnityEngine;
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
using Firebase;
using Firebase.Analytics;
using Firebase.Crashlytics;
#endif
#if !UNITY_EDITOR && UNITY_ANDROID
using Firebase.Messaging;
#endif

namespace Hulla.Infrastructure.Platform
{
    /// <summary>
    /// Firebase on the phone (#71, #72): Analytics, Crashlytics and, on Android, the FCM token for
    /// push_register. Nothing is collected before the player says yes to usage data: the manifest and
    /// Info.plist start with collection off (FirebaseConfigBuild) and only <see cref="SetConsent"/> turns
    /// it on. Ad storage and personalisation stay denied. The editor and desktop builds skip Firebase.
    /// </summary>
    public sealed class FirebaseServices
    {
        private bool _ready;
        private bool _consented;

        /// <summary>The FCM token, on the main thread, whenever Firebase issues or rotates it.</summary>
#pragma warning disable CS0067 // raised only in Android players
        public event Action<string>? TokenReceived;
#pragma warning restore CS0067

        public bool Ready => _ready;

        public void Start()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            StartAsync();
#endif
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
                // iOS joins when the paid Apple account brings APNs; asking for the token there would
                // show the system's notification dialog at launch.
                FirebaseMessaging.TokenReceived += OnTokenReceived;
#endif
            }
            catch (Exception error)
            {
                Debug.LogWarning($"firebase: {error.Message}");
            }
        }
#endif

#if !UNITY_EDITOR && UNITY_ANDROID
        private async void OnTokenReceived(object sender, TokenReceivedEventArgs args)
        {
            await Awaitable.MainThreadAsync();
            TokenReceived?.Invoke(args.Token);
        }
#endif
    }
}
