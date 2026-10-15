using System;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.TableKit;
using Hulla.Presentation.Text;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Screens.Home
{
    /// <summary>
    /// Home (GDD 14.10, docs/design/home-concept.md): the hanging name sign with the level badge
    /// and the coin pouch, the party bar and the hero when friends are about, the Thulla card (seat
    /// count, Casual or Ranked, Play now), Create a room and Join by code on boards, and the reward
    /// chest. Friends, Inventory and Settings live on the bottom navigation; the badge opens Profile.
    /// </summary>
    public sealed class HomeView : IDisposable
    {
        private readonly HomeViewModel _model;
        private readonly TextField _name;
        private readonly Label _nameLabel;
        private readonly VisualElement _namePanel;
        private readonly Button _level;
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
        private bool _ranked;
        private string? _note;
        private int _seats = 4;

        /// <param name="server">The server to show in a development build, or null to hide the field.</param>
        public HomeView(VisualElement root, HomeViewModel model, string? server, Action<string, string?> create, Action<string, string, string?> join, Action<string, int, string?> quickPlay, Action? openSettings = null, Action<string, string?>? openFriends = null, Action<string, string?>? openProfile = null, HomeFriendsViewModel? friendsOnline = null, Action? howToPlay = null, string? howToPlayText = null, Action<string, string, string?>? watch = null, ProgressViewModel? progress = null, Action<string, string?>? openMissions = null, Action<string, string?>? openLeaderboard = null, Action<string, string?>? openShop = null, Action<string, string?>? openPass = null, Action<string, string?>? openRanked = null)
        {
            _model = model;
            _progress = progress;
            _progressLine = root.Q<Label>("progress");
            _coins = root.Q<Label>("coins");
            _level = root.Q<Button>("level");
            _nameLabel = root.Q<Label>("name-label");
            _namePanel = root.Q<VisualElement>("name-panel");
            if (_progress != null)
            {
                _progress.Changed += RefreshProgress;
            }

            var how = root.Q<Button>("how-to-play");
            how.text = "";
            how.tooltip = howToPlayText ?? "";
            how.AddToClassList("button--icon");
            how.Add(new LineIcon(LineIconKind.Book));
            how.clicked += () => howToPlay?.Invoke();
            how.style.display = howToPlay == null ? DisplayStyle.None : DisplayStyle.Flex;
            _friendsOnline = friendsOnline;
            _hero = root.Q<Button>("hero");
            _rejoin = root.Q<Button>("rejoin");
            _rejoin.style.display = DisplayStyle.None;
            _rejoin.clicked += () => _onRejoin?.Invoke();
            _online = root.Q<Label>("online");
            _party = root.Q<VisualElement>("party");
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
            for (var n = 3; n <= 6; n++)
            {
                var count = n;
                var chip = new Button { text = count.ToString(), tooltip = UiText.SeatsLabel };
                chip.AddToClassList("chip");
                chip.AddToClassList("chip--seat");
                chip.AddToClassList("medal");
                chip.AddToClassList("medal--" + count);
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
            _quick.text = UiText.PlayNow.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
            _quick.clicked += () => WithName(name =>
            {
                if (_ranked && openRanked != null)
                {
                    openRanked(name, Server());
                    return;
                }

                quickPlay(name, _seats, Server());
            });
            BuildMode(root, openRanked != null);
            _level.clicked += () => openProfile?.Invoke(_name.value, Server());
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
        /// The Thulla card (docs/design/home-concept.md): the title and its line over a fan of cards
        /// on the felt, the seat medallions on the fan, Casual or Ranked, and Play now on its plaque.
        /// </summary>
        private static void BuildShelf(VisualElement root)
        {
            root.Q<Label>("game-thulla-name").text = UiText.GameThulla.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
            root.Q<Label>("game-thulla-line").text = UiText.GameThullaLine;
            var art = root.Q<VisualElement>("game-thulla-art");
            var fan = new[] { "AH", "KD", "AS", "AC", "6S", "AD", "KS" };
            for (var i = 0; i < fan.Length; i++)
            {
                var card = new TableKit.CardElement().Show(fan[i]);
                card.AddToClassList("game__card");
                var off = i - (fan.Length - 1) / 2f;
                card.style.rotate = new Rotate(off * 9f);
                card.style.marginTop = Math.Abs(off) * Math.Abs(off) * 5f;
                art.Add(card);
            }
        }

        /// <summary>
        /// Casual or Ranked on the card: a tap on the switch or either word flips it; Play now then
        /// starts Quick Play or opens Ranked. Hidden when there is nowhere to send a ranked player.
        /// </summary>
        private void BuildMode(VisualElement root, bool rankedAvailable)
        {
            var mode = root.Q<VisualElement>("mode");
            mode.style.display = rankedAvailable ? DisplayStyle.Flex : DisplayStyle.None;
            var casual = root.Q<Label>("mode-casual");
            var ranked = root.Q<Label>("mode-ranked");
            var toggle = root.Q<VisualElement>("mode-switch");
            casual.text = UiText.Casual;
            ranked.text = UiText.Ranked;
            void Set(bool on)
            {
                _ranked = on;
                toggle.EnableInClassList("switch--on", on);
                casual.EnableInClassList("game__mode-label--on", !on);
                ranked.EnableInClassList("game__mode-label--on", on);
                root.Q<VisualElement>("seats").SetEnabled(!on);
            }

            toggle.RegisterCallback<ClickEvent>(_ => Set(!_ranked));
            casual.RegisterCallback<ClickEvent>(_ => Set(false));
            ranked.RegisterCallback<ClickEvent>(_ => Set(true));
            Set(false);
        }

        /// <summary>Ranked is on; Play now opens Ranked instead of Quick Play.</summary>
        public bool RankedChosen => _ranked;

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

        /// <summary>
        /// Offers the table the player left while their seat is held (GDD 9.8); a null code hides the
        /// offer, an empty one is a matchmade table without a room code.
        /// </summary>
        public void ShowRejoin(string? code, Action? rejoin)
        {
            _onRejoin = rejoin;
            _rejoin.text = code == null ? "" : code.Length == 0 ? UiText.RejoinGame : Strings.Format(UiText.RejoinTable, code);
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
        }

        private void RefreshProgress()
        {
            var info = _progress?.Info;
            if (info == null)
            {
                _progressLine.text = "";
                _coins.text = "";
                _level.text = "";
                return;
            }

            var text = Strings.Format(UiText.ProgressLine, info.Level, info.IntoLevel, info.ToNext, _progress!.Coins.ToString("N0"));
            if (info.LoginStreak > 1)
            {
                text += Strings.Format(UiText.StreakSuffix, info.LoginStreak);
            }

            _progressLine.text = text;
            _level.text = info.Level.ToString();
            _level.tooltip = text;
            _coins.text = _progress.Coins.ToString("N0");
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
            var anyone = online != null && online.Count > 0;
            _party.style.display = anyone ? DisplayStyle.Flex : DisplayStyle.None;
            _online.style.display = anyone ? DisplayStyle.Flex : DisplayStyle.None;
            if (online == null || online.Count == 0)
            {
                _online.text = "";
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
