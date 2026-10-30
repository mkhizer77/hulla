using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Social;
using Hulla.Domain;
using Hulla.Protocol;
using Nakama;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>The social RPCs (profile_get, profile_update, friend_add_by_code, favourite_set, social_overview) and Nakama's friends.</summary>
    public sealed class NakamaSocialService : ISocialService
    {
        private readonly NakamaConnection _connection;

        public NakamaSocialService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<Profile>> MyProfileAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("profile_get", new JObject(), cancellation);
            return ProfileFrom(body);
        }

        public async Awaitable<Result<Profile>> ProfileAsync(string userId, CancellationToken cancellation)
        {
            var body = await RpcAsync("profile_get", new JObject { ["userId"] = userId }, cancellation);
            return ProfileFrom(body);
        }

        public async Awaitable<Result<Profile>> UpdateProfileAsync(ProfileUpdate update, CancellationToken cancellation)
        {
            var body = await RpcAsync("profile_update", JObject.FromObject(update, ProtocolJson.CreateSerializer()), cancellation);
            return ProfileFrom(body);
        }

        public async Awaitable<Result<IReadOnlyList<FriendEntry>>> FriendsAsync(CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<IReadOnlyList<FriendEntry>>.Fail("session.not_signed_in");
            }

            try
            {
                var favourites = new HashSet<string>();
                var overview = await RpcAsync("social_overview", new JObject(), cancellation);
                if (overview.IsOk && overview.Value["favourites"] is JArray pinned)
                {
                    foreach (var id in pinned)
                    {
                        favourites.Add((string)id!);
                    }
                }

                var list = await _connection.Client.ListFriendsAsync(session, null, 100, null, null, cancellation);
                var friends = new List<FriendEntry>();
                foreach (var friend in list.Friends)
                {
                    var user = friend.User;
                    var name = string.IsNullOrEmpty(user.DisplayName) ? user.Username : user.DisplayName;
                    friends.Add(new FriendEntry(user.Id, name, user.Username, user.Online, (FriendState)friend.State, favourites.Contains(user.Id)));
                }

                return Result<IReadOnlyList<FriendEntry>>.Ok(friends);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"friends: {error.Message}");
                return Result<IReadOnlyList<FriendEntry>>.Fail("social.unavailable", error.GetType().Name);
            }
        }

        public async Awaitable<Result<IReadOnlyList<FriendEntry>>> RecentAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("social_overview", new JObject(), cancellation);
            if (!body.IsOk)
            {
                return Result<IReadOnlyList<FriendEntry>>.Fail(body.Error);
            }

            var recent = new List<FriendEntry>();
            if (body.Value["recent"] is JArray cards)
            {
                foreach (var card in cards)
                {
                    var id = (string?)card["userId"];
                    if (id == null)
                    {
                        continue;
                    }

                    var friend = (bool?)card["friend"] == true;
                    recent.Add(new FriendEntry(
                        id,
                        (string?)card["displayName"] ?? (string?)card["handle"] ?? "?",
                        (string?)card["handle"],
                        false,
                        friend ? FriendState.Friend : (FriendState?)null,
                        (bool?)card["favourite"] == true,
                        (long?)card["rounds"] ?? 0));
                }
            }

            return Result<IReadOnlyList<FriendEntry>>.Ok(recent);
        }

        public async Awaitable<Result<bool>> AddByCodeAsync(string friendCode, CancellationToken cancellation)
        {
            var code = friendCode.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
            var body = await RpcAsync("friend_add_by_code", new JObject { ["friendCode"] = code }, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error);
        }

        public Awaitable<Result<bool>> AddAsync(string userId, CancellationToken cancellation) =>
            FriendCallAsync((client, session) => client.AddFriendsAsync(session, new[] { userId }, null, null, null, cancellation));

        public Awaitable<Result<bool>> RemoveAsync(string userId, CancellationToken cancellation) =>
            FriendCallAsync((client, session) => client.DeleteFriendsAsync(session, new[] { userId }, null, null, cancellation));

        public async Awaitable<Result<bool>> BlockAsync(string userId, CancellationToken cancellation, bool blocked = true)
        {
            var body = await RpcAsync("block_set", new JObject { ["userId"] = userId, ["blocked"] = blocked }, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error);
        }

        public async Awaitable<Result<bool>> ReportAsync(string userId, string reason, string? tableId, string? roundId, CancellationToken cancellation)
        {
            var input = new JObject { ["userId"] = userId, ["reason"] = reason };
            if (!string.IsNullOrEmpty(tableId))
            {
                input["tableId"] = tableId;
            }

            if (!string.IsNullOrEmpty(roundId))
            {
                input["roundId"] = roundId;
            }

            var body = await RpcAsync("report_create", input, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error);
        }

        public async Awaitable<Result<bool>> SetFavouriteAsync(string userId, bool pinned, CancellationToken cancellation)
        {
            var body = await RpcAsync("favourite_set", new JObject { ["userId"] = userId, ["pinned"] = pinned }, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error);
        }

        public async Awaitable<Result<IReadOnlyList<FriendPresence>>> PresenceAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("table_list_friends", new JObject(), cancellation);
            if (!body.IsOk)
            {
                return Result<IReadOnlyList<FriendPresence>>.Fail(body.Error);
            }

            var friends = new List<FriendPresence>();
            if (body.Value["friends"] is JArray list)
            {
                foreach (var item in list)
                {
                    var id = (string?)item["userId"];
                    if (id == null)
                    {
                        continue;
                    }

                    var table = item["table"] as JObject;
                    friends.Add(new FriendPresence(
                        id,
                        (string?)item["displayName"] ?? (string?)item["handle"] ?? "?",
                        StateOf((string?)item["state"]),
                        (bool?)item["favourite"] == true,
                        (string?)table?["code"],
                        (long?)table?["open"] ?? 0,
                        (bool?)table?["joinable"] == true,
                        (bool?)table?["watchable"] == true));
                }
            }

            return Result<IReadOnlyList<FriendPresence>>.Ok(friends);
        }

        public async Awaitable<Result<bool>> SetAwayAsync(bool away, CancellationToken cancellation)
        {
            var body = await RpcAsync("presence_set", new JObject { ["away"] = away }, cancellation);
            return body.IsOk ? Result<bool>.Ok(true) : Result<bool>.Fail(body.Error);
        }

        public async Awaitable<Result<IReadOnlyList<string>>> MutesAsync(CancellationToken cancellation) =>
            MutesFrom(await RpcAsync("mutes_get", new JObject(), cancellation));

        public async Awaitable<Result<IReadOnlyList<string>>> SetMuteAsync(string userId, bool muted, CancellationToken cancellation) =>
            MutesFrom(await RpcAsync("mute_set", new JObject { ["userId"] = userId, ["muted"] = muted }, cancellation));

        private static Result<IReadOnlyList<string>> MutesFrom(Result<JObject> body)
        {
            if (!body.IsOk)
            {
                return Result<IReadOnlyList<string>>.Fail(body.Error);
            }

            var ids = new List<string>();
            if (body.Value["mutes"] is JArray list)
            {
                foreach (var id in list)
                {
                    ids.Add((string)id!);
                }
            }

            return Result<IReadOnlyList<string>>.Ok(ids);
        }

        private static PresenceState StateOf(string? state) => state switch
        {
            "lobby" => PresenceState.Lobby,
            "round" => PresenceState.Round,
            "online" => PresenceState.Online,
            "away" => PresenceState.Away,
            "hidden" => PresenceState.Hidden,
            _ => PresenceState.Offline,
        };

        private static Result<Profile> ProfileFrom(Result<JObject> body)
        {
            if (!body.IsOk)
            {
                return Result<Profile>.Fail(body.Error);
            }

            var profile = body.Value["profile"]?.ToObject<Profile>(ProtocolJson.CreateSerializer());
            return profile == null ? Result<Profile>.Fail("profile.not_found") : Result<Profile>.Ok(profile);
        }

        private async Awaitable<Result<bool>> FriendCallAsync(Func<IClient, ISession, System.Threading.Tasks.Task> call)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<bool>.Fail("session.not_signed_in");
            }

            try
            {
                await call(_connection.Client, session);
                return Result<bool>.Ok(true);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"friends: {error.Message}");
                return Result<bool>.Fail("social.unavailable", error.GetType().Name);
            }
        }

        /// <summary>An RPC whose answer is <c>{ok: true, ...}</c> or <c>{ok: false, error: {code}}</c>.</summary>
        private async Awaitable<Result<JObject>> RpcAsync(string id, JObject input, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<JObject>.Fail("session.not_signed_in");
            }

            try
            {
                var response = await _connection.Client.RpcAsync(session, id, input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                return (bool?)body["ok"] == true
                    ? Result<JObject>.Ok(body)
                    : Result<JObject>.Fail((string?)body["error"]?["code"] ?? "social.unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{id}: {error.Message}");
                return Result<JObject>.Fail("social.unavailable", error.GetType().Name);
            }
        }
    }
}
