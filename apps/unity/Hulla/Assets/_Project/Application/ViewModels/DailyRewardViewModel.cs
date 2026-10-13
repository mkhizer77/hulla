using System;
using System.Threading;
using Hulla.Application.Progress;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The daily reward card on Home (GDD 12.6): today's reward with Claim, then tomorrow's.</summary>
    public sealed class DailyRewardViewModel
    {
        private readonly IDailyRewardService _service;

        public DailyRewardViewModel(IDailyRewardService service)
        {
            _service = service;
        }

        public event Action? Changed;

        /// <summary>Null until the server answers (or when it has no calendar): the card stays hidden.</summary>
        public DailyRewardStatus? Status { get; private set; }

        public bool Claiming { get; private set; }

        /// <summary>What the last claim paid, for the "Claimed" line; null before a claim this session.</summary>
        public DailyRewardDay? Claimed { get; private set; }

        public async Awaitable RefreshAsync(int tzOffsetMinutes, CancellationToken cancellation)
        {
            var status = await _service.StatusAsync(tzOffsetMinutes, cancellation);
            if (status.IsOk)
            {
                Status = status.Value;
                Changed?.Invoke();
            }
        }

        /// <returns>True when the reward was paid (or had been already today).</returns>
        public async Awaitable<bool> ClaimAsync(CancellationToken cancellation)
        {
            if (Claiming || Status == null || !Status.Claimable)
            {
                return false;
            }

            Claiming = true;
            Changed?.Invoke();
            var paid = Status.Today;
            var status = await _service.ClaimAsync(cancellation);
            Claiming = false;
            if (status.IsOk)
            {
                Status = status.Value;
                Claimed = paid;
            }

            Changed?.Invoke();
            return status.IsOk;
        }
    }
}
