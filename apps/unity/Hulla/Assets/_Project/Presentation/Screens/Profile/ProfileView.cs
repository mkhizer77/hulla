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
        private readonly TextField _name;
        private readonly TextField _handle;
        private readonly TextField _bio;
        private readonly Label _status;
        private readonly Button _copy;
        private bool _filled;

        public ProfileView(VisualElement root, ProfileViewModel model, Action back, Action<string, string, string> save)
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
                _stats.text = string.Format(UiText.StatsLine, card.Rounds, card.Escapes, card.Bhabhis, card.ThullasGiven);
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
    }
}
