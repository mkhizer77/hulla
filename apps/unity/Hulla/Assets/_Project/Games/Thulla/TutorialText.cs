namespace Hulla.Games.Thulla
{
    /// <summary>The coach's lines (GDD 17.3), in one place for the string tables (#65).</summary>
    public static class TutorialText
    {
        public const string Gate = "Do you know Bhabhi?";
        public const string GateYes = "Yes, let's play";
        public const string GateNo = "No, show me";
        public const string Refresher = "20-second refresher";
        public const string HowToPlay = "How to play";
        public const string Next = "Next";
        public const string Finish = "Let's play";
        public const string Skip = "Skip";

        public const string GotIt = "Got it";
        public const string HintLegal = "Lit cards are the ones you can play now. Hold the led suit? Then you must follow it.";
        public const string HintLeader = "\"Leads\" marks who led this trick. The highest card of that suit takes the hand.";
        public const string HintTakeHand = "You can take the next player's whole hand before you lead. They escape, and you lead with all of it.";
        public const string HintQuickChat = "A thulla! Tap Chat to say something, from \"Le lo bhai!\" to a chai emote.";

        public const string Step1 = "You have the Ace of Spades. Everyone throws a spade. No punishment on the first trick.";
        public const string Step1Hint = "The round opens with the Ace of Spades. Tap it, then tap again to play.";
        public const string Step1Done = "The opening trick goes to the discard. Your Ace was highest, so you lead next.";

        public const string Step2Lead = "Noor leads a heart. You have hearts, so you play one.";
        public const string Step2Hint = "You hold hearts, so you must play a heart. Only those are lit.";
        public const string Step2 = "Bilal has no hearts. His off-suit card is a thulla. The highest heart picks everything up. That's Sara, not you.";
        public const string Step2Done = "Sara took the whole pile into her hand. Thullas make other people pick up.";

        public const string Step3 = "No diamonds? Throw your worst card. Sara's K♦ is the highest, so she picks up.";
        public const string Step3Hint = "You have no diamonds, so any card goes. Throw one you want rid of.";
        public const string Step3Done = "That was your thulla. Running out of a suit early gives you more of them.";

        public const string Step4 = "Two low clubs left. Play low and let someone else take the hand.";
        public const string Step4Hint = "Follow clubs with a low one.";
        public const string Step4Done = "Play your last card low and let someone else take the hand. You're out, you're safe.";

        public const string Step5Lead = "Two players left. Sara leads her last card.";
        public const string Step5Hint = "Follow hearts with your heart.";
        public const string Step5 = "Two left and the leader has no cards? They draw from the other player. Play until it's confirmed.";
        public const string Step5Done = "You're out, and Sara is left holding a card: she's the Bhabhi. Now play a real round.";
    }
}
