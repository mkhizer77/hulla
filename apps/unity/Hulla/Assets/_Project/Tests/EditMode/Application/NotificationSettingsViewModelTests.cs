using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    public sealed class NotificationSettingsViewModelTests
    {
        private sealed class FakeNotifications : INotificationsService
        {
            public NotificationSettings Stored = new NotificationSettings();
            public bool Refuse;

            private static Awaitable<T> Done<T>(T value)
            {
                var source = new AwaitableCompletionSource<T>();
                source.SetResult(value);
                return source.Awaitable;
            }

            public Awaitable<Result<InviteResult>> InviteAsync(IReadOnlyList<string> userIds, CancellationToken c) =>
                Done(Result<InviteResult>.Ok(new InviteResult(userIds, new Dictionary<string, string>(), 19)));

            public Awaitable<Result<NotificationSettings>> SettingsAsync(CancellationToken c) => Done(Result<NotificationSettings>.Ok(Stored.Copy()));

            public Awaitable<Result<NotificationSettings>> SaveAsync(NotificationSettings settings, CancellationToken c)
            {
                if (Refuse)
                {
                    return Done(Result<NotificationSettings>.Fail("network.unavailable"));
                }

                Stored = settings.Copy();
                return Done(Result<NotificationSettings>.Ok(Stored.Copy()));
            }
        }

        [Test]
        public void ASwitchIsSavedToTheServer()
        {
            var service = new FakeNotifications();
            var model = new NotificationSettingsViewModel(service);
            _ = model.RefreshAsync(CancellationToken.None);
            _ = model.ChangeAsync(n => n.FriendOnline = false, CancellationToken.None);
            Assert.IsFalse(service.Stored.FriendOnline);
            Assert.IsFalse(model.Settings!.FriendOnline);
            Assert.IsTrue(model.Settings.TableInvite, "the others are untouched");
        }

        [Test]
        public void ARefusedSaveRestoresTheSwitch()
        {
            var service = new FakeNotifications { Refuse = true };
            var model = new NotificationSettingsViewModel(service);
            _ = model.RefreshAsync(CancellationToken.None);
            _ = model.ChangeAsync(n => n.QuietHours = false, CancellationToken.None);
            Assert.IsTrue(model.Settings!.QuietHours);
            Assert.AreEqual("network.unavailable", model.ErrorCode);
        }
    }
}
