namespace Hulla.Presentation.Text
{
    /// <summary>
    /// English names and goals for the achievement list until the string tables land (#65); an id
    /// without a name falls back to its goal.
    /// </summary>
    public static class AchievementText
    {
        public static string Name(string id, string metric, long target) => id switch
        {
            "first_thulla" => "First thulla",
            "thullas_100" => "Thulla machine",
            "thullas_1000" => "Thulla baaz",
            "first_escape" => "Out!",
            "escapes_100" => "Escape artist",
            "escapes_1000" => "Nikal gaya",
            "rounds_10" => "Getting started",
            "rounds_100" => "Regular",
            "rounds_1000" => "Purana khiladi",
            "first_bhabhi" => "Welcome, Bhabhi",
            "bhabhis_50" => "Bhabhi jaan",
            "hand_taken_first" => "Haath saaf",
            "hands_taken_25" => "Hand collector",
            "pickup_10" => "Big pick-up",
            "pickup_20" => "Haath bhar",
            "picked_up_500" => "Card magnet",
            "thullas_received_100" => "Thick skin",
            "series_first" => "Series winner",
            "series_10" => "Series champion",
            "first_friend" => "First friend",
            "friends_10" => "Full table",
            "friends_50" => "Mehfil wala",
            "rounds_with_friend_10" => "Yaar ke saath",
            "rounds_with_friend_100" => "Yaari",
            "full_house_friends" => "Full house",
            "same_group_10" => "Baithak regular",
            "same_group_50" => "Same old gang",
            "escaped_first_10" => "Quick exit",
            "escaped_first_100" => "Sabse pehle",
            "first_escape_streak_5" => "Ustad-e-farar",
            "no_bhabhi_20" => "Kabhi Bhabhi nahi",
            "clean_round_first" => "Clean hands",
            "clean_rounds_25" => "Spotless",
            "houdini" => "Houdini",
            "houdini_10" => "Great escape",
            "first_cosmetic" => "First look",
            "cosmetics_10" => "Collector",
            "cosmetics_30" => "Shaukeen",
            "seasonal_rounds_10" => "In season",
            "seasonal_rounds_50" => "Season regular",
            _ => Goal(metric, target),
        };

        public static string Goal(string metric, long target) => metric switch
        {
            "rounds" => target == 1 ? "Play a round" : $"Play {target} rounds",
            "bhabhis" => target == 1 ? "Be the Bhabhi once" : $"Be the Bhabhi {target} times",
            "biggest_pickup" => $"Pick up {target} cards at once",
            "cards_picked_up" => $"Pick up {target} cards in all",
            "thullas_received" => $"Take {target} thullas",
            "friends" => target == 1 ? "Add a friend" : $"Have {target} friends",
            "rounds_with_four_friends" => "Play a round with four friends",
            "same_group_rounds" => $"Play {target} rounds with the same group",
            "first_escape_streak" => $"Escape first {target} rounds in a row",
            "no_bhabhi_streak" => $"Play {target} rounds in a row without being the Bhabhi",
            "houdini" => target == 1 ? "Escape on a card drawn from the discard" : $"Escape on a drawn card {target} times",
            "cosmetics_owned" => target == 1 ? "Own a cosmetic" : $"Own {target} cosmetics",
            "seasonal_rounds" => $"Play {target} rounds this season",
            "clean_rounds" => target == 1 ? "Escape without picking up" : $"Escape without picking up, {target} times",
            "thullas_given" => target == 1 ? "Give a thulla" : $"Give {target} thullas",
            "escapes" => target == 1 ? "Escape a round" : $"Escape {target} times",
            _ => UiText.MissionText(metric, target),
        };

        public static string Category(string category) => category switch
        {
            "play" => "Play",
            "skill" => "Skill",
            "social" => "Friends",
            "collection" => "Collection",
            "seasonal" => "Season",
            _ => category,
        };
    }
}
