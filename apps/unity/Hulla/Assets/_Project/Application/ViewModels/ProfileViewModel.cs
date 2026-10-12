using System;
using System.Threading;
using Hulla.Application.Social;
using Hulla.Protocol;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The player's own profile (GDD 10.1): the card others see, the friend code to share, and edits.</summary>
    public sealed class ProfileViewModel
    {
        private readonly ISocialService _social;

        public ProfileViewModel(ISocialService social)
        {
            _social = social;
        }

        public event Action? Changed;

        public Profile? Profile { get; private set; }

        /// <summary>What the profile screen shows; null until the first load.</summary>
        public ProfileCard? Card => Profile == null
            ? null
            : new ProfileCard(
                Profile.DisplayName,
                Profile.Handle,
                Profile.Bio ?? "",
                Profile.FriendCode ?? "",
                Profile.Stats?.Rounds ?? 0,
                Profile.Stats?.Escapes ?? 0,
                Profile.Stats?.Bhabhis ?? 0,
                Profile.Stats?.ThullasGiven ?? 0);

        public string? ErrorCode { get; private set; }

        public bool Saved { get; private set; }

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            var profile = await _social.MyProfileAsync(cancellation);
            Profile = profile.IsOk ? profile.Value : Profile;
            ErrorCode = profile.IsOk ? null : profile.Error.Code;
            Changed?.Invoke();
        }

        /// <summary>Saves only what changed; an unchanged field is left out of the update.</summary>
        public async Awaitable SaveAsync(string displayName, string handle, string bio, CancellationToken cancellation)
        {
            var current = Profile;
            var update = new ProfileUpdate
            {
                DisplayName = current == null || displayName.Trim() != current.DisplayName ? displayName.Trim() : null,
                Handle = current == null || handle.Trim() != current.Handle ? handle.Trim() : null,
                Bio = current == null || bio.Trim() != current.Bio ? bio.Trim() : null,
            };
            Saved = false;
            if (update.DisplayName == null && update.Handle == null && update.Bio == null)
            {
                Saved = true;
                Changed?.Invoke();
                return;
            }

            var result = await _social.UpdateProfileAsync(update, cancellation);
            Saved = result.IsOk;
            Profile = result.IsOk ? result.Value : Profile;
            ErrorCode = result.IsOk ? null : result.Error.Code;
            Changed?.Invoke();
        }
    }
}
