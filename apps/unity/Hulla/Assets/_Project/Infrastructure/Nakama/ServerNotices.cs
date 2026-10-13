using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>Reads the server's notification contents.</summary>
    public static class ServerNotices
    {
        public const string AchievementUnlocked = "achievement_unlocked";

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
