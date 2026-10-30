using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Social
{
    /// <summary>
    /// The sign-in providers' public ids (Resources/HullaSocial.json; docs/runbooks/sign-in-setup.md). None are
    /// secrets. Facebook's client token stays out of the repository: builds take it from a local file (SocialSetup).
    /// An empty id hides the provider.
    /// </summary>
    public sealed class SocialConfig
    {
        public const string ResourceName = "HullaSocial";

        public SocialConfig(bool appleSignIn, string googleWebClientId, string googleIosClientId, string facebookAppId)
        {
            AppleSignIn = appleSignIn;
            GoogleWebClientId = googleWebClientId;
            GoogleIosClientId = googleIosClientId;
            FacebookAppId = facebookAppId;
        }

        /// <summary>Sign in with Apple needs the paid Apple Developer account's capability, so it is switched on by hand.</summary>
        public bool AppleSignIn { get; }

        /// <summary>The Google Cloud "Web application" client id: the audience Android's ID tokens are issued for.</summary>
        public string GoogleWebClientId { get; }

        /// <summary>The Google Cloud "iOS" client id; iOS signs in through a browser sheet with it.</summary>
        public string GoogleIosClientId { get; }

        public string FacebookAppId { get; }

        public static SocialConfig Parse(string json)
        {
            var o = JObject.Parse(json);
            return new SocialConfig(
                (bool?)o["appleSignIn"] == true,
                (string?)o["googleWebClientId"] ?? "",
                (string?)o["googleIosClientId"] ?? "",
                (string?)o["facebookAppId"] ?? "");
        }

        public static SocialConfig Load()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            return asset == null ? new SocialConfig(false, "", "", "") : Parse(asset.text);
        }
    }
}
