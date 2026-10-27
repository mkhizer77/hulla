using System;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using Hulla.Presentation.Theme;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Home
{
    /// <summary>
    /// Home (GDD 14.10, docs/design/basant-home.md): the gear, who you are with your level and coins,
    /// friends online with Invite, a friend's table to join, the Thulla card (seat size, Casual or
    /// Ranked, Play now), Create Room and Join by Code, the daily reward and Rung to come. Everything
    /// sits on one stage drawn at 1080 x 1736 and scaled to fit, so it keeps the concept's layout.
    /// </summary>
    public sealed class HomeView : IDisposable
    {
        /// <summary>The stage's design size; the dock sits under it.</summary>
        public const float StageWidth = 1080f;

        public const float StageHeight = 1736f;

        /// <summary>How many friends the panel shows before Invite.</summary>
        public const int PartySize = 4;

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
        private readonly HomeFriendsViewModel? _friendsOnline;
        private readonly ProgressViewModel? _progress;
        private readonly Label _level;
        private readonly Label _coins;
        private readonly Button _hero;
        private readonly Label _heroText;
        private readonly VisualElement _heroJoin;
        private readonly Button _rejoin;
        private readonly Label _rejoinText;
        private Action? _onRejoin;
        private readonly VisualElement _party;
        private readonly Button _quick;
        private bool _quickAvailable = true;
        private string? _note;
        private int _seats = 4;
        private bool _ranked;

        /// <param name="server">Kept for the callers' signature: the server field lives in Settings now, so a null is passed.</param>
        public HomeView(VisualElement root, HomeViewModel model, string? server, Action<string, string?> create, Action<string, string, string?> join, Action<string, int, string?> quickPlay, Action? openSettings = null, Action<string, string?>? openFriends = null, Action<string, string?>? openProfile = null, HomeFriendsViewModel? friendsOnline = null, Action? howToPlay = null, string? howToPlayText = null, Action<string, string, string?>? watch = null, ProgressViewModel? progress = null, Action<string, string?>? openMissions = null, Action<string, string?>? openLeaderboard = null, Action<string, string?>? openShop = null, Action<string, string?>? openPass = null, Action<string, string?>? openRanked = null)
        {
            _model = model;
            _progress = progress;
            Fit(root);
            _level = root.Q<Label>("level");
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
            _friendsOnline = friendsOnline;
            _hero = root.Q<Button>("hero");
            _heroText = root.Q<Label>("hero-text");
            _heroJoin = root.Q<VisualElement>("hero-join");
            TextArt.Mark(root.Q<Label>("hero-join-label"), "join").text = UiText.JoinTable;
            _rejoin = root.Q<Button>("rejoin");
            _rejoinText = root.Q<Label>("rejoin-text");
            _rejoin.style.display = DisplayStyle.None;
            _rejoin.clicked += () => _onRejoin?.Invoke();
            _party = root.Q<VisualElement>("party");
            TextArt.Mark(root.Q<Label>("friends-title"), "friends-online").text = UiText.FriendsOnline;
            TextArt.Mark(root.Q<Label>("invite-label"), "invite").text = UiText.Invite;
            var settings = root.Q<Button>("settings");
            settings.text = "";
            settings.tooltip = UiText.Settings;
            settings.clicked += () => openSettings?.Invoke();
            settings.style.display = openSettings == null ? DisplayStyle.None : DisplayStyle.Flex;
            _name = root.Q<TextField>("name");
            _name.label = UiText.NameLabel;
            _name.value = model.LastName;
            _name.maxLength = 16;
            _name.RegisterValueChangedCallback(_ => RefreshName());

            // The name is asked once (GDD 17.2); afterwards it sits in the header, the avatar opens the
            // profile and a tap on the name edits it.
            _namePanel.style.display = model.LastName.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _nameLabel.RegisterCallback<ClickEvent>(_ =>
                _namePanel.style.display = _namePanel.style.display == DisplayStyle.Flex ? DisplayStyle.None : DisplayStyle.Flex);
            _avatar.RegisterCallback<ClickEvent>(_ => openProfile?.Invoke(_name.value, null));
            _code = root.Q<TextField>("code");
            _code.label = UiText.CodeLabel;
            _code.maxLength = 8;
            _joinPanel = root.Q<VisualElement>("join-panel");
            _joinPanel.style.display = DisplayStyle.None;
            var joinToggle = root.Q<Button>("join-toggle");
            joinToggle.text = "";
            TextArt.Mark(root.Q<Label>("join-toggle-label"), "join-by-code").text = UiText.JoinByCode;
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
            _create.text = "";
            TextArt.Mark(root.Q<Label>("create-label"), "create-room").text = UiText.CreateRoom;
            _join = root.Q<Button>("join");
            _join.text = UiText.JoinTable;
            _status = root.Q<Label>("status");
            _create.clicked += () => WithName(name => create(name, null));
            _join.clicked += () => WithName(name => join(name, _code.value, null));
            root.Q<Button>("invite").clicked += () => openFriends?.Invoke(_name.value, null);
            BuildCard(root);
            BuildSeats(root);
            BuildMode(root, openRanked != null);
            _quick = root.Q<Button>("quick");
            _quick.text = "";
            TextArt.Mark(root.Q<Label>("quick-label"), "play-now").text = UiText.PlayNow;
            _quick.clicked += () => WithName(name =>
            {
                if (_ranked && openRanked != null)
                {
                    openRanked(name, null);
                    return;
                }

                quickPlay(name, _seats, null);
            });
            _hero.clicked += () => WithName(name =>
            {
                var code = _friendsOnline?.Hero?.TableCode;
                if (code != null)
                {
                    join(name, code, null);
                    return;
                }

                var watching = _friendsOnline?.WatchHero?.TableCode;
                if (watching != null)
                {
                    watch?.Invoke(name, watching, null);
                }
            });
            if (_friendsOnline != null)
            {
                _friendsOnline.Changed += RefreshFriends;
            }

            Link(root, "missions", UiText.Missions, openMissions);
            Link(root, "leaderboard", UiText.Leaderboard, openLeaderboard);
            Link(root, "shop", UiText.Shop, openShop);
            Link(root, "pass", UiText.SeasonPass, openPass);
            Link(root, "friends", UiText.Friends, openFriends);
            Link(root, "profile", UiText.Profile, openProfile);
            _model.Changed += Refresh;
            RefreshName();
            RefreshProgress();
            RefreshFriends();
            Refresh();
        }

        /// <summary>Ranked is on: Play now opens Ranked instead of Quick Play.</summary>
        public bool RankedChosen => _ranked;

        /// <summary>The seat count Quick Play asks for.</summary>
        public int Seats => _seats;

        /// <summary>The stage keeps its design size and scales to the room above the dock, centred.</summary>
        private static void Fit(VisualElement root)
        {
            var holder = root.Q<VisualElement>("stage-holder");
            var stage = root.Q<VisualElement>("stage");
            holder.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var size = holder.contentRect.size;
                if (size.x <= 0f || size.y <= 0f)
                {
                    return;
                }

                var scale = Math.Min(size.x / StageWidth, size.y / StageHeight);
                stage.style.scale = new Scale(new UnityEngine.Vector3(scale, scale, 1f));
            });
        }

        /// <summary>The Thulla card: the game's name in its own lettering, the line under it and Rung to come.</summary>
        private static void BuildCard(VisualElement root)
        {
            root.Q<Label>("game-thulla-name").text = UiText.GameThulla;
            TextArt.Mark(root.Q<Label>("game-thulla-line"), "tagline-thulla").text = UiText.GameThullaLine;
            root.Q<Label>("game-rung-name").text = UiText.GameRung;
            TextArt.Mark(root.Q<Label>("game-rung-soon"), "coming-soon").text = UiText.ComingSoon;
        }

        /// <summary>Four enamel medallions, 3 to 6; the chosen one wears the gold rim.</summary>
        private void BuildSeats(VisualElement root)
        {
            TextArt.Mark(root.Q<Label>("seats-label"), "seat-size").text = UiText.SeatSize;
            var seats = root.Q<VisualElement>("seats");
            for (var n = 3; n <= 6; n++)
            {
                var count = n;
                var medal = new Button { name = "seat-" + count };
                medal.AddToClassList("home__medal");
                medal.AddToClassList("home__medal--" + count);
                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                face.AddToClassList("home__face");
                face.Add(TextArt.Mark(new Label(Strings.Format("{0}", count)) { pickingMode = PickingMode.Ignore }, "digit-" + count));
                var ring = new VisualElement { pickingMode = PickingMode.Ignore };
                ring.AddToClassList("home__medal-ring");
                face.Add(ring);
                medal.Add(face);
                medal.clicked += () =>
                {
                    _seats = count;
                    foreach (var other in seats.Children())
                    {
                        other.EnableInClassList("home__medal--on", other == medal);
                    }
                };
                medal.EnableInClassList("home__medal--on", count == _seats);
                seats.Add(medal);
            }
        }

        /// <summary>Casual or Ranked; Ranked plays four seats, so the medallions rest while it is on.</summary>
        private void BuildMode(VisualElement root, bool rankedAvailable)
        {
            var mode = root.Q<VisualElement>("mode");
            mode.style.display = rankedAvailable ? DisplayStyle.Flex : DisplayStyle.None;
            var casual = TextArt.Mark(root.Q<Label>("mode-casual"), "casual");
            var ranked = TextArt.Mark(root.Q<Label>("mode-ranked"), "ranked");
            var toggle = root.Q<VisualElement>("mode-switch");
            casual.text = UiText.Casual;
            ranked.text = UiText.Ranked;
            void Set(bool on)
            {
                _ranked = on;
                toggle.EnableInClassList("home__switch--on", on);
                casual.EnableInClassList("home__mode-label--on", !on);
                ranked.EnableInClassList("home__mode-label--on", on);
                root.Q<VisualElement>("seats").SetEnabled(!on);
            }

            toggle.RegisterCallback<ClickEvent>(_ => Set(!_ranked));
            casual.RegisterCallback<ClickEvent>(_ => Set(false));
            ranked.RegisterCallback<ClickEvent>(_ => Set(true));
            Set(false);
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

        private void Link(VisualElement root, string name, string text, Action<string, string?>? open)
        {
            var button = root.Q<Button>(name);
            button.text = text;
            button.clicked += () => open?.Invoke(_name.value, null);
        }

        /// <summary>
        /// Offers the table the player left while their seat is held (GDD 9.8) in the banner's place; a
        /// null code hides the offer, an empty one is a matchmade table without a room code.
        /// </summary>
        public void ShowRejoin(string? code, Action? rejoin)
        {
            _onRejoin = rejoin;
            _rejoinText.text = code == null ? "" : code.Length == 0 ? UiText.RejoinGame : Strings.Format(UiText.RejoinTable, code);
            _rejoin.style.display = code == null || rejoin == null ? DisplayStyle.None : DisplayStyle.Flex;
            RefreshFriends();
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
            _level.text = info == null ? "" : Strings.Format("{0}", info.Level);
            _level.style.display = info == null ? DisplayStyle.None : DisplayStyle.Flex;
            _coins.text = info == null ? "" : _progress!.Coins.ToString("N0", Strings.Culture);
        }

        /// <summary>Friends online as faces with a green dot, the table banner under them when a friend has seats open.</summary>
        private void RefreshFriends()
        {
            var hero = _friendsOnline?.Hero;
            var watchHero = _friendsOnline?.WatchHero;
            var rejoining = _rejoin.style.display == DisplayStyle.Flex;
            _hero.style.display = rejoining || (hero == null && watchHero == null) ? DisplayStyle.None : DisplayStyle.Flex;
            _heroJoin.style.display = hero != null ? DisplayStyle.Flex : DisplayStyle.None;
            _hero.text = "";
            if (hero != null)
            {
                _heroText.text = Strings.Format(UiText.FriendTableBanner, hero.Name, hero.OpenSeats);
            }
            else if (watchHero != null)
            {
                _heroText.text = Strings.Format(UiText.WatchFriendTable, watchHero.Name);
            }

            _hero.tooltip = _heroText.text;
            _party.Clear();
            var online = _friendsOnline?.Online;
            var shown = online == null ? 0 : Math.Min(online.Count, PartySize);
            for (var i = 0; i < shown; i++)
            {
                var friend = online![i];
                var face = new VisualElement { tooltip = friend.Name + " (" + UiText.Presence(friend.State) + ")" };
                face.AddToClassList("party__face");
                var avatar = new VisualElement { pickingMode = PickingMode.Ignore };
                avatar.AddToClassList("seat__avatar");
                avatar.AddToClassList("party__avatar");
                AvatarTint.Apply(avatar, friend.Name);
                var initial = new Label(friend.Name.Length == 0 ? "?" : friend.Name.Substring(0, 1).ToUpperInvariant());
                initial.AddToClassList("seat__initial");
                initial.AddToClassList("party__initial");
                avatar.Add(initial);
                face.Add(avatar);
                var frame = new VisualElement { pickingMode = PickingMode.Ignore };
                frame.AddToClassList("party__frame");
                face.Add(frame);
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("party__dot");
                dot.EnableInClassList("party__dot--busy", friend.TableCode != null);
                face.Add(dot);
                _party.Add(face);
            }
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
