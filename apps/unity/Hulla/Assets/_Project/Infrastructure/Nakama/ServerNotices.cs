using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>Reads the server's notification contents.</summary>
    public static class ServerNotices
    {
        public const string AchievementUnlocked = "achievement_unlocked";
        public const string TableInvite = "table_invite";

        /// <summary>The inviter's name, the room code and the open seats in a table_invite notice; null when unreadable.</summary>
        public static (string FromName, string Code, long OpenSeats)? Invite(string content)
        {
            try
            {
                var notice = JObject.Parse(content);
                var code = (string?)notice["code"];
                return string.IsNullOrEmpty(code) ? null : ((string?)notice["fromName"] ?? "?", code!, (long?)notice["openSeats"] ?? 0);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>The ids in an achievement_unlocked notice (<c>{"unlocked":[...]}</c>); empty when unreadable.</summary>
        public static IReadOnlyList<string> UnlockedIds(string content)
        {
            var ids = new List<string>();
            try
            {
                if (JObject.Parse(content)["unlocked"] is JArray list)
                {
                    foreach (var id in list)
                    {
                        ids.Add((string)id!);
                    }
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // A notice we can't read just shows nothing; the list has it on the next fetch.
            }

            return ids;
        }
    }
}
