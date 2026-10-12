namespace Hulla.Application.Table
{
    /// <summary>Hand order for display: grouped by suit (spades, hearts, clubs, diamonds), then rank 2 to A.</summary>
    public static class CardOrder
    {
        private const string Ranks = "23456789TJQKA";
        private const string Suits = "SHCD";

        public static int Compare(string a, string b)
        {
            var suit = Suits.IndexOf(a[1]).CompareTo(Suits.IndexOf(b[1]));
            return suit != 0 ? suit : Ranks.IndexOf(a[0]).CompareTo(Ranks.IndexOf(b[0]));
        }
    }
}
