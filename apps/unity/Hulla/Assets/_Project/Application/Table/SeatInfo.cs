namespace Hulla.Application.Table
{
    /// <summary>One seat as the lobby and the table show it.</summary>
    public readonly struct SeatInfo
    {
        public SeatInfo(long seat, string? playerId, string? displayName, bool ready, bool connected, bool autoPlay, string? tableSurface = null, string? cardBack = null, string? avatar = null, bool mic = false, string? cardFace = null, string? celebration = null, bool bot = false)
        {
            Seat = seat;
            PlayerId = playerId;
            DisplayName = displayName;
            Ready = ready;
            Connected = connected;
            AutoPlay = autoPlay;
            TableSurface = tableSurface;
            CardBack = cardBack;
            Avatar = avatar;
            Mic = mic;
            CardFace = cardFace;
            Celebration = celebration;
            Bot = bot;
        }

        /// <summary>A bot holds this seat (a lone Quick Play player's table); its seat says so.</summary>
        public bool Bot { get; }

        /// <summary>The seat's microphone is on (GDD 11.4); the voice channel opens while two are.</summary>
        public bool Mic { get; }

        /// <summary>The catalog id of the table surface this seat has equipped (GDD 12.11); null for the default.</summary>
        public string? TableSurface { get; }

        /// <summary>The catalog id of this seat's card back; null for the default.</summary>
        public string? CardBack { get; }

        /// <summary>The catalog id of this seat's card face style; null for the default.</summary>
        public string? CardFace { get; }

        /// <summary>The catalog id of the celebration this seat's thulla plays (GDD 14.6); null for the default.</summary>
        public string? Celebration { get; }

        /// <summary>The avatar the server assigned when the player sat (GDD 15.6), unique at the table; null until known.</summary>
        public string? Avatar { get; }

        public long Seat { get; }

        public string? PlayerId { get; }

        public string? DisplayName { get; }

        public bool Ready { get; }

        public bool Connected { get; }

        public bool AutoPlay { get; }

        public bool Occupied => PlayerId != null;
    }
}
