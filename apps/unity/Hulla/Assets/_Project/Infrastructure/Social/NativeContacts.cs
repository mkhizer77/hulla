using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Social;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Hulla.Infrastructure.Social
{
    /// <summary>
    /// The address book's phone numbers, after the system asks the player: iOS through the Contacts
    /// framework (Plugins/iOS/HullaSocial.mm), Android through ContactsContract
    /// (Plugins/Android/HullaContacts.java) once READ_CONTACTS is granted. Nothing is read on desktop.
    /// </summary>
    public sealed class NativeContacts : IContactsSource
    {
        private static volatile bool _done;
        private static volatile string? _json;
        private static volatile string? _error;

#if UNITY_IOS && !UNITY_EDITOR
        public bool Supported => true;

        private delegate void NativeReply(string? value, string? error);

        [DllImport("__Internal")]
        private static extern void HullaContactsRead(NativeReply callback);

        [AOT.MonoPInvokeCallback(typeof(NativeReply))]
        private static void OnReply(string? value, string? error) => Finish(value, error);

        public async Awaitable<Result<IReadOnlyList<string>>> PhoneNumbersAsync(CancellationToken cancellation)
        {
            Begin();
            HullaContactsRead(OnReply);
            return await WaitAsync(cancellation);
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        private const string Permission = "android.permission.READ_CONTACTS";

        public bool Supported => true;

        private sealed class Reply : AndroidJavaProxy
        {
            public Reply()
                : base("gg.hulla.social.HullaContacts$Callback")
            {
            }

            public void onNumbers(string json) => Finish(json, null);

            public void onError(string code) => Finish(null, code);
        }

        public async Awaitable<Result<IReadOnlyList<string>>> PhoneNumbersAsync(CancellationToken cancellation)
        {
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(Permission))
            {
                Begin();
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => Finish("granted", null);
                callbacks.PermissionDenied += _ => Finish(null, "contacts.denied");
                UnityEngine.Android.Permission.RequestUserPermission(Permission, callbacks);
                var asked = await WaitAsync(cancellation, raw: true);
                if (!asked.IsOk)
                {
                    return asked;
                }
            }

            Begin();
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var contacts = new AndroidJavaClass("gg.hulla.social.HullaContacts"))
            {
                contacts.CallStatic("read", activity, new Reply());
            }

            return await WaitAsync(cancellation);
        }
#else
        public bool Supported => false;

        public Awaitable<Result<IReadOnlyList<string>>> PhoneNumbersAsync(CancellationToken cancellation)
        {
            var done = new AwaitableCompletionSource<Result<IReadOnlyList<string>>>();
            done.SetResult(Result<IReadOnlyList<string>>.Fail("contacts.unavailable"));
            return done.Awaitable;
        }
#endif

        private static void Begin()
        {
            _json = null;
            _error = null;
            _done = false;
        }

        private static void Finish(string? json, string? error)
        {
            _json = json;
            _error = error ?? (json == null ? "contacts.unavailable" : null);
            _done = true;
        }

        /// <summary>Waits for the native reply; the numbers arrive as a JSON array of strings.</summary>
        private static async Awaitable<Result<IReadOnlyList<string>>> WaitAsync(CancellationToken cancellation, bool raw = false)
        {
            while (!_done)
            {
                await Awaitable.NextFrameAsync(cancellation);
            }

            if (_error != null || _json == null)
            {
                return Result<IReadOnlyList<string>>.Fail(_error ?? "contacts.unavailable");
            }

            if (raw)
            {
                return Result<IReadOnlyList<string>>.Ok(new string[0]);
            }

            return Result<IReadOnlyList<string>>.Ok(Parse(_json));
        }

        public static IReadOnlyList<string> Parse(string json)
        {
            var list = new List<string>();
            foreach (var item in JArray.Parse(json))
            {
                if (item.Type == JTokenType.String)
                {
                    list.Add((string)item!);
                }
            }

            return list;
        }
    }
}
