using Hulla.Application.Settings;

namespace Hulla.Application.ViewModels
{
    /// <summary>
    /// The soft prompts after the first live round (GDD 17.2 step 6): keep your progress, then invite
    /// your group. One per visit to Home, each once ever, and only once a live round has finished.
    /// </summary>
    public sealed class WelcomePrompts
    {
        public const string FirstRoundKey = "hulla.welcome.first_round";
        public const string LinkShownKey = "hulla.welcome.link_prompt";
        public const string InviteShownKey = "hulla.welcome.invite_prompt";

        private readonly ISettingsStore _store;

        public WelcomePrompts(ISettingsStore store)
        {
            _store = store;
        }

        public enum Prompt
        {
            None,
            KeepProgress,
            Invite,
        }

        public void FirstRoundFinished()
        {
            _store.SetInt(FirstRoundKey, 1);
            _store.Save();
        }

        /// <summary>What to offer on this visit to Home; <paramref name="linked"/> skips the progress prompt.</summary>
        public Prompt Next(bool linked)
        {
            if (_store.GetInt(FirstRoundKey, 0) == 0)
            {
                return Prompt.None;
            }

            if (_store.GetInt(LinkShownKey, 0) == 0)
            {
                Mark(LinkShownKey);
                if (!linked)
                {
                    return Prompt.KeepProgress;
                }
            }

            if (_store.GetInt(InviteShownKey, 0) == 0)
            {
                Mark(InviteShownKey);
                return Prompt.Invite;
            }

            return Prompt.None;
        }

        private void Mark(string key)
        {
            _store.SetInt(key, 1);
            _store.Save();
        }
    }
}
