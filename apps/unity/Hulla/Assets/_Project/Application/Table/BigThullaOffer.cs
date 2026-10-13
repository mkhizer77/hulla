namespace Hulla.Application.Table
{
    /// <summary>
    /// The once-per-session "biggest thulla of the night" share offer (GDD 10.9): made the first time a
    /// round's biggest pick-up reaches the threshold and beats every earlier round this session.
    /// </summary>
    public sealed class BigThullaOffer
    {
        public const long Threshold = 10;

        private long _best;
        private bool _offered;

        /// <returns>True when this round earns the offer.</returns>
        public bool Consider(long biggestPickUp)
        {
            var beat = biggestPickUp > _best;
            if (beat)
            {
                _best = biggestPickUp;
            }

            if (_offered || !beat || biggestPickUp < Threshold)
            {
                return false;
            }

            _offered = true;
            return true;
        }
    }
}
