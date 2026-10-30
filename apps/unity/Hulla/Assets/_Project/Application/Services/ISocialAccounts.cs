using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>The sign-in providers a player can link to their account (GDD 10.2, #38).</summary>
    public enum SocialProvider
    {
        Apple,
        Google,
        Facebook,
    }

    /// <summary>
    /// The phone's own sign-in for each provider: Sign in with Apple, Google's account picker, Facebook
    /// Login. It hands back the provider's token for the server to check; it never sees a password.
    /// </summary>
    public interface ISocialSignIn
    {
        /// <summary>Whether this build and platform can sign in with the provider (configured, and supported here).</summary>
        bool Supports(SocialProvider provider);

        /// <summary>The provider's token for this player; fails <c>auth.cancelled</c> when they back out.</summary>
        Awaitable<Result<string>> TokenAsync(SocialProvider provider, CancellationToken cancellation);

        /// <summary>
        /// The player's Facebook friends who also use Hulla, as Facebook user ids (people you may know).
        /// Signs in to Facebook first if needed; only friends who allowed it themselves are listed.
        /// </summary>
        Awaitable<Result<IReadOnlyList<string>>> FacebookFriendIdsAsync(CancellationToken cancellation);
    }

    /// <summary>What linking a provider did.</summary>
    public sealed class SocialLinkResult
    {
        private SocialLinkResult(bool linked, string? token)
        {
            Linked = linked;
            Token = token;
        }

        /// <summary>True when the provider is now this account's.</summary>
        public bool Linked { get; }

        /// <summary>When another account already holds the provider: its token, to switch to that account.</summary>
        public string? Token { get; }

        public static SocialLinkResult Done() => new SocialLinkResult(true, null);

        public static SocialLinkResult InUse(string token) => new SocialLinkResult(false, token);
    }

    /// <summary>
    /// Apple, Google and Facebook on the player's account (GDD 10.2, #38): link one so progress survives a
    /// new phone, or sign in to the account that already holds it, bringing what this phone has along.
    /// </summary>
    public interface ISocialAccounts
    {
        /// <summary>The providers this build can offer, in the order they are shown.</summary>
        IReadOnlyList<SocialProvider> Available { get; }

        /// <summary>The providers this account has linked.</summary>
        Awaitable<Result<IReadOnlyCollection<SocialProvider>>> LinkedAsync(CancellationToken cancellation);

        Awaitable<Result<SocialLinkResult>> LinkAsync(SocialProvider provider, CancellationToken cancellation);

        /// <summary>Hands this guest's progress to the account that holds the provider, then signs in to it.</summary>
        Awaitable<Result<PlayerSession>> SwitchAsync(SocialProvider provider, string token, CancellationToken cancellation);
    }
}
