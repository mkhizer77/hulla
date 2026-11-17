using System;
using System.Threading;
using Hulla.Application.Progress;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The leaderboard screen: which board and scope are showing, and the page.</summary>
    public sealed class LeaderboardViewModel
    {
        private readonly ILeaderboardService _service;
        private int _request;

        public LeaderboardViewModel(ILeaderboardService service)
        {
            _service = service;
        }

        public event Action? Changed;

        public LeaderboardPeriod Period { get; private set; } = LeaderboardPeriod.Weekly;

        public bool Friends { get; private set; }

        /// <summary>Which game's board shows; Thulla until the player picks another.</summary>
        public GameFilter Game { get; private set; } = GameFilter.Thulla;

        public Awaitable ShowAsync(LeaderboardPeriod period, bool friends, GameFilter game, CancellationToken cancellation)
        {
            Game = game;
            return ShowAsync(period, friends, cancellation);
        }

        public LeaderboardPage? Page { get; private set; }

        public string? ErrorCode { get; private set; }

        public bool Loading { get; private set; }

        public Awaitable ShowAsync(LeaderboardPeriod period, bool friends, CancellationToken cancellation)
        {
            Period = period;
            Friends = friends;
            return RefreshAsync(cancellation);
        }

        /// <summary>Another account signed in: nothing of the last one stays.</summary>
        public void Reset()
        {
            _request++;
            Page = null;
            ErrorCode = null;
            Loading = false;
            Changed?.Invoke();
        }

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            // Only the latest tap's answer lands; a slower earlier one is dropped.
            var request = ++_request;
            Loading = true;
            Changed?.Invoke();
            var page = await _service.BoardAsync(Period, Friends, Game, cancellation);
            if (request != _request)
            {
                return;
            }

            Loading = false;
            ErrorCode = page.IsOk ? null : page.Error.Code;
            Page = page.IsOk ? page.Value : null;
            Changed?.Invoke();
        }
    }
}
