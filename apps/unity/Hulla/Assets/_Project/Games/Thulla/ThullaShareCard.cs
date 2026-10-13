using System;
using Hulla.Application.Table;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using UnityEngine.UIElements;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The results share card (GDD 10.9): the Bhabhi stamp, the escape order with avatars, the round's
    /// highlight and a footer with the room code. Built as UI Toolkit elements for CardRenderer.
    /// </summary>
    public static class ThullaShareCard
    {
        private const int Avatars = 6;

        public static void Build(VisualElement root, TableModel model, Func<long, string> nameOf, string footer, bool story)
        {
            var result = model.LastResult ?? throw new InvalidOperationException("no round result to share");
            var card = new VisualElement();
            card.AddToClassList("share-card");
            if (story)
            {
                card.AddToClassList("share-card--story");
            }

            root.Add(card);
            card.Add(Text(UiText.AppName, "share-card__brand"));
            card.Add(Text(UiText.ShareCardGame, "share-card__game"));

            var stamp = new VisualElement();
            stamp.AddToClassList("share-card__stamp");
            stamp.Add(Text(LoserTitle(model).ToUpperInvariant(), "share-card__stamp-title"));
            stamp.Add(Text(nameOf(result.BhabhiSeat), "share-card__stamp-name"));
            card.Add(stamp);

            var list = new VisualElement();
            list.AddToClassList("share-card__order");
            for (var i = 0; i < result.EscapeOrder.Count; i++)
            {
                list.Add(Row(i + 1, result.EscapeOrder[i], nameOf, result.EscapeOrder[i] == model.MySeat));
            }

            var last = Row(result.EscapeOrder.Count + 1, result.BhabhiSeat, nameOf, result.BhabhiSeat == model.MySeat);
            last.AddToClassList("share-card__row--loser");
            list.Add(last);

            card.Add(list);

            var (picker, picked) = BiggestPickUp(result);
            if (picker >= 0)
            {
                card.Add(Text(string.Format(UiText.MostPickedUp, nameOf(picker), picked), "share-card__highlight"));
            }

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            card.Add(spacer);
            card.Add(Text(footer, "share-card__footer"));
        }

        /// <summary>The seat that picked up the most cards this round, or -1.</summary>
        public static (long Seat, long Cards) BiggestPickUp(RoundEnd result)
        {
            long seat = -1;
            long cards = 0;
            for (var i = 0; i < result.Stats.Count; i++)
            {
                if (result.Stats[i].CardsPickedUp > cards)
                {
                    cards = result.Stats[i].CardsPickedUp;
                    seat = i;
                }
            }

            return (seat, cards);
        }

        private static VisualElement Row(int place, long seat, Func<long, string> nameOf, bool me)
        {
            var row = new VisualElement();
            row.AddToClassList("share-card__row");
            if (me)
            {
                row.AddToClassList("share-card__row--me");
            }

            row.Add(Text(place.ToString(), "share-card__place"));
            var name = nameOf(seat);
            var avatar = Text(name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?", "share-card__avatar");
            avatar.AddToClassList("share-card__avatar--" + (seat % Avatars));
            row.Add(avatar);
            row.Add(Text(name, "share-card__name"));
            return row;
        }

        private static Label Text(string text, string cls)
        {
            var label = new Label(text);
            label.AddToClassList(cls);
            return label;
        }

        /// <summary>The table's word for the loser (Bhabhi, Donkey or Thullad).</summary>
        public static string LoserTitle(TableModel model) =>
            (model.Selection?.Settings?.LoserTitle ?? Protocol.LoserTitle.Bhabhi) switch
            {
                Protocol.LoserTitle.Donkey => RulebookText.Donkey,
                Protocol.LoserTitle.Thullad => RulebookText.Thullad,
                _ => RulebookText.Bhabhi,
            };
    }
}
