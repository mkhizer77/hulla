namespace Hulla.Application.Social
{
    /// <summary>Someone on the friends screen: a friend, a request either way, a blocked player, or a recent co-player.</summary>
    public sealed class FriendEntry
    {
        public FriendEntry(string userId, string name, string? handle, bool online, FriendState? state, bool favourite, long rounds = 0)
        {
            UserId = userId;
            Name = name;
            Handle = handle;
            Online = online;
            State = state;
            Favourite = favourite;
            Rounds = rounds;
        }

        public string UserId { get; }

        public string Name { get; }

        public string? Handle { get; }

        public bool Online { get; }

        /// <summary>Null for a recent co-player who is not a friend or a request.</summary>
        public FriendState? State { get; }

        public bool Favourite { get; }

        /// <summary>Rounds played together, for recent players.</summary>
        public long Rounds { get; }
    }
}
