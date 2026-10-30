using System;
using System.Collections.Generic;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Profile
{
    /// <summary>
    /// The player's own profile (design: Profile), something to show rather than a form: the face with
    /// its frame, name, handle, level and title on one card; the friend code with Share; four stat
    /// tiles; a row of medals with See all; the ranked line. The pencil opens the edit sheet with the
    /// name, handle and bio fields.
    /// </summary>
    public sealed class ProfileView : IDisposable
    {
        private readonly ProfileViewModel _model;
        private readonly VisualElement _root;
        private readonly Label _code;
        private readonly Label _stats;
        private readonly TextField _name;
        private readonly TextField _handle;
        private readonly TextField _bio;
        private readonly Label _status;
        private readonly Button _copy;
        private readonly Button _save;
        private readonly VisualElement _fields;
        private readonly VisualElement _face;
        private readonly Label _faceInitial;
        private readonly Label _displayName;
        private readonly Label _line;
        private readonly VisualElement _tiles;
        private readonly VisualElement _medals;
        private readonly Label _medalsTitle;
        private readonly Label _ranked;
        private bool _filled;

        public ProfileView(VisualElement root, ProfileViewModel model, Action back, Action<string, string, string> save, Action? openAchievements = null)
        {
            _model = model;
            _root = root;
            root.Q<Label>("title").text = UiText.Profile;
            var backButton = root.Q<Button>("back");
            backButton.text = UiText.Back;
            backButton.clicked += back;
            root.Q<Label>("code-label").text = UiText.YourFriendCode;
            _code = root.Q<Label>("code");
            _copy = root.Q<Button>("copy");
            _copy.text = "";
            _copy.tooltip = UiText.Share;
            _copy.clicked += () =>
            {
                if (ShareCode != null)
                {
                    ShareCode();
                    return;
                }

                GUIUtility.systemCopyBuffer = _code.text;
            };
            _stats = root.Q<Label>("stats");
            var achievements = root.Q<Button>("achievements");
            achievements.text = UiText.SeeAll;
            achievements.clicked += () => openAchievements?.Invoke();
            achievements.style.display = openAchievements == null ? DisplayStyle.None : DisplayStyle.Flex;
            _face = root.Q<VisualElement>("face");
            _faceInitial = root.Q<Label>("face-initial");
            _displayName = root.Q<Label>("display-name");
            _line = root.Q<Label>("profile-line");
            _tiles = root.Q<VisualElement>("tiles");
            _medals = root.Q<VisualElement>("medals");
            _medalsTitle = root.Q<Label>("medals-title");
            _ranked = root.Q<Label>("ranked-line");
            _fields = root.Q<VisualElement>("edit-fields");
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
            _save = root.Q<Button>("save");
            _save.text = UiText.Save;
            _save.clicked += () => save(_name.value, _handle.value, _bio.value);
            var edit = root.Q<Button>("edit");
            edit.text = "";
            edit.tooltip = UiText.EditProfile;
            edit.clicked += OpenEdit;
            _model.Changed += Refresh;
            Refresh();
        }

        public void Dispose() => _model.Changed -= Refresh;

        /// <summary>Shares the friend code (the share sheet holds Copy); null copies it instead.</summary>
        public Action? ShareCode { get; set; }

        /// <summary>The player's level for the card; null leaves it out.</summary>
        public Func<long?>? Level { get; set; }

        /// <summary>Achievements unlocked and in all, and whether each of the first five is unlocked.</summary>
        public Func<(int Unlocked, int Total, IReadOnlyList<bool> First)?>? Medals { get; set; }

        /// <summary>"Ranked · Silver II" or similar; null hides the line.</summary>
        public Func<string?>? RankedText { get; set; }

        /// <summary>The face when the card carries none (the account's starting avatar).</summary>
        public string? FallbackAvatar { get; set; }

        /// <summary>Draws again, for when the level, medals or ranked line arrive after the card.</summary>
        public void Redraw() => Refresh();

        private void Refresh()
        {
            var card = _model.Card;
            if (card != null)
            {
                _code.text = card.FriendCode;
                _stats.text = Strings.Format(UiText.StatsLine, card.Rounds, card.Escapes, card.Bhabhis, card.ThullasGiven);
                _displayName.text = card.DisplayName;
                AvatarArt.Apply(_face, _faceInitial, card.Avatar ?? FallbackAvatar, card.DisplayName);
                var level = Level?.Invoke();
                _line.text = level == null
                    ? "@" + card.Handle
                    : card.Title != null
                        ? Strings.Format(UiText.ProfileLineTitle, card.Handle, level, UiText.TitleName(card.Title))
                        : Strings.Format(UiText.ProfileLine, card.Handle, level);
                _tiles.Clear();
                Tile(card.Rounds, UiText.StatRounds, false);
                Tile(card.Escapes, UiText.StatEscapes, false);
                Tile(card.ThullasGiven, UiText.StatThullas, false);
                Tile(card.Bhabhis, UiText.StatBhabhi, true);
                if (!_filled)
                {
                    _filled = true;
                    _name.value = card.DisplayName;
                    _handle.value = card.Handle;
                    _bio.value = card.Bio;
                }
            }

            var medals = Medals?.Invoke();
            _medals.Clear();
            _medalsTitle.text = medals is { } m ? Strings.Format(UiText.AchievementsOf, m.Unlocked, m.Total) : UiText.Achievements;
            if (medals is { } list)
            {
                foreach (var unlocked in list.First)
                {
                    var tile = new VisualElement { pickingMode = PickingMode.Ignore };
                    tile.AddToClassList("profile__medal");
                    tile.EnableInClassList("profile__medal--locked", !unlocked);
                    _medals.Add(tile);
                }
            }

            var ranked = RankedText?.Invoke();
            _ranked.text = ranked ?? "";
            _ranked.style.display = string.IsNullOrEmpty(ranked) ? DisplayStyle.None : DisplayStyle.Flex;
            _status.text = _model.ErrorCode != null ? UiText.Error(_model.ErrorCode) : _model.Saved ? UiText.Saved : "";
        }

        private void Tile(long value, string label, bool red)
        {
            var tile = new VisualElement();
            tile.AddToClassList("profile__tile");
            var number = new Label(value.ToString("N0", Strings.Culture));
            number.AddToClassList("profile__tile-number");
            number.EnableInClassList("profile__tile-number--red", red);
            tile.Add(number);
            var name = new Label(label);
            name.AddToClassList("profile__tile-label");
            tile.Add(name);
            _tiles.Add(tile);
        }

        /// <summary>The edit sheet (design: Edit profile): the fields move into the sheet and back when it closes.</summary>
        private void OpenEdit()
        {
            var panel = Sheet.Open(_root, UiText.EditProfile, () =>
            {
                _fields.Add(_name);
                _fields.Add(_handle);
                _fields.Add(_bio);
                _fields.Add(_save);
                _fields.Add(_status);
            });
            panel.AddToClassList("edit-profile");
            panel.Add(_name);
            panel.Add(_handle);
            panel.Add(Sheet.Line(UiText.HandleRule, "edit-profile__rule"));
            panel.Add(_bio);
            panel.Add(_save);
            panel.Add(_status);
        }
    }
}
