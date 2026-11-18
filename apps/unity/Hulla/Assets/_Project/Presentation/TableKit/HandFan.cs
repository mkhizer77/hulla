using System;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// How much of each card shows in the fanned hand (GDD 14.4): the whole fan fits the width, every
    /// card keeps at least a 22 px strip with its index visible, and a hand too long for one row
    /// wraps into rows of equal length.
    /// </summary>
    public readonly struct HandFan
    {
        public const float MinStrip = 22f;

        /// <summary>The most rows a hand wraps into.</summary>
        public const int MaxRows = 3;

        private HandFan(int rows, int perRow, float strip)
        {
            Rows = rows;
            PerRow = perRow;
            Strip = strip;
        }

        public int Rows { get; }

        public int PerRow { get; }

        /// <summary>Visible width of every card but the last in a row.</summary>
        public float Strip { get; }

        public static HandFan For(int cards, float width, float cardWidth)
        {
            if (cards <= 1 || width <= cardWidth)
            {
                return new HandFan(1, Math.Max(cards, 1), cardWidth);
            }

            var rows = 1;
            while (true)
            {
                var perRow = (cards + rows - 1) / rows;
                var strip = perRow <= 1 ? cardWidth : (width - cardWidth) / (perRow - 1);
                // Never more than three rows: a fourth would stack the hand into a column of single
                // cards. Past that the cards overlap more but every row still fans.
                if (strip >= MinStrip || perRow <= 1 || rows >= MaxRows)
                {
                    return new HandFan(rows, perRow, Math.Max(0f, Math.Min(strip, cardWidth + 8f)));
                }

                rows++;
            }
        }
    }
}
