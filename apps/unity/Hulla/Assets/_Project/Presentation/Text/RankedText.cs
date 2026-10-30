namespace Hulla.Presentation.Text
{
    /// <summary>The Ranked screen and queue's text from the string tables (GDD 26.1, ADR-0023).</summary>
    public static class RankedText
    {
        public static string Title => Strings.Text("ranked.title");
        public static string Play => Strings.Text("ranked.play");
        public static string Searching => Strings.Text("ranked.searching");
        public static string Widened => Strings.Text("ranked.widened");
        public static string Placement => Strings.Text("ranked.placement");
        public static string PlacementNote => Strings.Text("ranked.placement_note");
        public static string Unranked => Strings.Text("ranked.unranked");
        public static string Rating => Strings.Text("ranked.rating");
        public static string Rank => Strings.Text("ranked.rank");
        public static string Protected => Strings.Text("ranked.protected");
        public static string SeasonEnds => Strings.Text("ranked.season_ends");
        public static string Leagues => Strings.Text("ranked.leagues");
        public static string Top => Strings.Text("ranked.top");
        public static string TopEmpty => Strings.Text("ranked.top_empty");
        public static string Rules => Strings.Text("ranked.rules");
        public static string UstadNote => Strings.Text("ranked.ustad_note");
        public static string SeasonEnded => Strings.Text("ranked.season_ended");
        public static string SeasonEndedUnplaced => Strings.Text("ranked.season_ended_unplaced");
        public static string CardBack => Strings.Text("ranked.card_back");
        public static string Frame => Strings.Text("ranked.frame");
        public static string Coins => Strings.Text("ranked.coins");

        /// <summary>league.&lt;id&gt;, the same keys the ranked content names; null is Unranked.</summary>
        public static string League(string? id) =>
            id == null ? Unranked : Strings.Has("league." + id) ? Strings.Text("league." + id) : id;

        /// <summary>"title, 600 coins, card back" from what a league pays at season end.</summary>
        public static string Reward(Hulla.Application.Progress.RankedLeague league)
        {
            // Compact, as in the concept: "frame + card back + 2,000", or "150 coins" on its own.
            var parts = new System.Collections.Generic.List<string>();
            if (league.RewardFrame)
            {
                parts.Add(Frame);
            }

            if (league.RewardCardBack)
            {
                parts.Add(CardBack);
            }

            if (league.RewardCoins > 0)
            {
                parts.Add(parts.Count > 0 ? league.RewardCoins.ToString("N0", Strings.Culture) : Strings.Format(Coins, league.RewardCoins.ToString("N0", Strings.Culture)));
            }

            return string.Join(Strings.Text("ranked.reward_sep"), parts);
        }

        /// <summary>The season number for display: "s3" → "3".</summary>
        public static string Season(string seasonId) => seasonId.StartsWith("s") ? seasonId.Substring(1) : seasonId;
    }
}
