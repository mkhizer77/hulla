using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Hulla.Infrastructure.Social
{
    /// <summary>
    /// The phone's sign-in for each provider (GDD 10.2, #38). iOS: Sign in with Apple and Google's OAuth in a
    /// browser sheet (Plugins/iOS/HullaSocial.mm), no SDK. Android: Google through Credential Manager
    /// (Plugins/Android/HullaGoogleSignIn.java). Both: Facebook Login through Meta's SDK. Desktop and editor
    /// builds offer none. One sign-in runs at a time; the native reply lands in a field the await reads.
    /// </summary>
    public sealed class NativeSocialSignIn : ISocialSignIn
    {
        private readonly SocialConfig _config;
        private static volatile bool _done;
        private static volatile string? _value;
        private static volatile string? _error;
        private static volatile string? _detail;

        public NativeSocialSignIn(SocialConfig config)
        {
            _config = config;
        }

        public bool Supports(SocialProvider provider)
        {
#if UNITY_IOS && !UNITY_EDITOR
            return provider switch
            {
                SocialProvider.Apple => _config.AppleSignIn,
                SocialProvider.Google => _config.GoogleIosClientId.Length > 0,
                _ => _config.FacebookAppId.Length > 0,
            };
#elif UNITY_ANDROID && !UNITY_EDITOR
            return provider switch
            {
                SocialProvider.Apple => false,
                SocialProvider.Google => _config.GoogleWebClientId.Length > 0,
                _ => _config.FacebookAppId.Length > 0,
            };
#else
            return false;
#endif
        }

        public async Awaitable<Result<string>> TokenAsync(SocialProvider provider, CancellationToken cancellation)
        {
            if (!Supports(provider))
            {
                return Result<string>.Fail("auth.provider_unavailable");
            }

            return provider switch
            {
                SocialProvider.Apple => await AppleAsync(cancellation),
                SocialProvider.Google => await GoogleAsync(cancellation),
                _ => await FacebookAsync(cancellation),
            };
        }

        private static void Begin()
        {
            _value = null;
            _error = null;
            _detail = null;
            _done = false;
        }

        private static void Finish(string? value, string? error)
        {
            _value = value;
            _error = error ?? (value == null ? "auth.provider_unavailable" : null);
            _done = true;
        }

        /// <summary>Waits a frame at a time for the native reply, which may arrive on another thread.</summary>
        private static async Awaitable<Result<string>> WaitAsync(CancellationToken cancellation)
        {
            while (!_done)
            {
                await Awaitable.NextFrameAsync(cancellation);
            }

            return _error == null && _value != null ? Result<string>.Ok(_value) : Result<string>.Fail(_error ?? "auth.provider_unavailable", _detail);
        }

        /// <summary>The profile, and the friends who also use Hulla for people you may know.</summary>
        private static readonly string[] FacebookPermissions = { "public_profile", "user_friends" };

        public async Awaitable<Result<IReadOnlyList<string>>> FacebookFriendIdsAsync(CancellationToken cancellation)
        {
            if (!Supports(SocialProvider.Facebook))
            {
                return Result<IReadOnlyList<string>>.Fail("auth.provider_unavailable");
            }

            if (!Facebook.Unity.FB.IsInitialized || !Facebook.Unity.FB.IsLoggedIn)
            {
                var signedIn = await FacebookAsync(cancellation);
                if (!signedIn.IsOk)
                {
                    return Result<IReadOnlyList<string>>.Fail(signedIn.Error.Code);
                }
            }

            // iOS Limited Login has no Graph token: the profile carries the friend ids itself.
            var profile = Facebook.Unity.FB.Mobile.CurrentProfile();
            if (profile?.FriendIDs != null && profile.FriendIDs.Length > 0)
            {
                return Result<IReadOnlyList<string>>.Ok(profile.FriendIDs);
            }

            if (Facebook.Unity.AccessToken.CurrentAccessToken == null)
            {
                return Result<IReadOnlyList<string>>.Ok(new string[0]);
            }

            Begin();
            Facebook.Unity.FB.API("/me/friends?fields=id&limit=500", Facebook.Unity.HttpMethod.GET, result => Finish(string.IsNullOrEmpty(result.Error) ? result.RawResult : null, null));
            var raw = await WaitAsync(cancellation);
            if (!raw.IsOk)
            {
                return Result<IReadOnlyList<string>>.Fail(raw.Error);
            }

            return Result<IReadOnlyList<string>>.Ok(FriendIdsFrom(raw.Value));
        }

        /// <summary>The ids in a Graph <c>/me/friends</c> answer.</summary>
        public static IReadOnlyList<string> FriendIdsFrom(string json)
        {
            var ids = new System.Collections.Generic.List<string>();
            try
            {
                foreach (var friend in JObject.Parse(json)["data"] as JArray ?? new JArray())
                {
                    if ((string?)friend["id"] is string id)
                    {
                        ids.Add(id);
                    }
                }
            }
            catch (Exception)
            {
                // A malformed answer lists nobody.
            }

            return ids;
        }

        private static async Awaitable<Result<string>> FacebookAsync(CancellationToken cancellation)
        {
            if (!Facebook.Unity.FB.IsInitialized)
            {
                Begin();
                Facebook.Unity.FB.Init(() => Finish(Facebook.Unity.FB.IsInitialized ? "ready" : null, null));
                var ready = await WaitAsync(cancellation);
                if (!ready.IsOk)
                {
                    return ready;
                }
            }

            Begin();
            Facebook.Unity.FacebookDelegate<Facebook.Unity.ILoginResult> done = result =>
            {
                if (result.Cancelled)
                {
                    Finish(null, "auth.cancelled");
                }
                else if (!string.IsNullOrEmpty(result.Error))
                {
                    // Facebook's own words go with the failure, for the log and development builds.
                    _detail = result.Error;
                    Debug.LogWarning("Facebook login failed: " + result.Error + " " + result.RawResult);
                    Finish(null, "auth.provider_unavailable");
                }
                else
                {
                    // iOS signs in with Limited Login unless tracking is allowed: its access token is not a Graph
                    // token, so the signed ID token (AuthenticationToken) goes to Nakama, which checks it against
                    // social.facebook_limited_login. Android gives a classic access token only.
                    Finish(result.AuthenticationToken?.TokenString ?? result.AccessToken?.TokenString, null);
                }
            };
#if UNITY_IOS && !UNITY_EDITOR
            // Limited Login asked for outright: its signed token and friend list need no tracking consent.
            Facebook.Unity.FB.Mobile.LoginWithTrackingPreference(Facebook.Unity.LoginTracking.LIMITED, FacebookPermissions, Guid.NewGuid().ToString("N"), done);
#else
            Facebook.Unity.FB.LogInWithReadPermissions(FacebookPermissions, done);
#endif
            return await WaitAsync(cancellation);
        }

#if UNITY_IOS && !UNITY_EDITOR
        private delegate void NativeReply(string? value, string? error);

        [DllImport("__Internal")]
        private static extern void HullaAppleSignInStart(NativeReply callback);

        [DllImport("__Internal")]
        private static extern void HullaWebAuthStart(string url, string scheme, NativeReply callback);

        [AOT.MonoPInvokeCallback(typeof(NativeReply))]
        private static void OnReply(string? value, string? error) => Finish(value, error);

        private static async Awaitable<Result<string>> AppleAsync(CancellationToken cancellation)
        {
            Begin();
            HullaAppleSignInStart(OnReply);
            return await WaitAsync(cancellation);
        }

        private async Awaitable<Result<string>> GoogleAsync(CancellationToken cancellation)
        {
            var clientId = _config.GoogleIosClientId;
            var verifier = GoogleOAuth.NewVerifier();
            var state = GoogleOAuth.NewVerifier().Substring(0, 24);
            Begin();
            HullaWebAuthStart(GoogleOAuth.SignInUrl(clientId, verifier, state), GoogleOAuth.RedirectScheme(clientId), OnReply);
            var redirect = await WaitAsync(cancellation);
            if (!redirect.IsOk)
            {
                return redirect;
            }

            var code = GoogleOAuth.CodeFrom(redirect.Value, state);
            if (code == null)
            {
                return Result<string>.Fail("auth.provider_unavailable");
            }

            return await ExchangeAsync(clientId, code, verifier, cancellation);
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        private sealed class GoogleReply : AndroidJavaProxy
        {
            public GoogleReply()
                : base("gg.hulla.social.HullaGoogleSignIn$Callback")
            {
            }

            public void onToken(string token) => Finish(token, null);

            public void onError(string code) => Finish(null, code);
        }

        private static Awaitable<Result<string>> AppleAsync(CancellationToken cancellation) =>
            Unavailable();

        private async Awaitable<Result<string>> GoogleAsync(CancellationToken cancellation)
        {
            Begin();
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var signIn = new AndroidJavaClass("gg.hulla.social.HullaGoogleSignIn"))
            {
                signIn.CallStatic("signIn", activity, _config.GoogleWebClientId, new GoogleReply());
            }

            return await WaitAsync(cancellation);
        }
#else
        private static Awaitable<Result<string>> AppleAsync(CancellationToken cancellation) => Unavailable();

        private Awaitable<Result<string>> GoogleAsync(CancellationToken cancellation) => Unavailable();
#endif

        private static Awaitable<Result<string>> Unavailable()
        {
            var done = new AwaitableCompletionSource<Result<string>>();
            done.SetResult(Result<string>.Fail("auth.provider_unavailable"));
            return done.Awaitable;
        }

        /// <summary>Trades Google's code for an ID token (an installed app's client has no secret; PKCE proves it).</summary>
        public static async Awaitable<Result<string>> ExchangeAsync(string clientId, string code, string verifier, CancellationToken cancellation)
        {
            var form = new WWWForm();
            form.AddField("code", code);
            form.AddField("client_id", clientId);
            form.AddField("code_verifier", verifier);
            form.AddField("redirect_uri", GoogleOAuth.RedirectUri(clientId));
            form.AddField("grant_type", "authorization_code");
            using var request = UnityWebRequest.Post(GoogleOAuth.TokenUrl, form);
            await request.SendWebRequest();
            cancellation.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"google token: {request.error}");
                return Result<string>.Fail("auth.provider_unavailable");
            }

            try
            {
                var token = (string?)JObject.Parse(request.downloadHandler.text)["id_token"];
                return token == null ? Result<string>.Fail("auth.provider_unavailable") : Result<string>.Ok(token);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"google token: {error.Message}");
                return Result<string>.Fail("auth.provider_unavailable");
            }
        }
    }
}
