namespace Hulla.Application.Progress
{
    /// <summary>What the last finished round paid (GDD 14.8): XP, coins and any level-up.</summary>
    public sealed class RewardInfo
    {
        public RewardInfo(string roundId, long xp, long coins, long levelFrom, long levelTo, bool capped, long welcomeCoins = 0)
        {
            WelcomeCoins = welcomeCoins;
            RoundId = roundId;
            Xp = xp;
            Coins = coins;
            LevelFrom = levelFrom;
            LevelTo = levelTo;
            Capped = capped;
        }

        public string RoundId { get; }

        public long Xp { get; }

        public long Coins { get; }

        public long LevelFrom { get; }

        public long LevelTo { get; }

        /// <summary>The one-time welcome reward paid with the player's first live round (GDD 17.2).</summary>
        public long WelcomeCoins { get; }

        /// <summary>Past today's soft cap, so the coins were reduced.</summary>
        public bool Capped { get; }
    }
}
