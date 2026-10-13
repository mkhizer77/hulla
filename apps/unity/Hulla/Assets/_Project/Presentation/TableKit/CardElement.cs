using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// A card face drawn with text until the slice deck arrives (#34): the index (rank over suit) in
    /// the top-left and, upside down, the bottom-right corner, and a large pip in the middle.
    /// </summary>
    public sealed class CardElement : VisualElement
    {
        private readonly Label _rank = new Label();
        private readonly Label _suit = new Label();
        private readonly Label _pip = new Label();
        private readonly Label _rankBottom = new Label();
        private readonly Label _suitBottom = new Label();

        public CardElement()
        {
            AddToClassList("card");
            var top = new VisualElement();
            top.AddToClassList("card__corner");
            _rank.AddToClassList("card__rank");
            _suit.AddToClassList("card__suit");
            top.Add(_rank);
            top.Add(_suit);
            Add(top);
            _pip.AddToClassList("card__pip");
            Add(_pip);
            var bottom = new VisualElement();
            bottom.AddToClassList("card__corner");
            bottom.AddToClassList("card__corner--bottom");
            _rankBottom.AddToClassList("card__rank");
            _suitBottom.AddToClassList("card__index-suit");
            bottom.Add(_rankBottom);
            bottom.Add(_suitBottom);
            Add(bottom);
            pickingMode = PickingMode.Position;
            foreach (var child in new VisualElement[] { top, bottom, _rank, _suit, _pip, _rankBottom, _suitBottom })
            {
                child.pickingMode = PickingMode.Ignore;
            }
        }

        public string Card { get; private set; } = "";

        public CardElement Show(string card)
        {
            Card = card;
            var rank = card[0] == 'T' ? "10" : card.Substring(0, 1);
            var suit = SuitGlyph(card[1]);
            _rank.text = rank;
            _suit.text = suit;
            _rankBottom.text = rank;
            _suitBottom.text = suit;
            _pip.text = suit;
            var red = card[1] == 'H' || card[1] == 'D';
            EnableInClassList("card--red", red);
            EnableInClassList("card--suit-d", card[1] == 'D');
            EnableInClassList("card--suit-c", card[1] == 'C');
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
