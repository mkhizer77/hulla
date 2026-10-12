namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The Thulla rulebook in English (GDD 14.11, 6.13, R44, Appendix A), one sentence per toggle and
    /// value, kept in one place for the string tables of the localisation pass (#65).
    /// </summary>
    public static class RulebookText
    {
        public const string Title = "Rules";
        public const string ThisTable = "This table";
        public const string Basics = "How a round goes";
        public const string WhyNot = "Why couldn't I play that?";
        public const string AutoPlay = "When the timer runs out";
        public const string Glossary = "Words at the table";
        public const string Tips = "Three tips";

        public const string Preset = "Preset: {0}.";
        public const string PresetStandard = "Standard";
        public const string PresetFamily = "Family";
        public const string PresetHardcore = "Hardcore";
        public const string PresetClassicPagat = "Classic Pagat";
        public const string PresetCustom = "Custom";

        public const string OpeningSimultaneousOn = "Everyone answers the opening Ace of Spades at the same time, within 10 seconds.";
        public const string OpeningSimultaneousOff = "The opening trick is played in turn from the Ace of Spades.";
        public const string OpeningThullaOff = "The opening trick never causes a pick-up; its cards go to the discard.";
        public const string OpeningThullaOn = "Careful: a thulla counts in the opening trick, so someone can pick up the opening pile.";
        public const string ThullaEndsTrickOn = "A thulla stops the trick at once; whoever played the highest card of the led suit picks up the pile.";
        public const string ThullaEndsTrickOff = "After a thulla, play carries on around the table; at the end the highest card of the led suit picks up everything.";
        public const string EmptyHandDrawDiscard = "Win a trick with your last card and you draw a random card from the discard, then lead.";
        public const string EmptyHandDrawNext = "Win a trick with your last card and you draw a random card from the next player's hand, then lead.";
        public const string EmptyHandInstantExit = "Win a trick with your last card and you escape at once; the next player leads.";
        public const string HandTakingOff = "Taking another player's hand is off.";
        public const string HandTakingLeader = "From the second trick, the leader may take the whole hand of the next active player before leading (press and hold). That player escapes.";
        public const string HandTakingAnyone = "Before the lead, anyone may take the hand of the next active player; the first request wins. That player escapes.";
        public const string EndgameDrawOpponent = "With two players left, a leader who wins with their last card draws a random card from the opponent.";
        public const string EndgameDrawDiscard = "With two players left, a leader who wins with their last card draws a random card from the discard.";
        public const string EndgameThullaEndsRound = "With two players left, the first thulla ends the round: whoever picks up is the {0}.";
        public const string FastForwardOn = "When the loser is certain, the round ends straight away.";
        public const string FastForwardOff = "Rounds play to the last card even when the loser is certain.";
        public const string TurnTimer = "Each turn has {0} seconds.";
        public const string NoTurnTimer = "There is no turn timer.";
        public const string LoserTitle = "The last player holding cards is the {0}.";
        public const string Bhabhi = "Bhabhi";
        public const string Donkey = "Donkey";
        public const string Thullad = "Thulla'd";

        public const string Basic1 = "All 52 cards are dealt. Whoever holds the Ace of Spades leads it to open.";
        public const string Basic2 = "Follow the led suit if you can. If you can't, any card you play is a thulla.";
        public const string Basic3 = "Get rid of all your cards to escape. The last player still holding cards loses the round.";

        public const string WhyFollow = "You hold a card of the led suit, so you have to play one of them (R18). Only those cards are lit.";
        public const string WhyAce = "The round opens with the Ace of Spades, and its holder has to lead it (R12).";
        public const string WhyTurn = "It isn't your turn yet; your cards light up when it is.";
        public const string WhyServer = "The server checks every card. A card it refuses goes back to your hand with no penalty (R47).";

        public const string AutoFollow = "Following suit: your lowest card of the led suit.";
        public const string AutoThulla = "No card of the led suit: your highest card.";
        public const string AutoLead = "Leading: the lowest card of the suit you hold most of.";
        public const string AutoOpening = "In the opening trick: your lowest spade, otherwise your highest card.";
        public const string AutoNever = "Taking a hand and rematch votes are never played for you.";

        public const string WordThulla = "Thulla: the off-suit card that stops a trick (literally slang for a police officer).";
        public const string WordBhabhi = "Bhabhi: brother's wife; the loser's title.";
        public const string WordHaath = "Haath: hand; also the right to lead (\"haath mera hai\").";
        public const string WordHaathLena = "Haath lena: to take another player's hand.";
        public const string WordSuits = "Hukum, paan, eent, chidi: spades, hearts, diamonds, clubs.";
        public const string WordCourt = "Yakka, badshah, begum, ghulam: ace, king, queen, jack.";
        public const string WordEscape = "Nikal gaya, bach gaya: got away, escaped.";

        public const string Tip1 = "Run out of a suit early: once you can't follow, every card of that suit someone leads lets you give a thulla.";
        public const string Tip2 = "Watch what others pick up. A pile someone takes tells you which suits they now hold.";
        public const string Tip3 = "Leading your highest card can leave you with the hand and no cards; keep a low card for later.";
    }
}
