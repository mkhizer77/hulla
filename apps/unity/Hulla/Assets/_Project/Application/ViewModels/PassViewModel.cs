using System;
using System.Threading;
using Hulla.Application.Progress;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The season pass screen: the tiers, claims, and the no-season state between seasons.</summary>
    public sealed class PassViewModel
    {
        private readonly IPassService _service;

        public PassViewModel(IPassService service)
        {
            _service = service;
        }

        public event Action? Changed;

        public PassInfo? Info { get; private set; }

        /// <summary>True once the server has said no season is running.</summary>
        public bool NoSeason { get; private set; }

        public string? ErrorCode { get; private set; }

        public bool Busy { get; private set; }

        /// <summary>Reached tiers with an unclaimed reward on a track the player has.</summary>
        public int Claimable
        {
            get
            {
                var count = 0;
                if (Info == null)
                {
                    return 0;
                }

                foreach (var tier in Info.Tiers)
                {
                    if (!tier.Reached)
                    {
                        continue;
                    }

                    count += !tier.Free.Claimed && !tier.Free.Empty ? 1 : 0;
                    count += Info.Premium && !tier.Premium.Claimed && !tier.Premium.Empty ? 1 : 0;
                }

                return count;
            }
        }

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            var pass = await _service.PassAsync(cancellation);
            ErrorCode = pass.IsOk ? null : pass.Error.Code;
            if (pass.IsOk)
            {
                Info = pass.Value;
                NoSeason = pass.Value == null;
            }

            Changed?.Invoke();
        }

        public async Awaitable<bool> ClaimAsync(long tier, bool premium, CancellationToken cancellation)
        {
            if (Busy)
            {
                return false;
            }

            Busy = true;
            Changed?.Invoke();
            var claimed = await _service.ClaimAsync(tier, premium, cancellation);
            Busy = false;
            await RefreshAsync(cancellation);
            if (!claimed.IsOk)
            {
                ErrorCode = claimed.Error.Code;
                Changed?.Invoke();
            }

            return claimed.IsOk;
        }
    }
}
