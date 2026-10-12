namespace Hulla.Application.Table
{
    /// <summary>One seat as the lobby and the table show it.</summary>
    public readonly struct SeatInfo
    {
        public SeatInfo(long seat, string? playerId, string? displayName, bool ready, bool connected, bool autoPlay)
        {
            Seat = seat;
            PlayerId = playerId;
            DisplayName = displayName;
            Ready = ready;
            Connected = connected;
            AutoPlay = autoPlay;
        }

        public long Seat { get; }

        public string? PlayerId { get; }

        public string? DisplayName { get; }

        public bool Ready { get; }

        public bool Connected { get; }

        public bool AutoPlay { get; }

        public bool Occupied => PlayerId != null;
    }
}
