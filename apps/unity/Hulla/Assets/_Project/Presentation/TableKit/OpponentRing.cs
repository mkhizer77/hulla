using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hulla.Presentation.TableKit
{
    /// <summary>The opponents' badges on the seat arc for the table size (GDD 14.3).</summary>
    public sealed class OpponentRing
    {
        private readonly VisualElement _area;
        private readonly List<SeatBadge> _badges = new List<SeatBadge>();
        private int _seatCount;
        private long _mySeat;

        public OpponentRing(VisualElement area)
        {
            _area = area;
        }

        public IReadOnlyList<SeatBadge> Badges => _badges;

        /// <summary>Raised with the seat number when an opponent's badge is tapped.</summary>
        public event System.Action<long>? Tapped;

        public void Layout(int seatCount, long mySeat)
        {
            _seatCount = seatCount;
            _mySeat = mySeat;
            var needed = seatCount > 1 ? seatCount - 1 : 0;
            while (_badges.Count < needed)
            {
                var badge = new SeatBadge();
                badge.AddToClassList("seat--placed");
                var index = _badges.Count;
                badge.RegisterCallback<ClickEvent>(_ => Tapped?.Invoke(SeatAt(index)));
                _badges.Add(badge);
                _area.Add(badge);
            }

            for (var i = 0; i < _badges.Count; i++)
            {
                var badge = _badges[i];
                if (i >= needed)
                {
                    badge.style.display = DisplayStyle.None;
                    continue;
                }

                var at = SeatArc.Position(seatCount, i);
                badge.style.display = DisplayStyle.Flex;
                badge.style.left = Length.Percent(at.x * 100f);
                badge.style.top = Length.Percent(at.y * 100f);
            }
        }

        /// <summary>The seat number shown in opponent slot <paramref name="index"/>.</summary>
        public long SeatAt(int index) => _seatCount == 0 ? 0 : (_mySeat + 1 + index) % _seatCount;

        public SeatBadge? BadgeFor(long seat)
        {
            if (_seatCount <= 1 || seat == _mySeat)
            {
                return null;
            }

            var index = SeatArc.IndexOf(seat, _mySeat, _seatCount);
            return index < _badges.Count ? _badges[index] : null;
        }
    }
}
