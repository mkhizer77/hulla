using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.Settings;
using Hulla.Application.Social;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>
    /// People you may know on the Friends screen (owner decision, 30 Oct): players in your contacts and
    /// Facebook friends who play. The player opts in per source once; after that the list refreshes
    /// quietly whenever Friends opens. Neither the numbers nor the ids are kept on the phone or server.
    /// </summary>
    public sealed class FriendSuggestionsViewModel
    {
        public const string ContactsKey = "hulla.suggestions.contacts";
        public const string FacebookKey = "hulla.suggestions.facebook";

        private readonly IFriendSuggestions _suggestions;
        private readonly IContactsSource _contacts;
        private readonly ISocialSignIn _signIn;
        private readonly ISettingsStore _store;
        private readonly string? _callingCode;
        private readonly HashSet<string> _requested = new HashSet<string>();
        private IReadOnlyList<string> _phones = new string[0];
        private IReadOnlyList<string> _facebookIds = new string[0];

        /// <param name="callingCode">The phone's own country code for local numbers ("92"), or null.</param>
        public FriendSuggestionsViewModel(IFriendSuggestions suggestions, IContactsSource contacts, ISocialSignIn signIn, ISettingsStore store, string? callingCode)
        {
            _suggestions = suggestions;
            _contacts = contacts;
            _signIn = signIn;
            _store = store;
            _callingCode = callingCode;
        }

        public event Action? Changed;

        public IReadOnlyList<FriendSuggestion> Suggestions { get; private set; } = new FriendSuggestion[0];

        public bool Busy { get; private set; }

        public string? ErrorCode { get; private set; }

        public bool ContactsSupported => _contacts.Supported;

        public bool FacebookSupported => _signIn.Supports(SocialProvider.Facebook);

        /// <summary>The player has allowed contacts here before.</summary>
        public bool ContactsOn => _store.GetInt(ContactsKey, 0) == 1;

        public bool FacebookOn => _store.GetInt(FacebookKey, 0) == 1;

        public bool Requested(string userId) => _requested.Contains(userId);

        public void MarkRequested(string userId)
        {
            _requested.Add(userId);
            Changed?.Invoke();
        }

        /// <summary>Asks for contacts (the system prompt the first time), then looks them up.</summary>
        public async Awaitable FromContactsAsync(CancellationToken cancellation)
        {
            Begin();
            var numbers = await _contacts.PhoneNumbersAsync(cancellation);
            if (!numbers.IsOk)
            {
                Finish(numbers.Error.Code);
                return;
            }

            _phones = PhoneNumbers.NormaliseAll(numbers.Value, _callingCode);
            Remember(ContactsKey);
            await LookUpAsync(cancellation);
        }

        /// <summary>Signs in to Facebook if needed, then looks up friends who play.</summary>
        public async Awaitable FromFacebookAsync(CancellationToken cancellation)
        {
            Begin();
            var ids = await _signIn.FacebookFriendIdsAsync(cancellation);
            if (!ids.IsOk)
            {
                Finish(ids.Error.Code == "auth.cancelled" ? null : ids.Error.Code);
                return;
            }

            _facebookIds = ids.Value;
            Remember(FacebookKey);
            await LookUpAsync(cancellation);
        }

        /// <summary>When Friends opens: the sources already allowed, read again without asking.</summary>
        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            if (ContactsOn && _contacts.Supported && _phones.Count == 0)
            {
                var numbers = await _contacts.PhoneNumbersAsync(cancellation);
                if (numbers.IsOk)
                {
                    _phones = PhoneNumbers.NormaliseAll(numbers.Value, _callingCode);
                }
            }

            if (_phones.Count + _facebookIds.Count > 0)
            {
                Begin();
                await LookUpAsync(cancellation);
            }
        }

        private async Awaitable LookUpAsync(CancellationToken cancellation)
        {
            var found = await _suggestions.FindAsync(_phones, _facebookIds, cancellation);
            if (found.IsOk)
            {
                Suggestions = found.Value;
            }

            Finish(found.IsOk ? null : found.Error.Code);
        }

        private void Remember(string key)
        {
            _store.SetInt(key, 1);
            _store.Save();
        }

        private void Begin()
        {
            Busy = true;
            ErrorCode = null;
            Changed?.Invoke();
        }

        private void Finish(string? error)
        {
            Busy = false;
            ErrorCode = error;
            Changed?.Invoke();
        }
    }
}
