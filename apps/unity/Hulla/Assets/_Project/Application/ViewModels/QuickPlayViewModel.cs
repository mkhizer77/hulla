using System;
using System.Threading;
using Hulla.Application.Services;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>
    /// The Quick Play search (GDD 7.2, 9.6): strict on table size for 10 s, then any size; the server's
    /// matchmaker intervals accept a group of three at the 20 s mark. A Ranked search (GDD 7.5) is
    /// four seats only and widens its rating band every 10 s up to the cap. Time comes in through
    /// <see cref="Tick"/>, so the schedule is testable without a clock.
    /// </summary>
    public sealed class QuickPlayViewModel : IDisposable
    {
        public const float WidenAtSeconds = 10f;
        public const float WideAtSeconds = 15f;
        public const float StartAtSeconds = 20f;

        private readonly IMatchmaker _matchmaker;
        private CancellationToken _cancellation;

        public QuickPlayViewModel(IMatchmaker matchmaker)
        {
            _matchmaker = matchmaker;
            _matchmaker.Matched += OnMatched;
        }

        public enum Stage
        {
            Idle,
            Strict,
            Widened,
            Starting,
            Found,
            Failed,
        }

        public event Action? Changed;

        public event Action<TableTicket>? Found;

        public Stage Current { get; private set; } = Stage.Idle;

        public int Seats { get; private set; } = 4;

        public float Elapsed { get; private set; }

        public string? ErrorCode { get; private set; }

        /// <summary>The search is a Ranked one; <see cref="Band"/> is how far it looks either side of the rating.</summary>
        public bool Ranked { get; private set; }

        public int Band { get; private set; }

        private int _widenBy;
        private int _maxBand;

        public bool Searching => Current == Stage.Strict || Current == Stage.Widened || Current == Stage.Starting;

        public async Awaitable StartAsync(int seats, CancellationToken cancellation)
        {
            Ranked = false;
            Seats = Math.Clamp(seats, 3, 6);
            Elapsed = 0f;
            ErrorCode = null;
            _cancellation = cancellation;
            Current = Stage.Strict;
            Changed?.Invoke();
            var started = await _matchmaker.SearchAsync(Seats, false, cancellation);
            if (!started.IsOk && Current == Stage.Strict)
            {
                Fail(started.Error.Code);
            }
        }

        public async Awaitable StartRankedAsync(int band, int widenBy, int maxBand, CancellationToken cancellation)
        {
            Ranked = true;
            Seats = 4;
            Band = band;
            _widenBy = Math.Max(0, widenBy);
            _maxBand = Math.Max(band, maxBand);
            Elapsed = 0f;
            ErrorCode = null;
            _cancellation = cancellation;
            Current = Stage.Strict;
            Changed?.Invoke();
            var started = await _matchmaker.SearchRankedAsync(Band, cancellation);
            if (!started.IsOk && Current == Stage.Strict)
            {
                Fail(started.Error.Code);
            }
        }

        public void Tick(float deltaSeconds)
        {
            if (!Searching)
            {
                return;
            }

            var before = Elapsed;
            Elapsed += deltaSeconds;
            if (Ranked)
            {
                // Every 10 s the band grows until it reaches the cap; no smaller tables in Ranked.
                var steps = (int)(Elapsed / WidenAtSeconds) - (int)(before / WidenAtSeconds);
                if (steps > 0 && Band < _maxBand)
                {
                    Band = Math.Min(_maxBand, Band + steps * _widenBy);
                    Current = Stage.Widened;
                    WidenRanked();
                }

                Changed?.Invoke();
                return;
            }

            if (before < WidenAtSeconds && Elapsed >= WidenAtSeconds)
            {
                Current = Stage.Widened;
                Widen();
            }

            if (before < StartAtSeconds && Elapsed >= StartAtSeconds)
            {
                Current = Stage.Starting;
            }

            Changed?.Invoke();
        }

        public async Awaitable CancelAsync()
        {
            if (!Searching)
            {
                return;
            }

            Current = Stage.Idle;
            Changed?.Invoke();
            await _matchmaker.CancelAsync();
        }

        public void Dispose() => _matchmaker.Matched -= OnMatched;

        private async void Widen()
        {
            var widened = await _matchmaker.SearchAsync(Seats, true, _cancellation);
            if (!widened.IsOk && Searching)
            {
                Fail(widened.Error.Code);
            }
        }

        private async void WidenRanked()
        {
            var widened = await _matchmaker.SearchRankedAsync(Band, _cancellation);
            if (!widened.IsOk && Searching)
            {
                Fail(widened.Error.Code);
            }
        }

        private void Fail(string code)
        {
            ErrorCode = code;
            Current = Stage.Failed;
            Changed?.Invoke();
        }

        private void OnMatched(TableTicket ticket)
        {
            if (!Searching)
            {
                return;
            }

            Current = Stage.Found;
            Changed?.Invoke();
            Found?.Invoke(ticket);
        }
    }
}
