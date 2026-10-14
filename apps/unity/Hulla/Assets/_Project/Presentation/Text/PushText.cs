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
        public static string ChannelName => Strings.Text("push.channel_name");
        public static string AskTitle => Strings.Text("push.ask_title");
        public static string AskYes => Strings.Text("push.ask_yes");
        public static string AskNo => Strings.Text("push.ask_no");
        /// <summary>The server's push keys (push_&lt;type&gt;_title, _body) with their text from the tables.</summary>
        public static IReadOnlyDictionary<string, string> Keyed
        {
            get
            {
                var keyed = new Dictionary<string, string>();
                foreach (var key in Keys)
                {
                    keyed[key] = Strings.Text("push." + key.Substring("push_".Length));
                }

                return keyed;
            }
        }

        private static readonly string[] Keys =
        {
            "push_table_invite_title",
            "push_table_invite_body",
            "push_friend_request_title",
            "push_friend_request_body",
            "push_friend_online_title",
            "push_friend_online_body",
            "push_daily_reward_title",
            "push_daily_reward_body",
            "push_season_end_title",
            "push_season_end_body",
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
