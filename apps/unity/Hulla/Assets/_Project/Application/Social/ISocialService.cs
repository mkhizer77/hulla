using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using Hulla.Protocol;
using UnityEngine;

namespace Hulla.Application.Social
{
    /// <summary>
    /// Profiles and friends (GDD 10.1 to 10.3): the profile RPCs, friend codes, favourites and recent
    /// players from the server, and requests, accepts, removals and blocks through Nakama's friend API.
    /// </summary>
    public interface ISocialService
    {
        Awaitable<Result<Profile>> MyProfileAsync(CancellationToken cancellation);

        Awaitable<Result<Profile>> UpdateProfileAsync(ProfileUpdate update, CancellationToken cancellation);

        /// <summary>Another player's public profile (profile_get with their id), as their privacy allows.</summary>
        Awaitable<Result<Profile>> ProfileAsync(string userId, CancellationToken cancellation);

        /// <summary>Friends, requests both ways and blocks, with favourites marked.</summary>
        Awaitable<Result<IReadOnlyList<FriendEntry>>> FriendsAsync(CancellationToken cancellation);

        /// <summary>The last people played with, newest first.</summary>
        Awaitable<Result<IReadOnlyList<FriendEntry>>> RecentAsync(CancellationToken cancellation);

        Awaitable<Result<bool>> AddByCodeAsync(string friendCode, CancellationToken cancellation);

        /// <summary>Sends a request, or accepts one that came in.</summary>
        Awaitable<Result<bool>> AddAsync(string userId, CancellationToken cancellation);

        /// <summary>Removes a friend, declines a request or cancels one sent.</summary>
        Awaitable<Result<bool>> RemoveAsync(string userId, CancellationToken cancellation);

        /// <summary>Blocks or unblocks (block_set): both ways, in matchmaking, and on the friend graph (GDD 10.11).</summary>
        Awaitable<Result<bool>> BlockAsync(string userId, CancellationToken cancellation, bool blocked = true);

        /// <summary>Reports a player with a reason (GDD 10.11); the server attaches the table's recent chat.</summary>
        Awaitable<Result<bool>> ReportAsync(string userId, string reason, string? tableId, string? roundId, CancellationToken cancellation);

        Awaitable<Result<bool>> SetFavouriteAsync(string userId, bool pinned, CancellationToken cancellation);

        /// <summary>Friends with presence and their tables, joinable lobbies first (table_list_friends).</summary>
        Awaitable<Result<IReadOnlyList<FriendPresence>>> PresenceAsync(CancellationToken cancellation);

        /// <summary>Shows the player as Away while the app is in the background (presence_set).</summary>
        Awaitable<Result<bool>> SetAwayAsync(bool away, CancellationToken cancellation);

        /// <summary>The players this player has muted, kept across devices (mutes_get).</summary>
        Awaitable<Result<IReadOnlyList<string>>> MutesAsync(CancellationToken cancellation);

        /// <summary>Mutes or unmutes a player and returns the new list (mute_set).</summary>
        Awaitable<Result<IReadOnlyList<string>>> SetMuteAsync(string userId, bool muted, CancellationToken cancellation);
    }
}
