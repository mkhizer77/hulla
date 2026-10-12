using System;
using System.Threading;
using Hulla.Application.Progress;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The player's level, XP and coins for Home and the results screen.</summary>
    public sealed class ProgressViewModel
    {
        private readonly IProgressService _progress;

        public ProgressViewModel(IProgressService progress)
        {
            _progress = progress;
        }

        public event Action? Changed;

        public ProgressInfo? Info { get; private set; }

        public long Coins { get; private set; }

        public long Gems { get; private set; }

        public async Awaitable RefreshAsync(int tzOffsetMinutes, CancellationToken cancellation)
        {
            var info = await _progress.ProgressAsync(tzOffsetMinutes, cancellation);
            var balances = await _progress.BalancesAsync(cancellation);
            if (info.IsOk)
            {
                Info = info.Value;
            }

            if (balances.IsOk)
            {
                Coins = balances.Value.Coins;
                Gems = balances.Value.Gems;
            }

            Changed?.Invoke();
        }

        /// <summary>The reward for this round once the server has written it; null until then.</summary>
        public RewardInfo? RewardFor(string roundId) => Info?.LastReward?.RoundId == roundId ? Info.LastReward : null;
    }
}
