using System;

namespace Hulla.Application.Social
{
    /// <summary>
    /// Table invite links (GDD 10.5): <c>https://hulla.gg/j/CODE</c> once the domain serves App Links and
    /// Universal Links, and <c>hulla://j/CODE</c>, which works on an installed app today.
    /// </summary>
    public static class InviteLink
    {
        public const string Scheme = "hulla";
        public const string Domain = "hulla.gg";

        /// <summary>Room codes as the server makes them (shared-ts ROOM_CODE_ALPHABET).</summary>
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 6;

        /// <summary>The link put in shared invites. The custom scheme until the domain is live (#130).</summary>
        public static string For(string code) => Scheme + "://j/" + code;

        /// <returns>The room code in an invite link, upper-cased, or null for any other link.</returns>
        public static string? CodeFrom(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            string path;
            if (uri.Scheme == Scheme)
            {
                // hulla://j/CODE parses with "j" as the host.
                if (!string.Equals(uri.Host, "j", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                path = uri.AbsolutePath;
            }
            else if (uri.Scheme == Uri.UriSchemeHttps
                && (string.Equals(uri.Host, Domain, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Host, "www." + Domain, StringComparison.OrdinalIgnoreCase)))
            {
                if (!uri.AbsolutePath.StartsWith("/j/", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                path = uri.AbsolutePath.Substring(2);
            }
            else
            {
                return null;
            }

            var code = path.Trim('/').ToUpperInvariant();
            if (code.Length != CodeLength)
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
