using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Progress;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>Three dailies and five weeklies, claims and the one free daily re-roll (GDD 12.5).</summary>
    public sealed class MissionsViewModel
    {
        private readonly IMissionsService _missions;
        private readonly List<MissionInfo> _daily = new List<MissionInfo>();
        private readonly List<MissionInfo> _weekly = new List<MissionInfo>();

        public MissionsViewModel(IMissionsService missions)
        {
            _missions = missions;
        }

        public event Action? Changed;

        public IReadOnlyList<MissionInfo> Daily => _daily;

        public IReadOnlyList<MissionInfo> Weekly => _weekly;

        public bool RerollAvailable { get; private set; }

        public string? ErrorCode { get; private set; }

        public int Claimable
        {
            get
            {
                var count = 0;
                foreach (var m in _daily)
                {
                    count += m.Claimable ? 1 : 0;
                }

                foreach (var m in _weekly)
                {
                    count += m.Claimable ? 1 : 0;
                }

                return count;
            }
        }

        public async Awaitable RefreshAsync(int tzOffsetMinutes, CancellationToken cancellation)
        {
            var result = await _missions.MissionsAsync(tzOffsetMinutes, cancellation);
            if (!result.IsOk)
            {
                ErrorCode = result.Error.Code;
                Changed?.Invoke();
                return;
            }

            ErrorCode = null;
            _daily.Clear();
            _weekly.Clear();
            foreach (var mission in result.Value.Missions)
            {
                (mission.Daily ? _daily : _weekly).Add(mission);
            }

            RerollAvailable = result.Value.RerollAvailable;
            Changed?.Invoke();
        }

        public async Awaitable ClaimAsync(string missionId, int tzOffsetMinutes, CancellationToken cancellation)
        {
            var claimed = await _missions.ClaimAsync(missionId, cancellation);
            ErrorCode = claimed.IsOk ? null : claimed.Error.Code;
            await RefreshAsync(tzOffsetMinutes, cancellation);
        }

        public async Awaitable RerollAsync(string missionId, int tzOffsetMinutes, CancellationToken cancellation)
        {
            var rerolled = await _missions.RerollAsync(missionId, cancellation);
            ErrorCode = rerolled.IsOk ? null : rerolled.Error.Code;
            await RefreshAsync(tzOffsetMinutes, cancellation);
        }
    }
}
