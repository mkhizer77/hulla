using System;

namespace Hulla.Application.Social
{
    /// <summary>
    /// Table invite links (GDD 10.5): <c>https://hulla.gg/j/CODE</c> once the domain serves App Links and
    /// Universal Links, and <c>hulla://j/CODE</c>, which works on an installed app today. Referral links
    /// (GDD 10.9) carry the sharer's friend code the same way: <c>hulla://r/FRIENDCODE</c>.
    /// </summary>
    public static class InviteLink
    {
        public const string Scheme = "hulla";
        public const string Domain = "hulla.gg";

        /// <summary>Room and friend codes as the server makes them (shared-ts ROOM_CODE_ALPHABET).</summary>
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 6;
        private const int FriendCodeLength = 8;

        /// <summary>The link put in shared invites. The custom scheme until the domain is live (#130).</summary>
        public static string For(string code) => Scheme + "://j/" + code;

        /// <summary>The link on a share card away from a private table: it offers to add the sharer.</summary>
        public static string ReferralFor(string friendCode) => Scheme + "://r/" + friendCode;

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
            else if (uri.Scheme == Uri.UriSchemeHttps
                && (string.Equals(uri.Host, Domain, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "www." + Domain, StringComparison.OrdinalIgnoreCase)))
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
