using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>A card face drawn with text until the slice deck arrives (#34): rank over suit, red or black.</summary>
    public sealed class CardElement : VisualElement
    {
        private readonly Label _rank = new Label();
        private readonly Label _suit = new Label();

        public CardElement()
        {
            AddToClassList("card");
            _rank.AddToClassList("card__rank");
            _suit.AddToClassList("card__suit");
            Add(_rank);
            Add(_suit);
        }

        public string Card { get; private set; } = "";

        public CardElement Show(string card)
        {
            Card = card;
            _rank.text = card[0] == 'T' ? "10" : card.Substring(0, 1);
            _suit.text = SuitGlyph(card[1]);
            var red = card[1] == 'H' || card[1] == 'D';
            EnableInClassList("card--red", red);
            return this;
        }

        public static string SuitGlyph(char suit)
        {
            switch (suit)
            {
                case 'S': return "♠";
                case 'H': return "♥";
                case 'C': return "♣";
                default: return "♦";
            }
        }
    }
}
