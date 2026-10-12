namespace Hulla.Application.Social
{
    /// <summary>A friend on Home: their presence and, when seated, the table and whether it can be joined.</summary>
    public sealed class FriendPresence
    {
        public FriendPresence(string userId, string name, PresenceState state, bool favourite, string? tableCode, long openSeats, bool joinable, bool watchable = false)
        {
            Watchable = watchable;
            UserId = userId;
            Name = name;
            State = state;
            Favourite = favourite;
            TableCode = tableCode;
            OpenSeats = openSeats;
            Joinable = joinable;
        }

        public string UserId { get; }

        public string Name { get; }

        public PresenceState State { get; }

        public bool Favourite { get; }

        public string? TableCode { get; }

        public long OpenSeats { get; }

        public bool Joinable { get; }

        /// <summary>Their table takes watchers (GDD 7.7).</summary>
        public bool Watchable { get; }
    }
}
