using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Services
{
    public sealed class PhoneCodeSent
    {
        public PhoneCodeSent(string maskedPhone, long expiresTs, long resendAfterTs)
        {
            MaskedPhone = maskedPhone;
            ExpiresTs = expiresTs;
            ResendAfterTs = resendAfterTs;
        }

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

        Awaitable<Result<PhoneCodeSent>> StartAsync(string phone, CancellationToken cancellation);

        Awaitable<Result<PhoneVerified>> VerifyAsync(string phone, string code, CancellationToken cancellation);

        Awaitable<Result<bool>> RemoveAsync(CancellationToken cancellation);

        /// <summary>Hands this guest's progress to the number's account, then signs in to it.</summary>
        Awaitable<Result<PlayerSession>> SwitchAsync(string token, CancellationToken cancellation);
    }
}
