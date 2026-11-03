using System.Collections.Generic;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    /// <summary>Where phone_start sends a code (protocol shell/phone.json).</summary>
    public static class PhoneChannels
    {
        public const string WhatsApp = "whatsapp";
        public const string Sms = "sms";
    }

    public sealed class PhoneCodeSent
    {
        public PhoneCodeSent(string maskedPhone, long expiresTs, long resendAfterTs, string channel = PhoneChannels.Sms, IReadOnlyList<string>? channels = null, bool fallback = false)
        {
            MaskedPhone = maskedPhone;
            ExpiresTs = expiresTs;
            ResendAfterTs = resendAfterTs;
            Channel = channel;
            Channels = channels ?? new[] { PhoneChannels.Sms };
            Fallback = fallback;
        }

        /// <summary>Where the code went: whatsapp or sms; anything else reads as sms.</summary>
        public string Channel { get; }

        /// <summary>The channels the server can send on, in the order it tries them.</summary>
        public IReadOnlyList<string> Channels { get; }

        /// <summary>WhatsApp was tried first and failed, so the code went by SMS.</summary>
        public bool Fallback { get; }

        /// <summary>"+92 ••• 67": the number as the server shows it back.</summary>
        public string MaskedPhone { get; }

        public long ExpiresTs { get; }

        public long ResendAfterTs { get; }
    }

    public sealed class PhoneVerified
    {
        public PhoneVerified(bool linked, string maskedPhone, string? token)
        {
            Linked = linked;
            MaskedPhone = maskedPhone;
            Token = token;
        }

        /// <summary>True when the number is now this account's; false when another account holds it.</summary>
        public bool Linked { get; }

        public string MaskedPhone { get; }

        /// <summary>The one-time sign-in token for the account that holds the number.</summary>
        public string? Token { get; }
    }

    /// <summary>
    /// Phone sign-in (GDD 10.2, #99): link a number to this account, or sign in to the account that
    /// holds it, bringing what this phone has along.
    /// </summary>
    public interface IPhoneService
    {
        /// <summary>Whether this account has a number.</summary>
        Awaitable<Result<bool>> StatusAsync(CancellationToken cancellation);

        /// <param name="channel">Null lets the server try WhatsApp, then SMS; sms for "Send by SMS instead".</param>
        Awaitable<Result<PhoneCodeSent>> StartAsync(string phone, string? channel, CancellationToken cancellation);

        Awaitable<Result<PhoneVerified>> VerifyAsync(string phone, string code, CancellationToken cancellation);

        Awaitable<Result<bool>> RemoveAsync(CancellationToken cancellation);

        /// <summary>Hands this guest's progress to the number's account, then signs in to it.</summary>
        Awaitable<Result<PlayerSession>> SwitchAsync(string token, CancellationToken cancellation);
    }
}
