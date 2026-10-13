using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Social
{
    /// <summary>The notification switches of GDD 10.10, kept on the server so they follow the account.</summary>
    public sealed class NotificationSettings
    {
        public bool TableInvite { get; set; } = true;

        public bool FriendRequest { get; set; } = true;

        public bool FriendOnline { get; set; } = true;

        public bool DailyReward { get; set; } = true;

        public bool SeasonEnd { get; set; } = true;

        /// <summary>No pushes overnight, except invites from friends.</summary>
        public bool QuietHours { get; set; } = true;

        public bool InvitesFromNonFriends { get; set; } = true;

        public NotificationSettings Copy() => (NotificationSettings)MemberwiseClone();
    }

    /// <summary>What table_invite did for each player asked.</summary>
    public sealed class InviteResult
    {
        public InviteResult(IReadOnlyList<string> sent, IReadOnlyDictionary<string, string> skipped, long remaining)
        {
            Sent = sent;
            Skipped = skipped;
            Remaining = remaining;
        }

        public IReadOnlyList<string> Sent { get; }

        /// <summary>User id to reason: self, blocked, muted or rate_limited.</summary>
        public IReadOnlyDictionary<string, string> Skipped { get; }

        /// <summary>Invites left this hour.</summary>
        public long Remaining { get; }
    }

    /// <summary>table_invite and push_settings_get/set (GDD 10.5, 10.10).</summary>
    public interface INotificationsService
    {
        Awaitable<Result<InviteResult>> InviteAsync(IReadOnlyList<string> userIds, CancellationToken cancellation);

        Awaitable<Result<NotificationSettings>> SettingsAsync(CancellationToken cancellation);

        Awaitable<Result<NotificationSettings>> SaveAsync(NotificationSettings settings, CancellationToken cancellation);
    }
}
