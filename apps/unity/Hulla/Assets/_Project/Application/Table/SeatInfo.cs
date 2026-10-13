namespace Hulla.Application.Table
{
    /// <summary>One seat as the lobby and the table show it.</summary>
    public readonly struct SeatInfo
    {
        public SeatInfo(long seat, string? playerId, string? displayName, bool ready, bool connected, bool autoPlay, string? tableSurface = null, string? cardBack = null)
        {
            Seat = seat;
            PlayerId = playerId;
            DisplayName = displayName;
            Ready = ready;
            Connected = connected;
            AutoPlay = autoPlay;
            TableSurface = tableSurface;
            CardBack = cardBack;
        }

        /// <summary>The catalog id of the table surface this seat has equipped (GDD 12.11); null for the default.</summary>
        public string? TableSurface { get; }

        /// <summary>The catalog id of this seat's card back; null for the default.</summary>
        public string? CardBack { get; }

        public long Seat { get; }

        public string? PlayerId { get; }

        public string? DisplayName { get; }

        public bool Ready { get; }

        public bool Connected { get; }

        public bool AutoPlay { get; }

        public bool Occupied => PlayerId != null;
    }
}
