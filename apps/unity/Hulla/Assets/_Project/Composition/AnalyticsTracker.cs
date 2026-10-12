using System;
using Hulla.Application.Analytics;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Protocol;
using UnityEngine;

namespace Hulla.Composition
{
    /// <summary>
    /// Turns what happens at a table into the events of GDD 23.3: round_start, move_played, thulla,
    /// pickup, escape and round_end. Counts and timings only; never cards, names or chat.
    /// </summary>
    public sealed class AnalyticsTracker : IDisposable
    {
        private readonly AnalyticsClient _analytics;
        private readonly TableClient _client;
        private float _roundStartedAt;
        private float _turnStartedAt;
        private bool _rematchVoted;

        public AnalyticsTracker(AnalyticsClient analytics, TableClient client)
        {
            _analytics = analytics;
            _client = client;
            _client.Applied += OnApplied;
        }

        public void RematchVoted() => _rematchVoted = true;

        public void Dispose() => _client.Applied -= OnApplied;

        private void OnApplied(ServerMessage message)
        {
            var model = _client.Model;
            var now = Time.realtimeSinceStartup;
            switch (message.Payload)
            {
                case RoundStart start:
                    _roundStartedAt = now;
                    _rematchVoted = false;
                    var humans = 0;
                    foreach (var seat in model.Seats)
                    {
                        humans += seat.Occupied ? 1 : 0;
                    }

                    _analytics.Track(AnalyticsEvents.RoundStart(ModeName(model.Mode), start.SeatCount, humans, 0));
                    break;
                case TurnStart turn when turn.Seat == model.MySeat:
                    _turnStartedAt = now;
                    break;
                case MoveApplied applied when applied.Seat == model.MySeat:
                    _analytics.Track(AnalyticsEvents.MovePlayed(
                        _client.LastPlayLegalCount,
                        (long)((now - _turnStartedAt) * 1000f),
                        auto: applied.Auto == true));
                    break;
                case TrickResolved resolved when resolved.ThullaBy != null && resolved.Picker != null:
                    _analytics.Track(AnalyticsEvents.Thulla(resolved.Cards.Count, resolved.ThullaBy, resolved.Picker, resolved.TrickIndex));
                    break;
                case Pickup pickup:
                    _analytics.Track(AnalyticsEvents.Pickup(pickup.Cards.Count));
                    break;
                case Escape escape when escape.Seat == model.MySeat:
                    _analytics.Track(AnalyticsEvents.Escape(escape.Rank));
                    break;
                case RoundEnd end:
                    _analytics.Track(AnalyticsEvents.RoundEnd(now - _roundStartedAt, end.BhabhiCards, _rematchVoted));
                    break;
            }
        }

        private static string ModeName(TableMode mode) => mode switch
        {
            TableMode.Public => "public",
            TableMode.Ranked => "ranked",
            _ => "private",
        };
    }
}
