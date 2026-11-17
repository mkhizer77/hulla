namespace Hulla.Application.Social
{
    /// <summary>The parts of a profile the screens show, so views never touch wire types.</summary>
    public sealed class ProfileCard
    {
        public ProfileCard(string displayName, string handle, string bio, string friendCode, long rounds, long escapes, long bhabhis, long thullasGiven, string? avatar = null, string? title = null, long? level = null)
        {
            Level = level;
            DisplayName = displayName;
            Handle = handle;
            Bio = bio;
            FriendCode = friendCode;
            Rounds = rounds;
            Escapes = escapes;
            Bhabhis = bhabhis;
            ThullasGiven = thullasGiven;
            Avatar = avatar;
            Title = title;
        }

        /// <summary>The player's Rung numbers; null until their first deal.</summary>
        public RungCard? Rung { get; set; }

        public string? Avatar { get; }

        public string? Title { get; }

        /// <summary>The player's level (GDD 12.2), when the server sent it.</summary>
        public long? Level { get; }

        public string DisplayName { get; }

        public string Handle { get; }

        public string Bio { get; }

        public string FriendCode { get; }

        public long Rounds { get; }

        public long Escapes { get; }

        public long Bhabhis { get; }

        public long ThullasGiven { get; }
    }
}

namespace Hulla.Application.Social
{
    /// <summary>The Rung side of the stats card (docs/design/two-games.md); built from the profile's Rung numbers.</summary>
    public sealed class RungCard
    {
        public RungCard(long deals, long dealsWon, long bazis, long bazisWon, long courts, long callerDeals, long callerWins, long defenderDeals, long defenderWins, char? favouriteRung)
        {
            Deals = deals;
            DealsWon = dealsWon;
            Bazis = bazis;
            BazisWon = bazisWon;
            Courts = courts;
            CallerDeals = callerDeals;
            CallerWins = callerWins;
            DefenderDeals = defenderDeals;
            DefenderWins = defenderWins;
            FavouriteRung = favouriteRung;
        }

        public long Deals { get; }

        public long DealsWon { get; }

        public long Bazis { get; }

        public long BazisWon { get; }

        public long Courts { get; }

        public long CallerDeals { get; }

        public long CallerWins { get; }

        public long DefenderDeals { get; }

        public long DefenderWins { get; }

        /// <summary>The suit called most often (S, H, C, D); null before the first call.</summary>
        public char? FavouriteRung { get; }

        /// <summary>Whole percent, 0 when nothing was played.</summary>
        public static long Percent(long won, long played) => played <= 0 ? 0 : (won * 100 + played / 2) / played;

        public long BaziWinPercent => Percent(BazisWon, Bazis);

        public long DealWinPercent => Percent(DealsWon, Deals);

        public long CallerWinPercent => Percent(CallerWins, CallerDeals);

        public long DefenderWinPercent => Percent(DefenderWins, DefenderDeals);

        /// <summary>The suit with the most calls; ties go to spades, hearts, clubs, diamonds.</summary>
        public static char? Favourite(long spades, long hearts, long clubs, long diamonds)
        {
            var best = System.Math.Max(System.Math.Max(spades, hearts), System.Math.Max(clubs, diamonds));
            if (best <= 0)
            {
                return null;
            }

            return spades == best ? 'S' : hearts == best ? 'H' : clubs == best ? 'C' : 'D';
        }
    }
}
