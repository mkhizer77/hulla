using System;
using UnityEngine;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// Precomputed opponent positions for 3 to 6 seats (GDD 14.3): the player sits at the bottom centre,
    /// so the next seat in play order is low on the left and the order reads left to right across the
    /// top. Positions are fractions of the table zone (x across, y down) for the badge centre, so the
    /// badges sit on the rail around the felt.
    /// </summary>
    public static class SeatArc
    {
        private static readonly Vector2[][] Arcs =
        {
            new Vector2[0],
            new[] { new Vector2(0.5f, 0.09f) },
            new[] { new Vector2(0.22f, 0.1f), new Vector2(0.78f, 0.1f) },
            new[] { new Vector2(0.1f, 0.5f), new Vector2(0.5f, 0.09f), new Vector2(0.9f, 0.5f) },
            new[] { new Vector2(0.1f, 0.6f), new Vector2(0.24f, 0.1f), new Vector2(0.76f, 0.1f), new Vector2(0.9f, 0.6f) },
            new[] { new Vector2(0.1f, 0.66f), new Vector2(0.11f, 0.3f), new Vector2(0.5f, 0.09f), new Vector2(0.89f, 0.3f), new Vector2(0.9f, 0.66f) },

            // Six around a watcher at a six-seat table.
            new[] { new Vector2(0.09f, 0.7f), new Vector2(0.1f, 0.38f), new Vector2(0.3f, 0.09f), new Vector2(0.7f, 0.09f), new Vector2(0.9f, 0.38f), new Vector2(0.91f, 0.7f) },
        };

        /// <summary>Where opponent <paramref name="index"/> (0 = the next seat after the player) sits at a table of <paramref name="seatCount"/>.</summary>
        public static Vector2 Position(int seatCount, int index)
        {
            var opponents = Math.Clamp(seatCount - 1, 0, Arcs.Length - 1);
            var arc = Arcs[opponents];
            return index >= 0 && index < arc.Length ? arc[index] : new Vector2(0.5f, 0.5f);
        }

        /// <summary>The opponent slot a seat occupies, counted from the seat after the player.</summary>
        public static int IndexOf(long seat, long mySeat, int seatCount) =>
            seatCount <= 1 ? 0 : (int)((seat - mySeat - 1 + seatCount) % seatCount);
    }
}
