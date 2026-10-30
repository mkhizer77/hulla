using System;
using System.Security.Cryptography;
using System.Text;

namespace Hulla.Infrastructure.Social
{
    /// <summary>
    /// Google's OAuth for installed apps with PKCE (RFC 7636), used on iOS where the browser sheet signs in:
    /// the sign-in URL, the redirect to the client id's reversed scheme, and the code exchange's form.
    /// </summary>
    public static class GoogleOAuth
    {
        public const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
        public const string TokenUrl = "https://oauth2.googleapis.com/token";
        private const string ClientSuffix = ".apps.googleusercontent.com";

        /// <summary>"com.googleusercontent.apps.123-abc": the iOS client id turned around, Google's redirect scheme.</summary>
        public static string RedirectScheme(string clientId) =>
            "com.googleusercontent.apps." + (clientId.EndsWith(ClientSuffix, StringComparison.Ordinal) ? clientId.Substring(0, clientId.Length - ClientSuffix.Length) : clientId);

        public static string RedirectUri(string clientId) => RedirectScheme(clientId) + ":/oauth2redirect";

        /// <summary>A fresh code verifier: 64 URL-safe characters from 48 random bytes.</summary>
        public static string NewVerifier()
        {
            var bytes = new byte[48];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(bytes);
            return Base64Url(bytes);
        }

        public static string Challenge(string verifier)
        {
            using var sha = SHA256.Create();
            return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        public static string SignInUrl(string clientId, string verifier, string state) =>
            AuthorizeUrl +
            "?client_id=" + Uri.EscapeDataString(clientId) +
            "&redirect_uri=" + Uri.EscapeDataString(RedirectUri(clientId)) +
            "&response_type=code&scope=" + Uri.EscapeDataString("openid email profile") +
            "&code_challenge=" + Challenge(verifier) + "&code_challenge_method=S256" +
            "&state=" + Uri.EscapeDataString(state);

        /// <summary>The code from Google's redirect, or null when it is missing or the state does not match.</summary>
        public static string? CodeFrom(string redirect, string state)
        {
            var query = redirect.IndexOf('?');
            if (query < 0)
            {
                return null;
            }

            string? code = null;
            string? returned = null;
            foreach (var pair in redirect.Substring(query + 1).Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var key = pair.Substring(0, eq);
                var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
                if (key == "code")
                {
                    code = value;
                }
                else if (key == "state")
                {
                    returned = value;
                }
            }

            return returned == state ? code : null;
        }

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
