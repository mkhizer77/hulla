namespace Hulla.Application.Table
{
    /// <summary>One card on the pile and the seat that played it.</summary>
    public readonly struct TrickPlay
    {
        public TrickPlay(long seat, string card)
        {
            Seat = seat;
            Card = card;
        }

        public long Seat { get; }

        public string Card { get; }
    }
}
