namespace Hulla.Application.Table
{
    /// <summary>A quick-chat phrase or an emote someone at the table sent.</summary>
    public readonly struct ChatLine
    {
        public ChatLine(long seat, string? phraseId, string? emoteId, long? targetSeat)
        {
            Seat = seat;
            PhraseId = phraseId;
            EmoteId = emoteId;
            TargetSeat = targetSeat;
        }

        public long Seat { get; }

        public string? PhraseId { get; }

        public string? EmoteId { get; }

        public long? TargetSeat { get; }
    }
}
