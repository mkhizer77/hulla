using System;
using System.Text.RegularExpressions;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>
    /// The server refuses a join or a sign-in with one of its error codes as the message (a match
    /// join attempt's reject message, a hook's thrown error); this reads it back out.
    /// </summary>
    public static class ServerErrors
    {
        private static readonly Regex Code = new Regex(@"\b(table|auth|social|profile|content|network|voice)\.[a-z_]+\b");

        public static string CodeOf(Exception error, string fallback)
        {
            var match = Code.Match(error.Message);
            return match.Success ? match.Value : fallback;
        }
    }
}
