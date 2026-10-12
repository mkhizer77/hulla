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

        /// <summary>Friends, requests both ways and blocks, with favourites marked.</summary>
        Awaitable<Result<IReadOnlyList<FriendEntry>>> FriendsAsync(CancellationToken cancellation);

        /// <summary>The last people played with, newest first.</summary>
        Awaitable<Result<IReadOnlyList<FriendEntry>>> RecentAsync(CancellationToken cancellation);

        Awaitable<Result<bool>> AddByCodeAsync(string friendCode, CancellationToken cancellation);

        /// <summary>Sends a request, or accepts one that came in.</summary>
        Awaitable<Result<bool>> AddAsync(string userId, CancellationToken cancellation);

        /// <summary>Removes a friend, declines a request or cancels one sent.</summary>
        Awaitable<Result<bool>> RemoveAsync(string userId, CancellationToken cancellation);

        Awaitable<Result<bool>> BlockAsync(string userId, CancellationToken cancellation);

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
