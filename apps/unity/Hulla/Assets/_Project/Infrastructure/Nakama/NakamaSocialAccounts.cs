using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Nakama;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>
    /// Apple, Google and Facebook on Nakama's own account links (GDD 10.2, #38). Linking keeps this
    /// account; when another account already holds the provider, the player can switch to it, and a merge
    /// code first hands it this phone's progress, as the phone sign-in does (account_merge, OQ-015).
    /// </summary>
    public sealed class NakamaSocialAccounts : ISocialAccounts
    {
        private static readonly SocialProvider[] Order = { SocialProvider.Apple, SocialProvider.Google, SocialProvider.Facebook };

        private readonly NakamaConnection _connection;
        private readonly NakamaSessionService _sessions;
        private readonly ISocialSignIn _signIn;

        public NakamaSocialAccounts(NakamaConnection connection, NakamaSessionService sessions, ISocialSignIn signIn)
        {
            _connection = connection;
            _sessions = sessions;
            _signIn = signIn;
            var available = new List<SocialProvider>();
            foreach (var provider in Order)
            {
                if (signIn.Supports(provider))
                {
                    available.Add(provider);
                }
            }

            Available = available;
        }

        public IReadOnlyList<SocialProvider> Available { get; }

        public async Awaitable<Result<IReadOnlyCollection<SocialProvider>>> LinkedAsync(CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<IReadOnlyCollection<SocialProvider>>.Fail("session.not_signed_in");
            }

            try
            {
                var account = await _connection.Client.GetAccountAsync(session, null, cancellation);
                var linked = new List<SocialProvider>();
                if (!string.IsNullOrEmpty(account.User.AppleId))
                {
                    linked.Add(SocialProvider.Apple);
                }

                if (!string.IsNullOrEmpty(account.User.GoogleId))
                {
                    linked.Add(SocialProvider.Google);
                }

                if (!string.IsNullOrEmpty(account.User.FacebookId))
                {
                    linked.Add(SocialProvider.Facebook);
                }

                return Result<IReadOnlyCollection<SocialProvider>>.Ok(linked);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"account links: {error.Message}");
                return Result<IReadOnlyCollection<SocialProvider>>.Fail("session.unavailable", error.GetType().Name);
            }
        }

        public async Awaitable<Result<SocialLinkResult>> LinkAsync(SocialProvider provider, CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null)
            {
                return Result<SocialLinkResult>.Fail("session.not_signed_in");
            }

            var token = await _signIn.TokenAsync(provider, cancellation);
            if (!token.IsOk)
            {
                return Result<SocialLinkResult>.Fail(token.Error);
            }

            try
            {
                var client = _connection.Client;
                switch (provider)
                {
                    case SocialProvider.Apple:
                        await client.LinkAppleAsync(session, token.Value, null, cancellation);
                        break;
                    case SocialProvider.Google:
                        await client.LinkGoogleAsync(session, token.Value, null, cancellation);
                        break;
                    default:
                        await client.LinkFacebookAsync(session, token.Value, true, null, cancellation);
                        break;
                }

                return Result<SocialLinkResult>.Ok(SocialLinkResult.Done());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ApiResponseException error) when (error.StatusCode == 409 || error.GrpcStatusCode == 6)
            {
                // Another account already holds this provider: offer to switch to it.
                return Result<SocialLinkResult>.Ok(SocialLinkResult.InUse(token.Value));
            }
            catch (Exception error)
            {
                Debug.LogWarning($"link {provider}: {error.Message}");
                return Result<SocialLinkResult>.Fail(ServerErrors.CodeOf(error, "auth.provider_unavailable"), error.Message);
            }
        }

        public async Awaitable<Result<PlayerSession>> SwitchAsync(SocialProvider provider, string token, CancellationToken cancellation)
        {
            var current = _connection.Session;
            string? code = null;
            if (current != null)
            {
                // While still this phone's account, a merge code hands its progress to the provider's account.
                try
                {
                    var merge = await _connection.Client.RpcAsync(current, "account_merge_prepare", "{}", null, cancellation);
                    var body = JObject.Parse(merge.Payload);
                    code = (bool?)body["ok"] == true ? (string?)body["code"] : null;
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                {
                    Debug.LogWarning($"account_merge_prepare: {error.Message}");
                }
            }

            try
            {
                var client = _connection.Client;
                var session = provider switch
                {
                    SocialProvider.Apple => await client.AuthenticateAppleAsync(token, null, false, null, null, cancellation),
                    SocialProvider.Google => await client.AuthenticateGoogleAsync(token, null, false, null, null, cancellation),
                    _ => await client.AuthenticateFacebookAsync(token, null, false, true, null, null, cancellation),
                };
                if (code != null)
                {
                    try
                    {
                        await client.RpcAsync(session, "account_merge", new JObject { ["code"] = code }.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
                    }
                    catch (Exception error) when (!(error is OperationCanceledException))
                    {
                        Debug.LogWarning($"account_merge: {error.Message}");
                    }
                }

                return await _sessions.AdoptAsync(session, cancellation);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"sign in with {provider}: {error.Message}");
                return Result<PlayerSession>.Fail(ServerErrors.CodeOf(error, "auth.provider_unavailable"), error.GetType().Name);
            }
        }
    }
}
