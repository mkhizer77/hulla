using System.Collections.Generic;
using System.Text;
using System.Threading;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Application.Social
{
    /// <summary>Where a suggested player was found.</summary>
    public enum SuggestionSource
    {
        Contacts,
        Facebook,
    }

    /// <summary>Someone who plays Hulla and is in your contacts or a Facebook friend (people you may know).</summary>
    public sealed class FriendSuggestion
    {
        public FriendSuggestion(string userId, string name, string? avatar, SuggestionSource source)
        {
            UserId = userId;
            Name = name;
            Avatar = avatar;
            Source = source;
        }

        public string UserId { get; }

        public string Name { get; }

        public string? Avatar { get; }

        public SuggestionSource Source { get; }
    }

    /// <summary>
    /// friend_suggestions: the server matches numbers against verified phone sign-ins and Facebook ids
    /// against Facebook sign-ins, and keeps neither list.
    /// </summary>
    public interface IFriendSuggestions
    {
        Awaitable<Result<IReadOnlyList<FriendSuggestion>>> FindAsync(IReadOnlyList<string> phones, IReadOnlyList<string> facebookIds, CancellationToken cancellation);
    }

    /// <summary>The phone's address book: phone numbers only, after the player allows it.</summary>
    public interface IContactsSource
    {
        bool Supported { get; }

        /// <summary>Every phone number in the address book; fails <c>contacts.denied</c> when not allowed.</summary>
        Awaitable<Result<IReadOnlyList<string>>> PhoneNumbersAsync(CancellationToken cancellation);
    }

    /// <summary>
    /// Address-book numbers to E.164 before they leave the phone (the server takes nothing else): "+" and
    /// "00" numbers keep their country; a local number ("0300 1234567") takes the phone's own country.
    /// </summary>
    public static class PhoneNumbers
    {
        private static readonly Dictionary<string, string> CallingCodes = new Dictionary<string, string>
        {
            ["PK"] = "92", ["IN"] = "91", ["BD"] = "880", ["NP"] = "977", ["LK"] = "94", ["AF"] = "93",
            ["GB"] = "44", ["IE"] = "353", ["US"] = "1", ["CA"] = "1", ["AU"] = "61", ["NZ"] = "64",
            ["AE"] = "971", ["SA"] = "966", ["QA"] = "974", ["KW"] = "965", ["OM"] = "968", ["BH"] = "973",
            ["DE"] = "49", ["FR"] = "33", ["NL"] = "31", ["IT"] = "39", ["ES"] = "34", ["NO"] = "47",
            ["SE"] = "46", ["DK"] = "45", ["MY"] = "60", ["SG"] = "65",
        };

        /// <summary>The calling code for a region ("PK" → "92"), or null for one not listed.</summary>
        public static string? CallingCode(string region) => CallingCodes.TryGetValue(region.ToUpperInvariant(), out var code) ? code : null;

        /// <summary>E.164 for an address-book entry, or null when it can't be one.</summary>
        public static string? Normalise(string raw, string? callingCode)
        {
            var digits = new StringBuilder();
            var plus = false;
            foreach (var c in raw.Trim())
            {
                if (c == '+' && digits.Length == 0)
                {
                    plus = true;
                }
                else if (c >= '0' && c <= '9')
                {
                    digits.Append(c);
                }
            }

            var number = digits.ToString();
            if (!plus && number.StartsWith("00"))
            {
                plus = true;
                number = number.Substring(2);
            }
            else if (!plus && callingCode != null)
            {
                // A local number: drop the trunk zero and add the phone's own country.
                number = callingCode + (number.StartsWith("0") ? number.Substring(1) : number);
                plus = true;
            }

            return plus && number.Length >= 8 && number.Length <= 15 && number[0] != '0' ? "+" + number : null;
        }

        /// <summary>Each entry once, in E.164, skipping what can't be one.</summary>
        public static IReadOnlyList<string> NormaliseAll(IEnumerable<string> raw, string? callingCode)
        {
            var seen = new HashSet<string>();
            var list = new List<string>();
            foreach (var entry in raw)
            {
                var number = Normalise(entry, callingCode);
                if (number != null && seen.Add(number))
                {
                    list.Add(number);
                }
            }

            return list;
        }
    }
}
