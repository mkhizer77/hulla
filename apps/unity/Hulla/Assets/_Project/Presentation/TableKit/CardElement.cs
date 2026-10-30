using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// A card face until the slice decks arrive (#34): the index (rank over suit) in the top-left and,
    /// upside down, the bottom-right corner, with the pips drawn as vectors in between
    /// (<see cref="CardFace"/>). Court cards carry their letter over a framed band,
    /// or the Desi court deck's portrait (<see cref="Theme.CosmeticArt"/>).
    /// </summary>
    public sealed class CardElement : VisualElement
    {
        // The palette's suit colours (GDD 15.2); painter2D cannot read USS variables.
        private static readonly Color Red = new Color32(0xD3, 0x2F, 0x2F, 0xFF);
        private static readonly Color Black = new Color32(0x1B, 0x1B, 0x1F, 0xFF);
        private static readonly Color Blue = new Color32(0x1E, 0x6F, 0xD9, 0xFF);
        private static readonly Color Green = new Color32(0x2E, 0x7D, 0x32, 0xFF);
        private static readonly Color Frame = new Color32(0xC9, 0x8F, 0x10, 0xFF);

        private readonly Label _rank = new Label();
        private readonly Label _suit = new Label();
        private readonly Label _court = new Label();
        private readonly Label _rankBottom = new Label();
        private readonly Label _suitBottom = new Label();
        private readonly VisualElement _field = new VisualElement();
        private readonly VisualElement _courtArt = new VisualElement();
        private bool _fourColour;

        public CardElement()
        {
            AddToClassList("card");
            // A soft ring outside the edge for a playable card; first, so the face draws over its inner edge.
            var glow = new VisualElement { pickingMode = PickingMode.Ignore };
            glow.AddToClassList("card__glow");
            Add(glow);
            var top = new VisualElement();
            top.AddToClassList("card__corner");
            _rank.AddToClassList("card__rank");
            _suit.AddToClassList("card__suit");
            top.Add(_rank);
            top.Add(_suit);
            Add(top);
            _field.AddToClassList("card__field");
            _field.generateVisualContent += DrawPips;
            Add(_field);

            // The Desi court deck's portrait in the court band, framed; shown only under its face class.
            _courtArt.AddToClassList("card__court-art");
            var portrait = new VisualElement { pickingMode = PickingMode.Ignore };
            portrait.AddToClassList("card__court-face");
            _courtArt.Add(portrait);
            _courtArt.pickingMode = PickingMode.Ignore;
            _field.Add(_courtArt);
            _court.AddToClassList("card__court");
            Add(_court);
            var bottom = new VisualElement();
            bottom.AddToClassList("card__corner");
            bottom.AddToClassList("card__corner--bottom");
            _rankBottom.AddToClassList("card__rank");
            _suitBottom.AddToClassList("card__index-suit");
            bottom.Add(_rankBottom);
            bottom.Add(_suitBottom);
            Add(bottom);

            // A card that cannot be played now is shaded, not see-through: the cards behind it in the fan
            // never show through it.
            var shade = new VisualElement { pickingMode = PickingMode.Ignore };
            shade.AddToClassList("card__shade");
            Add(shade);
            pickingMode = PickingMode.Position;
            foreach (var child in new VisualElement[] { top, bottom, _rank, _suit, _court, _field, _rankBottom, _suitBottom })
            {
                child.pickingMode = PickingMode.Ignore;
            }

            RegisterCallback<AttachToPanelEvent>(_ => RefreshFourColour());
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
            var court = card[0] == 'J' || card[0] == 'Q' || card[0] == 'K';
            _court.text = court ? rank : "";
            _court.style.display = court ? DisplayStyle.Flex : DisplayStyle.None;
            var red = card[1] == 'H' || card[1] == 'D';
            EnableInClassList("card--red", red);
            EnableInClassList("card--suit-d", card[1] == 'D');
            EnableInClassList("card--suit-c", card[1] == 'C');
            EnableInClassList("card--court", court);
            _courtArt.EnableInClassList("card__court-art--j", card[0] == 'J');
            _courtArt.EnableInClassList("card__court-art--q", card[0] == 'Q');
            _courtArt.EnableInClassList("card__court-art--k", card[0] == 'K');
            _field.MarkDirtyRepaint();
            return this;
        }

        /// <summary>The four-colour deck (GDD 27) comes as a class on the shell root; the pips follow it.</summary>
        private void RefreshFourColour()
        {
            var four = false;
            for (var e = parent; e != null; e = e.parent)
            {
                four |= e.ClassListContains("deck--four");
            }

            if (four != _fourColour)
            {
                _fourColour = four;
                _field.MarkDirtyRepaint();
            }
        }

        private void DrawPips(MeshGenerationContext context)
        {
            if (Card.Length < 2)
            {
                return;
            }

            var suit = Card[1];
            var colour = suit == 'H' ? Red
                : suit == 'D' ? (_fourColour ? Blue : Red)
                : suit == 'C' ? (_fourColour ? Green : Black)
                : Black;
            var rect = _field.contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            CardFace.Draw(context.painter2D, rect, Card[0], suit, colour, Frame);
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
