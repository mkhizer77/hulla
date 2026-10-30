using System;

namespace Hulla.Application.Social
{
    /// <summary>
    /// Table invite links (GDD 10.5): <c>https://playhulla.pages.dev/j/CODE</c>, served by our own page
    /// (infra/cloudflare/links), which opens the app where it is installed (App Links, Universal Links) and
    /// shows the code and the stores where it is not. <c>hulla://j/CODE</c> still opens an installed app.
    /// Referral links (GDD 10.9) carry the sharer's friend code the same way: <c>/r/FRIENDCODE</c>.
    /// </summary>
    public static class InviteLink
    {
        public const string Scheme = "hulla";

        /// <summary>Where shared links point. The free Pages address until the domain is bought (#130).</summary>
        public const string Host = "playhulla.pages.dev";

        /// <summary>
        /// Every host the app opens links from. Each must serve the link page and its .well-known files: Android
        /// before 12 verifies none of them if one fails. When the domain arrives it joins this list and the Pages
        /// address stays, so links already shared keep working.
        /// </summary>
        public static readonly string[] Hosts = { Host };

        /// <summary>Room and friend codes as the server makes them (shared-ts ROOM_CODE_ALPHABET).</summary>
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 6;
        private const int FriendCodeLength = 8;

        /// <summary>The link put in shared invites.</summary>
        public static string For(string code) => "https://" + Host + "/j/" + code;

        /// <summary>The link on a share card away from a private table: it offers to add the sharer.</summary>
        public static string ReferralFor(string friendCode) => "https://" + Host + "/r/" + friendCode;

        /// <returns>The room code in an invite link, upper-cased, or null for any other link.</returns>
        public static string? CodeFrom(string? url) => Parse(url, "j", CodeLength);

        /// <returns>The friend code in a referral link, upper-cased, or null for any other link.</returns>
        public static string? FriendCodeFrom(string? url) => Parse(url, "r", FriendCodeLength);

        private static string? Parse(string? url, string kind, int length)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            string path;
            if (uri.Scheme == Scheme)
            {
                // hulla://j/CODE parses with "j" as the host.
                if (!string.Equals(uri.Host, kind, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                path = uri.AbsolutePath;
            }
            else if (uri.Scheme == Uri.UriSchemeHttps && Array.Exists(Hosts, h => string.Equals(uri.Host, h, StringComparison.OrdinalIgnoreCase)))
            {
                if (!uri.AbsolutePath.StartsWith("/" + kind + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                path = uri.AbsolutePath.Substring(kind.Length + 1);
            }
            else
            {
                return null;
            }

            var code = path.Trim('/').ToUpperInvariant();
            if (code.Length != length)
            {
                return null;
            }

            foreach (var c in code)
            {
                if (Alphabet.IndexOf(c) < 0)
                {
                    return null;
                }
            }

            return code;
        }
    }
}
