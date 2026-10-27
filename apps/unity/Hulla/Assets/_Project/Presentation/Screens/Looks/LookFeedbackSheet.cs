using System;
using System.Collections.Generic;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Looks
{
    /// <summary>
    /// The question after a round in a test look: how they like it (five answers), keep it or go
    /// back, an optional line. Send stays off until there is an answer; Not now asks again later.
    /// </summary>
    public static class LookFeedbackSheet
    {
        public const string Name = "look-feedback";
        public const int CommentMax = 280;

        /// <param name="onSend">rating 1 to 5, keep, comment.</param>
        public static VisualElement Build(string lookName, string beforeName, Action<int, bool, string> onSend, Action onLater)
        {
            var overlay = new VisualElement { name = Name };
            overlay.AddToClassList("overlay");
            overlay.AddToClassList("results");
            overlay.style.display = DisplayStyle.Flex;
            var panel = new VisualElement();
            panel.AddToClassList("panel");
            panel.AddToClassList("dialog");
            panel.AddToClassList("look-sheet");
            overlay.Add(panel);

            var title = new Label(Strings.Format(LookText.Ask, lookName));
            title.AddToClassList("title");
            title.AddToClassList("dialog__text");
            panel.Add(title);

            var rating = 0;
            var keep = true;
            var send = new Button { name = "look-send", text = LookText.Send };
            send.AddToClassList("button");
            send.AddToClassList("button--primary");
            send.AddToClassList("dialog__button");
            send.SetEnabled(false);

            var ratings = Chips(panel, "look-rating");
            var ratingChips = new List<Button>();
            for (var stars = 5; stars >= 1; stars--)
            {
                var value = stars;
                var chip = Chip(ratings, "look-rate-" + stars, LookText.Rating(stars));
                ratingChips.Add(chip);
                chip.clicked += () =>
                {
                    rating = value;
                    foreach (var other in ratingChips)
                    {
                        other.EnableInClassList("chip--on", other == chip);
                    }

                    send.SetEnabled(true);
                };
            }

            var keeps = Chips(panel, "look-keep");
            var keepChip = Chip(keeps, "look-keep-yes", LookText.Keep);
            var backChip = Chip(keeps, "look-keep-no", Strings.Format(LookText.Back, beforeName));
            keepChip.AddToClassList("chip--on");
            keepChip.clicked += () => { keep = true; keepChip.AddToClassList("chip--on"); backChip.RemoveFromClassList("chip--on"); };
            backChip.clicked += () => { keep = false; backChip.AddToClassList("chip--on"); keepChip.RemoveFromClassList("chip--on"); };

            var comment = new TextField { name = "look-comment", maxLength = CommentMax };
            comment.AddToClassList("field");
            comment.textEdition.placeholder = LookText.Comment;
            comment.textEdition.hidePlaceholderOnFocus = true;
            panel.Add(comment);

            send.clicked += () =>
            {
                onSend(rating, keep, comment.value);
                panel.Clear();
                var thanks = new Label(LookText.Thanks);
                thanks.AddToClassList("title");
                thanks.AddToClassList("dialog__text");
                panel.Add(thanks);
                overlay.schedule.Execute(overlay.RemoveFromHierarchy).StartingIn(1600);
            };
            panel.Add(send);

            var later = new Button(() =>
            {
                overlay.RemoveFromHierarchy();
                onLater();
            }) { name = "look-later", text = UiText.NotNow };
            later.AddToClassList("button");
            later.AddToClassList("button--quiet");
            later.AddToClassList("dialog__button");
            panel.Add(later);
            return overlay;
        }

        private static VisualElement Chips(VisualElement parent, string name)
        {
            var row = new VisualElement { name = name };
            row.AddToClassList("chips");
            row.AddToClassList("look-sheet__chips");
            parent.Add(row);
            return row;
        }

        private static Button Chip(VisualElement row, string name, string text)
        {
            var chip = new Button { name = name, text = text };
            chip.AddToClassList("chip");
            row.Add(chip);
            return chip;
        }
    }
}
