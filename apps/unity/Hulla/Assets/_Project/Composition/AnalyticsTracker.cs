using System;
using Hulla.Application.Analytics;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Protocol;
using UnityEngine;

namespace Hulla.Composition
{
    /// <summary>
    /// Turns what happens at a table into the events of GDD 23.3 for both games: round_start, the
    /// per-move events (move_played, thulla, pickup, escape) and a round_end that carries the round's
    /// totals, since Amplitude gets only that one (docs/design/analytics-events.md). Every event from
    /// the table carries the game. Counts and timings only; never cards, names or chat.
    /// </summary>
    public sealed class AnalyticsTracker : IDisposable
    {
        private readonly AnalyticsClient _analytics;
        private readonly TableClient _client;
        private readonly Func<string, bool> _isFriend;
        private readonly float _enteredAt;
        private float _roundStartedAt;
        private float _turnStartedAt;
        private bool _rematchVoted;
        private bool _inRound;
        private bool _voice;
        private int _rounds;
        private int _seats;
        private int _humans;
        private int _bots;
        private int _friends;
        private int _moves;
        private int _timeouts;
        private int _thullasGiven;
        private int _thullasTaken;
        private int _cardsPicked;
        private int _chats;

        /// <param name="isFriend">Whether a player id is one of this player's friends, for the friend-round count (GDD 23.1).</param>
        public AnalyticsTracker(AnalyticsClient analytics, TableClient client, Func<string, bool> isFriend)
        {
            _analytics = analytics;
            _client = client;
            _isFriend = isFriend;
            _enteredAt = Time.realtimeSinceStartup;
            _client.Applied += OnApplied;
        }

        public void RematchVoted() => _rematchVoted = true;

        /// <summary>The player left the table, or was taken off it: where in the evening that happened.</summary>
        public void Left(string reason)
        {
            var phase = _inRound ? "round" : _rounds > 0 ? "between_rounds" : "lobby";
            _analytics.Track(AnalyticsEvents.TableLeave(phase, reason, _rounds, Time.realtimeSinceStartup - _enteredAt));
        }

        public void Dispose()
        {
            _client.Applied -= OnApplied;
            _analytics.SetContext("game", null);
        }

        private void OnApplied(ServerMessage message)
        {
            var model = _client.Model;
            var now = Time.realtimeSinceStartup;
            _analytics.SetContext("game", model.IsRung ? "rung" : "thulla");
            switch (message.Payload)
            {
                case RoundStart start:
                    Started(model, (int)start.SeatCount, now);
                    break;
                case RungRoundStart:
                    Started(model, RungModel.Seats, now);
                    break;
                case TurnStart turn when turn.Seat == model.MySeat:
                    _turnStartedAt = now;
                    break;
                case MoveApplied applied when applied.Seat == model.MySeat:
                    Moved(_client.LastPlayLegalCount, applied.Auto == true, now);
                    break;
                case RungCardPlayed played when played.Seat == model.MySeat:
                    Moved(_client.LastPlayLegalCount, played.Auto, now);
                    break;
                case TrickResolved resolved when resolved.ThullaBy != null && resolved.Picker != null:
                    _thullasGiven += resolved.ThullaBy == model.MySeat ? 1 : 0;
                    _thullasTaken += resolved.Picker == model.MySeat ? 1 : 0;
                    _analytics.Track(AnalyticsEvents.Thulla(resolved.Cards.Count, resolved.ThullaBy, resolved.Picker, resolved.TrickIndex));
                    break;
                case Pickup pickup:
                    _cardsPicked += pickup.Seat == model.MySeat ? pickup.Cards.Count : 0;
                    _analytics.Track(AnalyticsEvents.Pickup(pickup.Cards.Count));
                    break;
                case QuickChatSent chat when chat.Seat == model.MySeat:
                    _chats++;
                    break;
                case EmoteSent emote when emote.Seat == model.MySeat:
                    _chats++;
                    break;
                case Escape escape when escape.Seat == model.MySeat:
                    _analytics.Track(AnalyticsEvents.Escape(escape.Rank));
                    break;
                case RoundEnd end:
                    long? rank = null;
                    bool? lost = null;
                    if (model.MySeat is long mine)
                    {
                        lost = end.BhabhiSeat == mine;
                        var place = end.EscapeOrder.IndexOf(mine);
                        rank = lost == true ? _seats : place >= 0 ? place + 1 : (long?)null;
                    }

                    Ended(model, now, end.BhabhiCards, rank, lost, null);
                    break;
                case RungRoundEnd deal:
                    Ended(
                        model,
                        now,
                        null,
                        null,
                        model.MySeat is long seat ? RungModel.TeamOf(seat) != deal.WinnerTeam : (bool?)null,
                        deal.Kind != RungOutcomeKind.Win);
                    break;
            }
        }

        private void Started(TableModel model, int seats, float now)
        {
            _roundStartedAt = now;
            _turnStartedAt = now;
            _rematchVoted = false;
            _inRound = true;
            _seats = seats;
            _humans = 0;
            _bots = 0;
            _friends = 0;
            _moves = 0;
            _timeouts = 0;
            _thullasGiven = 0;
            _thullasTaken = 0;
            _cardsPicked = 0;
            _chats = 0;
            _voice = MicOn(model);
            foreach (var seat in model.Seats)
            {
                if (!seat.Occupied)
                {
                    continue;
                }

                if (seat.Bot)
                {
                    _bots++;
                    continue;
                }

                _humans++;
                if (seat.Seat != model.MySeat && seat.PlayerId != null && _isFriend(seat.PlayerId))
                {
                    _friends++;
                }
            }

            _analytics.Track(AnalyticsEvents.RoundStart(ModeName(model.Mode), seats, _humans, _bots, friends: _friends));
        }

        private void Moved(int legalCount, bool auto, float now)
        {
            _moves++;
            _timeouts += auto ? 1 : 0;
            _analytics.Track(AnalyticsEvents.MovePlayed(legalCount, (long)((now - _turnStartedAt) * 1000f), auto: auto));
            _turnStartedAt = now;
        }

        private void Ended(TableModel model, float now, long? bhabhiCards, long? rank, bool? lost, bool? court)
        {
            _inRound = false;
            _rounds++;
            var playing = model.MySeat != null;
            _analytics.Track(AnalyticsEvents.RoundEnd(
                now - _roundStartedAt,
                bhabhiCards,
                _rematchVoted,
                seats: _seats,
                humans: _humans,
                bots: _bots,
                friends: _friends,
                rank: rank,
                lost: lost,
                court: court,
                moves: playing ? _moves : (long?)null,
                timeouts: playing ? _timeouts : (long?)null,
                thullasGiven: playing && !model.IsRung ? _thullasGiven : (long?)null,
                thullasTaken: playing && !model.IsRung ? _thullasTaken : (long?)null,
                cardsPicked: playing && !model.IsRung ? _cardsPicked : (long?)null,
                chats: playing ? _chats : (long?)null,
                voice: playing ? _voice || MicOn(model) : (bool?)null));
        }

        private static bool MicOn(TableModel model)
        {
            foreach (var seat in model.Seats)
            {
                if (seat.Seat == model.MySeat)
                {
                    return seat.Mic;
                }
            }

            return false;
        }

        private static string ModeName(TableMode mode) => mode switch
        {
            TableMode.Public => "public",
            TableMode.Ranked => "ranked",
            _ => "private",
        };
    }
}
