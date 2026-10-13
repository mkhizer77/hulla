namespace Hulla.Presentation.Text
{
    /// <summary>English for the Ranked screen and queue until the string tables land (#65).</summary>
    public static class RankedText
    {
        public const string Title = "Ranked";
        public const string Play = "Play ranked";
        public const string Searching = "Finding a ranked table";
        public const string Widened = "Looking a little wider";
        public const string Placement = "Placement {0} of {1}";
        public const string PlacementNote = "Play {0} more to get your league.";
        public const string Unranked = "Unranked";
        public const string Rating = "Rating {0}";
        public const string Rank = "#{0} this season";
        public const string Protected = "Promotion protected for {0} more";
        public const string SeasonEnds = "Season {0} ends in {1}";
        public const string Leagues = "Leagues";
        public const string Top = "Top this season";
        public const string TopEmpty = "Nobody has finished placement yet.";
        public const string Rules = "4 players · 12 s turns · no hand-taking · no card memory";
        public const string UstadNote = "Top {0}";
        public const string SeasonEnded = "Season {0} ended: {1}. Your rewards are in.";
        public const string SeasonEndedUnplaced = "Season {0} ended before you finished placement.";
        public const string CardBack = "card back";
        public const string Frame = "frame";
        public const string Coins = "{0} coins";

        public static string League(string? id) => id switch
        {
            "bronze" => "Bronze",
            "silver" => "Silver",
            "gold" => "Gold",
            "platinum" => "Platinum",
            "diamond" => "Diamond",
            "ustad" => "Ustad",
            null => Unranked,
            _ => id,
        };

        /// <summary>"title, 600 coins, card back" from what a league pays at season end.</summary>
        public static string Reward(Hulla.Application.Progress.RankedLeague league)
        {
            var parts = new System.Collections.Generic.List<string> { "\"" + ItemText.Name(league.RewardTitle.Replace("ranked_", "")) + "\" title" };
            if (league.RewardCoins > 0)
            {
                parts.Add(string.Format(Coins, league.RewardCoins.ToString("N0")));
            }

            if (league.RewardCardBack)
            {
                parts.Add(CardBack);
            }

            if (league.RewardFrame)
            {
                parts.Add(Frame);
            }

            return string.Join(", ", parts);
        }

        /// <summary>The season number for display: "s3" → "3".</summary>
        public static string Season(string seasonId) => seasonId.StartsWith("s") ? seasonId.Substring(1) : seasonId;
    }
}
