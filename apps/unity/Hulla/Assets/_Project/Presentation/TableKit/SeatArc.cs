using System;
using UnityEngine;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// Precomputed opponent positions for 3 to 6 seats (GDD 14.3): the player sits at the bottom centre,
    /// so the next seat in play order is low on the left and the order reads left to right across the
    /// top. Positions are fractions of the opponent area (x across, y down) for the badge centre.
    /// </summary>
    public static class SeatArc
    {
        private static readonly Vector2[][] Arcs =
        {
            new Vector2[0],
            new[] { new Vector2(0.5f, 0.35f) },
            new[] { new Vector2(0.25f, 0.4f), new Vector2(0.75f, 0.4f) },
            new[] { new Vector2(0.14f, 0.62f), new Vector2(0.5f, 0.22f), new Vector2(0.86f, 0.62f) },
            new[] { new Vector2(0.12f, 0.7f), new Vector2(0.32f, 0.22f), new Vector2(0.68f, 0.22f), new Vector2(0.88f, 0.7f) },
            new[] { new Vector2(0.1f, 0.78f), new Vector2(0.2f, 0.32f), new Vector2(0.5f, 0.14f), new Vector2(0.8f, 0.32f), new Vector2(0.9f, 0.78f) },
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
