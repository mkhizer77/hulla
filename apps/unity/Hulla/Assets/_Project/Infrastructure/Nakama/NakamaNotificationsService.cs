using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Social;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    public sealed class NakamaNotificationsService : INotificationsService
    {
        private readonly NakamaConnection _connection;

        public NakamaNotificationsService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<InviteResult>> InviteAsync(IReadOnlyList<string> userIds, CancellationToken cancellation)
        {
            var body = await RpcAsync("table_invite", new JObject { ["userIds"] = new JArray(userIds) }, cancellation);
            if (!body.IsOk)
            {
                return Result<InviteResult>.Fail(body.Error);
            }

            var sent = new List<string>();
            if (body.Value["sent"] is JArray list)
            {
                foreach (var id in list)
                {
                    sent.Add((string)id!);
                }
            }

            var skipped = new Dictionary<string, string>();
            if (body.Value["skipped"] is JObject map)
            {
                foreach (var pair in map)
                {
                    skipped[pair.Key] = (string?)pair.Value ?? "";
                }
            }

            return Result<InviteResult>.Ok(new InviteResult(sent, skipped, (long?)body.Value["remaining"] ?? 0));
        }

        public async Awaitable<Result<NotificationSettings>> SettingsAsync(CancellationToken cancellation)
        {
            var body = await RpcAsync("push_settings_get", new JObject(), cancellation);
            return body.IsOk ? Result<NotificationSettings>.Ok(Read(body.Value["settings"])) : Result<NotificationSettings>.Fail(body.Error);
        }

        public async Awaitable<Result<NotificationSettings>> SaveAsync(NotificationSettings settings, CancellationToken cancellation)
        {
            var input = new JObject
            {
                ["types"] = new JObject
                {
                    ["table_invite"] = settings.TableInvite,
                    ["friend_request"] = settings.FriendRequest,
                    ["friend_online"] = settings.FriendOnline,
                    ["daily_reward"] = settings.DailyReward,
                    ["season_end"] = settings.SeasonEnd,
                },
                ["quietHours"] = settings.QuietHours,
                ["invitesFromNonFriends"] = settings.InvitesFromNonFriends,
            };
            var body = await RpcAsync("push_settings_set", input, cancellation);
            return body.IsOk ? Result<NotificationSettings>.Ok(Read(body.Value["settings"])) : Result<NotificationSettings>.Fail(body.Error);
        }

        private static NotificationSettings Read(JToken? s) => new NotificationSettings
        {
            TableInvite = (bool?)s?["types"]?["table_invite"] ?? true,
            FriendRequest = (bool?)s?["types"]?["friend_request"] ?? true,
            FriendOnline = (bool?)s?["types"]?["friend_online"] ?? true,
            DailyReward = (bool?)s?["types"]?["daily_reward"] ?? true,
            SeasonEnd = (bool?)s?["types"]?["season_end"] ?? true,
            QuietHours = (bool?)s?["quietHours"] ?? true,
            InvitesFromNonFriends = (bool?)s?["invitesFromNonFriends"] ?? true,
        };

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
                    : Result<JObject>.Fail((string?)body["error"]?["code"] ?? "network.unavailable");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"{id}: {error.Message}");
                return Result<JObject>.Fail(ServerErrors.CodeOf(error, "network.unavailable"), error.GetType().Name);
            }
        }
    }
}
