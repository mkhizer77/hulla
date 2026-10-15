using System;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Profile
{
    /// <summary>The player's own profile: friend code to copy, a stats line, and name, handle and bio to edit.</summary>
    public sealed class ProfileView : IDisposable
    {
        private readonly ProfileViewModel _model;
        private readonly Label _code;
        private readonly Label _stats;
        private readonly VisualElement _grid;
        private readonly VisualElement _avatar;
        private readonly Label _initial;
        private readonly Label _display;
        private readonly Label _handleLine;
        private readonly Label _bioScroll;
        private readonly TextField _name;
        private readonly TextField _handle;
        private readonly TextField _bio;
        private readonly Label _status;
        private readonly Button _copy;
        private bool _filled;

        public ProfileView(VisualElement root, ProfileViewModel model, Action back, Action<string, string, string> save, Action? openAchievements = null)
        {
            _model = model;
            root.Q<Label>("title").text = UiText.Profile;
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            root.Q<Label>("code-label").text = UiText.FriendCodeLabel;
            _code = root.Q<Label>("code");
            _copy = root.Q<Button>("copy");
            _copy.text = UiText.Copy;
            _copy.clicked += () =>
            {
                GUIUtility.systemCopyBuffer = _code.text;
                _copy.text = UiText.Copied;
            };
            _stats = root.Q<Label>("stats");
            _grid = root.Q<VisualElement>("stats-grid");
            _avatar = root.Q<VisualElement>("avatar");
            _initial = root.Q<Label>("avatar-initial");
            _display = root.Q<Label>("display");
            _handleLine = root.Q<Label>("handle-line");
            _bioScroll = root.Q<Label>("bio-scroll");
            var achievements = root.Q<Button>("achievements");
            achievements.text = UiText.Achievements;
            achievements.clicked += () => openAchievements?.Invoke();
            achievements.style.display = openAchievements == null ? DisplayStyle.None : DisplayStyle.Flex;
            _name = root.Q<TextField>("name");
            _name.label = UiText.DisplayNameLabel;
            _name.maxLength = 16;
            _handle = root.Q<TextField>("handle");
            _handle.label = UiText.HandleLabel;
            _handle.maxLength = 16;
            _bio = root.Q<TextField>("bio");
            _bio.label = UiText.BioLabel;
            _bio.maxLength = 60;
            _status = root.Q<Label>("status");
            var saveButton = root.Q<Button>("save");
            saveButton.text = UiText.Save;
            saveButton.clicked += () => save(_name.value, _handle.value, _bio.value);
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        private void Refresh()
        {
            var card = _model.Card;
            if (card != null)
            {
                _code.text = card.FriendCode;
                _stats.text = Strings.Format(UiText.StatsLine, card.Rounds, card.Escapes, card.Bhabhis, card.ThullasGiven);
                Dossier(card);
                if (!_filled)
                {
                    _filled = true;
                    _name.value = card.DisplayName;
                    _handle.value = card.Handle;
                    _bio.value = card.Bio;
                }
            }

            _status.text = _model.ErrorCode != null ? UiText.Error(_model.ErrorCode) : _model.Saved ? UiText.Saved : "";
        }

        /// <summary>The dossier (art pack, Profile): the face in its frame, name and handle, four stats and the bio.</summary>
        private void Dossier(Hulla.Application.Social.ProfileCard card)
        {
            TableKit.AvatarArt.Apply(_avatar, _initial, null, card.DisplayName);
            _display.text = card.DisplayName;
            _handleLine.text = card.Handle.Length == 0 ? "" : "@" + card.Handle;
            _bioScroll.text = card.Bio;
            _bioScroll.style.display = card.Bio.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            _grid.Clear();
            Stat(card.Rounds, UiText.StatRounds, false);
            Stat(card.Escapes, UiText.StatEscapes, false);
            Stat(card.ThullasGiven, UiText.StatThullas, false);
            Stat(card.Bhabhis, UiText.StatBhabhis, true);
        }

        private void Stat(long value, string label, bool loss)
        {
            var cell = new VisualElement();
            cell.AddToClassList("profile__stat");
            cell.EnableInClassList("profile__stat--loss", loss);
            var number = new Label(value.ToString("N0"));
            number.AddToClassList("profile__stat-value");
            cell.Add(number);
            var text = new Label(label);
            text.AddToClassList("profile__stat-label");
            cell.Add(text);
            _grid.Add(cell);
        }
    }
}
