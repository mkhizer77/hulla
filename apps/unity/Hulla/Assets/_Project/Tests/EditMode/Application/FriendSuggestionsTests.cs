using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.Settings;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using Hulla.Infrastructure.Social;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    /// <summary>People you may know: address-book numbers in E.164, Facebook friend ids, the view model.</summary>
    public sealed class FriendSuggestionsTests
    {
        [Test]
        public void ALocalNumberTakesThePhonesCountryAndLosesTheTrunkZero()
        {
            Assert.AreEqual("+923001234567", PhoneNumbers.Normalise("0300 1234567", "92"));
            Assert.AreEqual("+447700900123", PhoneNumbers.Normalise("(07700) 900-123", "44"));
        }

        [Test]
        public void AnInternationalNumberKeepsItsOwnCountry()
        {
            Assert.AreEqual("+923001234567", PhoneNumbers.Normalise("+92 300 1234567", "44"));
            Assert.AreEqual("+923001234567", PhoneNumbers.Normalise("0092 300 1234567", "44"));
        }

        [Test]
        public void WhatCannotBeANumberIsSkipped()
        {
            Assert.IsNull(PhoneNumbers.Normalise("123", "92"));
            Assert.IsNull(PhoneNumbers.Normalise("0300 1234567", null));
            Assert.IsNull(PhoneNumbers.Normalise("not a number", "92"));
        }

        [Test]
        public void EachNumberIsSentOnce()
        {
            var all = PhoneNumbers.NormaliseAll(new[] { "0300 1234567", "+923001234567", "12" }, "92");
            CollectionAssert.AreEqual(new[] { "+923001234567" }, all);
        }

        [Test]
        public void TheCallingCodeFollowsTheRegion()
        {
            Assert.AreEqual("92", PhoneNumbers.CallingCode("pk"));
            Assert.IsNull(PhoneNumbers.CallingCode("ZZ"));
        }

        [Test]
        public void TheNativeContactsListIsReadAsStrings()
        {
            CollectionAssert.AreEqual(new[] { "+92 300", "0300" }, NativeContacts.Parse("[\"+92 300\", 5, \"0300\"]"));
        }

        [Test]
        public void TheGraphFriendsAnswerGivesIdsAndAMalformedOneNobody()
        {
            CollectionAssert.AreEqual(new[] { "1", "2" }, NativeSocialSignIn.FriendIdsFrom("{\"data\":[{\"id\":\"1\",\"name\":\"A\"},{\"id\":\"2\"}]}"));
            CollectionAssert.IsEmpty(NativeSocialSignIn.FriendIdsFrom("oops"));
        }

        [Test]
        public void FromContactsLooksUpTheNumbersAndRemembersTheChoice()
        {
            var service = new FakeSuggestions();
            var store = new MemoryStore();
            var model = new FriendSuggestionsViewModel(service, new FakeContacts("0300 1234567"), new FakeSignIn(), store, "92");
            Run(model.FromContactsAsync(CancellationToken.None));
            CollectionAssert.AreEqual(new[] { "+923001234567" }, service.Phones);
            Assert.AreEqual(1, model.Suggestions.Count);
            Assert.IsTrue(model.ContactsOn);
            Assert.IsFalse(model.Busy);

            model.MarkRequested("u1");
            Assert.IsTrue(model.Requested("u1"));
        }

        [Test]
        public void RefusedContactsShowWhyAndAreNotRemembered()
        {
            var model = new FriendSuggestionsViewModel(new FakeSuggestions(), new FakeContacts(null), new FakeSignIn(), new MemoryStore(), "92");
            Run(model.FromContactsAsync(CancellationToken.None));
            Assert.AreEqual("contacts.denied", model.ErrorCode);
            Assert.IsFalse(model.ContactsOn);
        }

        [Test]
        public void FromFacebookSendsTheFriendIds()
        {
            var service = new FakeSuggestions();
            var model = new FriendSuggestionsViewModel(service, new FakeContacts(null), new FakeSignIn("10", "11"), new MemoryStore(), "92");
            Run(model.FromFacebookAsync(CancellationToken.None));
            CollectionAssert.AreEqual(new[] { "10", "11" }, service.FacebookIds);
            Assert.IsTrue(model.FacebookOn);
        }

        private static void Run(Awaitable awaitable)
        {
            var awaiter = awaitable.GetAwaiter();
            Assert.IsTrue(awaiter.IsCompleted, "the fakes answer at once");
            awaiter.GetResult();
        }

        private static Awaitable<T> Done<T>(T value)
        {
            var done = new AwaitableCompletionSource<T>();
            done.SetResult(value);
            return done.Awaitable;
        }

        private sealed class FakeSuggestions : IFriendSuggestions
        {
            public IReadOnlyList<string> Phones = new string[0];
            public IReadOnlyList<string> FacebookIds = new string[0];

            public Awaitable<Result<IReadOnlyList<FriendSuggestion>>> FindAsync(IReadOnlyList<string> phones, IReadOnlyList<string> facebookIds, CancellationToken cancellation)
            {
                Phones = phones;
                FacebookIds = facebookIds;
                IReadOnlyList<FriendSuggestion> found = new[] { new FriendSuggestion("u1", "Ali", null, phones.Count > 0 ? SuggestionSource.Contacts : SuggestionSource.Facebook) };
                return Done(Result<IReadOnlyList<FriendSuggestion>>.Ok(found));
            }
        }

        private sealed class FakeContacts : IContactsSource
        {
            private readonly string? _number;

            public FakeContacts(string? number) => _number = number;

            public bool Supported => true;

            public Awaitable<Result<IReadOnlyList<string>>> PhoneNumbersAsync(CancellationToken cancellation) =>
                Done(_number == null
                    ? Result<IReadOnlyList<string>>.Fail("contacts.denied")
                    : Result<IReadOnlyList<string>>.Ok(new[] { _number }));
        }

        private sealed class FakeSignIn : ISocialSignIn
        {
            private readonly string[] _friends;

            public FakeSignIn(params string[] friends) => _friends = friends;

            public bool Supports(SocialProvider provider) => true;

            public Awaitable<Result<string>> TokenAsync(SocialProvider provider, CancellationToken cancellation) => Done(Result<string>.Ok("token"));

            public Awaitable<Result<IReadOnlyList<string>>> FacebookFriendIdsAsync(CancellationToken cancellation) =>
                Done(Result<IReadOnlyList<string>>.Ok(_friends));
        }

        private sealed class MemoryStore : ISettingsStore
        {
            private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

            public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var v) ? v : fallback;

            public void SetInt(string key, int value) => _values[key] = value;

            public void Save()
            {
            }
        }
    }
}
