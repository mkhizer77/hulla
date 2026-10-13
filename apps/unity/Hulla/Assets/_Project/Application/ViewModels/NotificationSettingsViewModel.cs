using System;
using System.Threading;
using Hulla.Application.Social;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The Notifications section of Settings: loaded from the server, each switch saved at once.</summary>
    public sealed class NotificationSettingsViewModel
    {
        private readonly INotificationsService _service;

        public NotificationSettingsViewModel(INotificationsService service)
        {
            _service = service;
        }

        public event Action? Changed;

        /// <summary>Null until loaded; the section stays hidden then.</summary>
        public NotificationSettings? Settings { get; private set; }

        public string? ErrorCode { get; private set; }

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            var loaded = await _service.SettingsAsync(cancellation);
            ErrorCode = loaded.IsOk ? null : loaded.Error.Code;
            if (loaded.IsOk)
            {
                Settings = loaded.Value;
            }

            Changed?.Invoke();
        }

        /// <summary>Applies the change at once and saves it; a refusal puts the old value back.</summary>
        public async Awaitable ChangeAsync(Action<NotificationSettings> change, CancellationToken cancellation)
        {
            if (Settings == null)
            {
                return;
            }

            var before = Settings;
            var next = before.Copy();
            change(next);
            Settings = next;
            Changed?.Invoke();
            var saved = await _service.SaveAsync(next, cancellation);
            ErrorCode = saved.IsOk ? null : saved.Error.Code;
            Settings = saved.IsOk ? saved.Value : before;
            Changed?.Invoke();
        }
    }
}
