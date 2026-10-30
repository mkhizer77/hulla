using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Social;
using Hulla.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>friend_suggestions (people you may know): numbers and Facebook ids in, matching players out.</summary>
    public sealed class NakamaFriendSuggestions : IFriendSuggestions
    {
        private readonly NakamaConnection _connection;

        public NakamaFriendSuggestions(NakamaConnection connection)
        {
            _connection = connection;
        }

        public async Awaitable<Result<IReadOnlyList<FriendSuggestion>>> FindAsync(IReadOnlyList<string> phones, IReadOnlyList<string> facebookIds, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<IReadOnlyList<FriendSuggestion>>.Fail("session.not_signed_in");
            }

            try
            {
                var input = new JObject { ["phones"] = new JArray(phones), ["facebookIds"] = new JArray(facebookIds) };
                var response = await _connection.Client.RpcAsync(session, "friend_suggestions", input.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                var body = JObject.Parse(response.Payload);
                if ((bool?)body["ok"] != true)
                {
                    return Result<IReadOnlyList<FriendSuggestion>>.Fail((string?)body["error"]?["code"] ?? "social.unavailable");
                }

                var list = new List<FriendSuggestion>();
                foreach (var item in body["suggestions"] as JArray ?? new JArray())
                {
                    var id = (string?)item["userId"];
                    if (id == null)
                    {
                        continue;
                    }

                    var source = (string?)item["source"] == "facebook" ? SuggestionSource.Facebook : SuggestionSource.Contacts;
                    list.Add(new FriendSuggestion(id, (string?)item["displayName"] ?? "", (string?)item["avatar"], source));
                }

                return Result<IReadOnlyList<FriendSuggestion>>.Ok(list);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"friend_suggestions: {error.Message}");
                return Result<IReadOnlyList<FriendSuggestion>>.Fail("social.unavailable", error.GetType().Name);
            }
        }
    }
}
