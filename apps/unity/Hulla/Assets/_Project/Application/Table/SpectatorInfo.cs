namespace Hulla.Application.Table
{
    /// <summary>Someone watching the table; <see cref="SeatNext"/> when they take a free seat at the round's end.</summary>
    public readonly struct SpectatorInfo
    {
        public SpectatorInfo(string userId, string name, bool seatNext)
        {
            UserId = userId;
            Name = name;
            SeatNext = seatNext;
        }

        public string UserId { get; }

        public string Name { get; }

        public bool SeatNext { get; }
    }
}
