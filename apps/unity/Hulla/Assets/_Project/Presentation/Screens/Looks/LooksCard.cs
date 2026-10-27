using System;
using Hulla.Application.Settings;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Looks
{
    /// <summary>
    /// The Home card for the look playtest: the two new looks as tiles, one tap to switch, and a link
    /// to answer early while one of them is on.
    /// </summary>
    public static class LooksCard
    {
        /// <param name="card">The empty "looks" element on Home.</param>
        /// <param name="ask">Shown while a test look is on and has no answer; null hides the link.</param>
        public static void Fill(VisualElement? card, bool show, TableSkin current, Action<TableSkin> pick, Action? ask)
        {
            if (card == null)
            {
                return;
            }

            card.Clear();
            card.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
            {
                return;
            }

            var title = new Label(LookText.CardTitle);
            title.AddToClassList("looks__title");
            card.Add(title);
            var line = new Label(LookText.CardLine);
            line.AddToClassList("caption");
            card.Add(line);
            var row = new VisualElement();
            row.AddToClassList("looks__row");
            card.Add(row);
            foreach (var look in LookPlaytest.Looks)
            {
                var tile = new Button(() => pick(look)) { name = "look-" + LookPlaytest.Id(look) };
                tile.AddToClassList("theme-tile");
                tile.AddToClassList("looks__tile");
                tile.EnableInClassList("theme-tile--on", look == current);
                var preview = new VisualElement { pickingMode = PickingMode.Ignore };
                preview.AddToClassList("theme-tile__preview");
                preview.AddToClassList("theme-tile__preview--" + LookPlaytest.Id(look));
                tile.Add(preview);
                var name = new Label(UiText.SkinName(look)) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("theme-tile__name");
                tile.Add(name);
                row.Add(tile);
            }

            if (ask != null)
            {
                var link = new Button(ask) { name = "look-ask", text = LookText.RateThis };
                link.AddToClassList("button");
                link.AddToClassList("button--quiet");
                card.Add(link);
            }
        }
    }
}
