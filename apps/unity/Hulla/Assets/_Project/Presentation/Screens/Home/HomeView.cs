using System;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Home
{
    /// <summary>
    /// Home (GDD 14.10, design §4): who you are and your coins, the party bar of friends online, the
    /// adaptive hero (a friend's table to join or watch, the table you left, or Play now), Create a
    /// room, Join by code, the daily reward. Shop, Pass, Friends and Profile live on the bottom navigation.
    /// </summary>
    public sealed class HomeView : IDisposable
    {
        private readonly HomeViewModel _model;
        private readonly TextField _name;
        private readonly Label _nameLabel;
        private readonly VisualElement _namePanel;
        private readonly VisualElement _avatar;
        private readonly Label _avatarInitial;
        private readonly TextField _code;
        private readonly VisualElement _joinPanel;
        private readonly Button _create;
        private readonly Button _join;
        private readonly Label _status;
        private readonly TextField _server;
        private readonly HomeFriendsViewModel? _friendsOnline;
        private readonly ProgressViewModel? _progress;
        private readonly Label _progressLine;
        private readonly Label _coins;
        private readonly Button _hero;
        private readonly Button _rejoin;
        private Action? _onRejoin;
        private readonly Label _online;
        private readonly VisualElement _party;
        private readonly Button _quick;
        private bool _quickAvailable = true;
        private string? _note;
        private int _seats = 4;

        /// <param name="server">The server to show in a development build, or null to hide the field.</param>
        public HomeView(VisualElement root, HomeViewModel model, string? server, Action<string, string?> create, Action<string, string, string?> join, Action<string, int, string?> quickPlay, Action? openSettings = null, Action<string, string?>? openFriends = null, Action<string, string?>? openProfile = null, HomeFriendsViewModel? friendsOnline = null, Action? howToPlay = null, string? howToPlayText = null, Action<string, string, string?>? watch = null, ProgressViewModel? progress = null, Action<string, string?>? openMissions = null, Action<string, string?>? openLeaderboard = null, Action<string, string?>? openShop = null, Action<string, string?>? openPass = null)
        {
            _model = model;
            _progress = progress;
            _progressLine = root.Q<Label>("progress");
            _coins = root.Q<Label>("coins");
            _avatar = root.Q<VisualElement>("avatar");
            _avatarInitial = root.Q<Label>("avatar-initial");
            _nameLabel = root.Q<Label>("name-label");
            _namePanel = root.Q<VisualElement>("name-panel");
            if (_progress != null)
            {
                _progress.Changed += RefreshProgress;
            }

            var how = root.Q<Button>("how-to-play");
            how.text = howToPlayText ?? "";
            how.clicked += () => howToPlay?.Invoke();
            how.style.display = howToPlay == null ? DisplayStyle.None : DisplayStyle.Flex;
            _friendsOnline = friendsOnline;
            _hero = root.Q<Button>("hero");
            _rejoin = root.Q<Button>("rejoin");
            _rejoin.style.display = DisplayStyle.None;
            _rejoin.clicked += () => _onRejoin?.Invoke();
            _online = root.Q<Label>("online");
            _party = root.Q<VisualElement>("party");
            var settings = root.Q<Button>("settings");
            settings.text = UiText.Settings;
            settings.clicked += () => openSettings?.Invoke();
            settings.style.display = openSettings == null ? DisplayStyle.None : DisplayStyle.Flex;
            root.Q<Label>("title").text = UiText.AppTitle;
            root.Q<Label>("tagline").text = UiText.Tagline;
            _name = root.Q<TextField>("name");
            _name.label = UiText.NameLabel;
            _name.value = model.LastName;
            _name.maxLength = 16;
            _name.RegisterValueChangedCallback(_ => RefreshName());

            // The name is asked once (GDD 17.2); afterwards it sits in the header and a tap edits it.
            _namePanel.style.display = model.LastName.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            root.Q<VisualElement>("me").RegisterCallback<ClickEvent>(_ =>
                _namePanel.style.display = _namePanel.style.display == DisplayStyle.Flex ? DisplayStyle.None : DisplayStyle.Flex);
            _code = root.Q<TextField>("code");
            _code.label = UiText.CodeLabel;
            _code.maxLength = 8;
            _joinPanel = root.Q<VisualElement>("join-panel");
            _joinPanel.style.display = DisplayStyle.None;
            var joinToggle = root.Q<Button>("join-toggle");
            joinToggle.text = UiText.JoinByCode;
            joinToggle.clicked += () =>
            {
                var open = _joinPanel.style.display != DisplayStyle.Flex;
                _joinPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (open)
                {
                    _code.Focus();
                }
            };
            _create = root.Q<Button>("create");
            _create.text = UiText.CreateRoom;
            _join = root.Q<Button>("join");
            _join.text = UiText.JoinTable;
            _status = root.Q<Label>("status");
            _server = root.Q<TextField>("server");
            _server.label = UiText.Server;
            _server.value = server ?? "";
            _server.style.display = server == null ? DisplayStyle.None : DisplayStyle.Flex;
            string? Server() => server == null ? null : _server.value;
            _create.clicked += () => WithName(name => create(name, Server()));
            _join.clicked += () => WithName(name => join(name, _code.value, Server()));
            root.Q<Label>("seats-label").text = UiText.SeatsLabel;
            var seatChips = root.Q<VisualElement>("seats");
            for (var n = 4; n <= 6; n++)
            {
                var count = n;
                var chip = new Button { text = count.ToString() };
                chip.AddToClassList("chip");
                chip.AddToClassList("chip--seat");
                chip.clicked += () =>
                {
                    _seats = count;
                    foreach (var other in seatChips.Children())
                    {
                        other.EnableInClassList("chip--on", other == chip);
                    }
                };
                chip.EnableInClassList("chip--on", count == _seats);
                seatChips.Add(chip);
            }

            BuildShelf(root);
            _quick = root.Q<Button>("quick");
            _quick.text = UiText.PlayNow;
            _quick.clicked += () => WithName(name => quickPlay(name, _seats, Server()));
            _hero.clicked += () => WithName(name =>
            {
                var code = _friendsOnline?.Hero?.TableCode;
                if (code != null)
                {
                    join(name, code, Server());
                    return;
                }

                var watching = _friendsOnline?.WatchHero?.TableCode;
                if (watching != null)
                {
                    watch?.Invoke(name, watching, Server());
                }
            });
            if (_friendsOnline != null)
            {
                _friendsOnline.Changed += RefreshFriends;
            }

            Link(root, "missions", UiText.Missions, openMissions, Server);
            Link(root, "leaderboard", UiText.Leaderboard, openLeaderboard, Server);
            Link(root, "shop", UiText.Shop, openShop, Server);
            Link(root, "pass", UiText.SeasonPass, openPass, Server);
            Link(root, "friends", UiText.Friends, openFriends, Server);
            Link(root, "profile", UiText.Profile, openProfile, Server);
            _model.Changed += Refresh;
            RefreshName();
            RefreshProgress();
            RefreshFriends();
            Refresh();
        }

        /// <summary>
        /// The game shelf (docs/design/table-themes.md §3): Thulla with its seat count and Play now,
        /// then the games to come; each tile shows the theme's table with a fan of cards on it.
        /// </summary>
        private static void BuildShelf(VisualElement root)
        {
            root.Q<Label>("game-thulla-name").text = UiText.GameThulla;
            root.Q<Label>("game-thulla-line").text = UiText.GameThullaLine;
            root.Q<Label>("game-rung-name").text = UiText.GameRung;
            root.Q<Label>("game-rung-line").text = UiText.GameRungLine;
            root.Q<Label>("game-rung-soon").text = UiText.ComingSoon;
            var art = root.Q<VisualElement>("game-thulla-art");
            var fan = new[] { "QH", "AS", "7D" };
            for (var i = 0; i < fan.Length; i++)
            {
                var card = new TableKit.CardElement().Show(fan[i]);
                card.AddToClassList("game__card");
                card.style.rotate = new Rotate((i - 1) * 14f);
                card.style.marginTop = i == 1 ? 0f : 24f;
                art.Add(card);
            }

            var backs = root.Q<VisualElement>("game-rung-art");
            for (var i = 0; i < 3; i++)
            {
                var back = new VisualElement { pickingMode = PickingMode.Ignore };
                back.AddToClassList("game__back");
                back.style.rotate = new Rotate((i - 1) * 14f);
                back.style.marginTop = i == 1 ? 0f : 24f;
                backs.Add(back);
            }
        }

        /// <summary>The server keeps names of 2 to 20 characters (GDD 9.4); a shorter one is refused here with the reason.</summary>
        public const int MinNameLength = 2;

        private void WithName(Action<string> go)
        {
            var name = _name.value.Trim();
            if (name.Length > 0 && name.Length < MinNameLength)
            {
                _namePanel.style.display = DisplayStyle.Flex;
                _status.text = UiText.NameTooShort;
                return;
            }

            go(name);
        }

        private void Link(VisualElement root, string name, string text, Action<string, string?>? open, Func<string?> server)
        {
            var button = root.Q<Button>(name);
            button.text = text;
            button.clicked += () => open?.Invoke(_name.value, server());
            if (open == null)
            {
                button.style.display = DisplayStyle.None;
            }
        }

        /// <summary>Offers the table the player left mid-round (GDD 9.8); a null code hides the offer.</summary>
        public void ShowRejoin(string? code, Action? rejoin)
        {
            _onRejoin = rejoin;
            _rejoin.text = code == null ? "" : Strings.Format(UiText.RejoinTable, code);
            _rejoin.style.display = code == null || rejoin == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void Dispose()
        {
            _model.Changed -= Refresh;
            if (_progress != null)
            {
                _progress.Changed -= RefreshProgress;
            }

            if (_friendsOnline != null)
            {
                _friendsOnline.Changed -= RefreshFriends;
            }
        }

        private void RefreshName()
        {
            var name = _name.value;
            _nameLabel.text = name.Length == 0 ? UiText.NameLabel : name;
            _avatarInitial.text = name.Length == 0 ? "?" : name.Substring(0, 1).ToUpperInvariant();
            AvatarTint.Apply(_avatar, name);
        }

        private void RefreshProgress()
        {
            var info = _progress?.Info;
            if (info == null)
            {
                _progressLine.text = "";
                _coins.text = "";
                return;
            }

            var text = Strings.Format(UiText.ProgressLine, info.Level, info.IntoLevel, info.ToNext, _progress!.Coins.ToString("N0"));
            if (info.LoginStreak > 1)
            {
                text += Strings.Format(UiText.StreakSuffix, info.LoginStreak);
            }

            _progressLine.text = text;
            _coins.text = "◉ " + _progress.Coins.ToString("N0");
        }

        /// <summary>The party bar (design §4): friends online as avatars with a presence dot, the hero under it.</summary>
        private void RefreshFriends()
        {
            var hero = _friendsOnline?.Hero;
            var watchHero = _friendsOnline?.WatchHero;
            _hero.style.display = hero == null && watchHero == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (hero != null)
            {
                _hero.text = Strings.Format(UiText.JoinFriendTable, hero.Name, hero.OpenSeats);
            }
            else if (watchHero != null)
            {
                _hero.text = Strings.Format(UiText.WatchFriendTable, watchHero.Name);
            }

            _party.Clear();
            var online = _friendsOnline?.Online;
            if (online == null || online.Count == 0)
            {
                _online.text = _friendsOnline == null ? "" : UiText.PartyEmpty;
                return;
            }

            var names = new System.Text.StringBuilder();
            foreach (var friend in online)
            {
                var avatar = new VisualElement();
                avatar.AddToClassList("seat__avatar");
                avatar.AddToClassList("party__avatar");
                AvatarTint.Apply(avatar, friend.Name);
                var initial = new Label(friend.Name.Length == 0 ? "?" : friend.Name.Substring(0, 1).ToUpperInvariant());
                initial.AddToClassList("seat__initial");
                initial.AddToClassList("party__initial");
                avatar.Add(initial);
                var dot = new VisualElement();
                dot.AddToClassList("party__dot");
                dot.EnableInClassList("party__dot--busy", friend.TableCode != null);
                avatar.Add(dot);
                avatar.tooltip = friend.Name;
                _party.Add(avatar);
                if (names.Length > 0)
                {
                    names.Append(" · ");
                }

                names.Append(friend.Name).Append(" (").Append(UiText.Presence(friend.State)).Append(')');
            }

            _online.text = names.ToString();
        }

        /// <summary>The server can rest Quick Play (remote config, maintenance); the note says why.</summary>
        public void SetQuickPlay(bool available, string? note)
        {
            _quickAvailable = available;
            _note = note;
            Refresh();
        }

        private void Refresh()
        {
            _create.SetEnabled(!_model.Busy);
            _join.SetEnabled(!_model.Busy);
            _quick.SetEnabled(!_model.Busy && _quickAvailable);
            _status.text = _model.Busy ? UiText.Connecting : _model.ErrorCode != null ? UiText.Error(_model.ErrorCode) : _note ?? "";
        }
    }
}
