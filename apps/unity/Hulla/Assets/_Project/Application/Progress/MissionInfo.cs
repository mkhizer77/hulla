namespace Hulla.Application.Progress
{
    /// <summary>One daily or weekly mission (GDD 12.5) with its progress and reward.</summary>
    public sealed class MissionInfo
    {
        public MissionInfo(string id, string metric, long target, long progress, bool claimed, bool claimable, long coins, long gems, bool daily)
        {
            Id = id;
            Metric = metric;
            Target = target;
            Progress = progress;
            Claimed = claimed;
            Claimable = claimable;
            Coins = coins;
            Gems = gems;
            Daily = daily;
        }

        public string Id { get; }

        /// <summary>What counts: thullas_given, escapes, rounds_with_friend… (server services/missions).</summary>
        public string Metric { get; }

        public long Target { get; }

        public long Progress { get; }

        public bool Claimed { get; }

        public bool Claimable { get; }

        public long Coins { get; }

        public long Gems { get; }

        public bool Daily { get; }
    }
}
