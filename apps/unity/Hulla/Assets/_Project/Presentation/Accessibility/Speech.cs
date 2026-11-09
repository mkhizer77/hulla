using Hulla.Presentation.Text;

namespace Hulla.Presentation.Accessibility
{
    /// <summary>The words the screen reader speaks for cards and table moments (GDD 27: "Sara played King of Hearts").</summary>
    public static class Speech
    {
        /// <summary>"King of Hearts" for "KH"; the card id itself if it is not one.</summary>
        public static string Card(string card)
        {
            if (card.Length != 2 || !Strings.Has("card.rank." + card[0]) || !Strings.Has("card.suit." + card[1]))
            {
                return card;
            }

            return Strings.Get("card.name", Strings.Text("card.rank." + card[0]), Strings.Text("card.suit." + card[1]));
        }

        public static string Played(string name, string card, bool thulla) =>
            Strings.Get(thulla ? "speech.thulla" : "speech.played", name, Card(card));

        public static string PickedUp(string name, int cards) => Strings.Get("speech.picked_up", name, cards);

        public static string YourTurn => Strings.Text("speech.your_turn");

        public static string Bhabhi(string name) => Strings.Get("speech.bhabhi", name);

        public static string Escaped(string name) => Strings.Get("speech.escaped", name);

        public static string CannotPlay(string card) => Strings.Get("speech.cannot_play", Card(card));
    }
}
