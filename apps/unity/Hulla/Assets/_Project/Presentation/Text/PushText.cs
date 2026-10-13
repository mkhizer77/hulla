using System.Collections.Generic;
using System.Security;
using System.Text.RegularExpressions;

namespace Hulla.Presentation.Text
{
    /// <summary>
    /// Notification text (GDD 10.10). The server sends only these keys and the arguments, and the phone
    /// renders the text, so pushes follow the device language once the string tables are localised.
    /// {0} and {1} are the arguments in the server's order.
    /// </summary>
    public static class PushText
    {
        public const string ChannelName = "Invites and friends";
        public const string AskTitle = "Know when friends invite you to a table?";
        public const string AskYes = "Turn on";
        public const string AskNo = "Not now";

        public static readonly IReadOnlyDictionary<string, string> Strings = new Dictionary<string, string>
        {
            ["push_table_invite_title"] = "Table invite",
            ["push_table_invite_body"] = "{0} invites you to their table ({1}).",
            ["push_friend_request_title"] = "Friend request",
            ["push_friend_request_body"] = "{0} wants to be your friend.",
            ["push_friend_online_title"] = "Friend online",
            ["push_friend_online_body"] = "{0} is online. Fancy a round?",
            ["push_daily_reward_title"] = "Daily reward",
            ["push_daily_reward_body"] = "Your daily reward is ready.",
            ["push_season_end_title"] = "Season ending",
            ["push_season_end_body"] = "The season ends in {0} hours. Claim your rewards.",
        };

        /// <summary>
        /// A string resource for Android: XML-escaped, quotes backslashed, and {0} as %1$s so the
        /// notification fills in the server's arguments.
        /// </summary>
        public static string ToAndroid(string text)
        {
            var escaped = SecurityElement.Escape(text).Replace("&apos;", "\\'").Replace("&quot;", "\\\"");
            return Regex.Replace(escaped, @"\{(\d)\}", m => "%" + (int.Parse(m.Groups[1].Value) + 1) + "$s");
        }
    }
}
