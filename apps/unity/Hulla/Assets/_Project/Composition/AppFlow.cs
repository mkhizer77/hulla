using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Application.Tutorial;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using Hulla.Protocol;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Screens.Friends;
using Hulla.Presentation.Screens.Lobby;
using Hulla.Presentation.Screens.Achievements;
using Hulla.Presentation.Screens.Leaderboard;
using Hulla.Presentation.Screens.Ranked;
using Hulla.Presentation.Screens.Pass;
using Hulla.Presentation.Screens.Phone;
using Hulla.Presentation.Screens.Shop;
using Hulla.Presentation.Screens.Missions;
using Hulla.Presentation.Screens.Profile;
using Hulla.Presentation.Screens.QuickPlay;
using Hulla.Presentation.Screens.Rulebook;
using Hulla.Presentation.Screens.Settings;
using Hulla.Presentation.Share;
using Hulla.Presentation.Theme;
using Hulla.Application.Settings;
using Hulla.Application.Social;
using Hulla.Presentation.Text;
using UnityEngine;

namespace Hulla.Composition
{
    /// <summary>
    /// Moves between the home, lobby and table screens. Which screen shows follows the table model:
    /// a round in play or a result on screen means the table, otherwise the lobby; a closed table or
    /// a dropped socket returns home (reconnection is #19).
    /// </summary>
    public sealed class AppFlow : IDisposable
    {
        private enum Screen
        {
            None,
            Home,
            Lobby,
            Table,
            QuickPlay,
        }

        private const float ReconnectWindowSeconds = 30f;
        private const float RetrySeconds = 2f;

        /// <summary>The name the player last signed in with, offered next launch.</summary>
        private const string NamePrefKey = "hulla.name";

        /// <summary>The private table the player is at, kept so they can rejoin after leaving mid-round.</summary>
        private const string LastTableCodeKey = "hulla.lastTable.code";
        private const string LastTableAtKey = "hulla.lastTable.at";

        /// <summary>A room lives 30 minutes after its last human leaves (GDD 7.5).</summary>
        private const long RejoinWindowSeconds = 30 * 60;


        private readonly AppScope _app;
        private readonly ShellView _shell;
        private Screen _screen;
        private IDisposable? _view;
        private AnimationScheduler? _scheduler;
        private ThullaTableView? _tableView;
        private bool _reconnecting;
        private SettingsView? _settingsView;
        private bool _updateAsked;
        private bool _contentSynced;
        private PhoneView? _phoneView;
        private bool _tutorialRefresher;
        private LobbyView? _lobbyView;
        private string _friendsForRound = "";
        private readonly BigThullaOffer _bigThulla = new BigThullaOffer();
        private DailyRewardCard? _daily;
        private AnalyticsTracker? _tracker;

        /// <summary>The table's placeholder sounds (GDD 16.2); the SFX setting sets its level.</summary>
        private readonly Hulla.Presentation.Audio.SoundPlayer _sounds = new Hulla.Presentation.Audio.SoundPlayer();

        /// <summary>When the invite link being joined arrived, for invite_seated.</summary>
        private float? _inviteLinkAt;
        private ScriptedTableGateway? _tutorial;
        private TableClient? _tutorialClient;

        /// <summary>1 once the player has answered "Do you know Bhabhi?" or finished the tutorial.</summary>
        private const string TutorialSeenPrefKey = "hulla.tutorial_seen";

        public AppFlow(AppScope app, ShellView shell)
        {
            _app = app;
            _shell = shell;
            _shell.Ticked += Tick;
            _shell.NavChosen += OnNav;
            _app.Settings.Changed += ApplySettings;
            _app.QuickPlay.Found += OnQuickPlayFound;
            _app.Phone.Switched += OnPhoneSwitched;
            _app.TableChat.Received += OnText;
            _app.Tables.Disconnected += OnDisconnected;
            _app.Sessions.Notified += OnNotice;
            _app.Links.InviteOpened += OnInviteLink;
            _app.Links.ReferralOpened += OnReferralLink;
            _app.Friends.Changed += RefreshBadges;
            _app.Pass.Changed += RefreshBadges;
        }

        /// <summary>
        /// <c>-hulla-name &lt;name&gt;</c> on the command line, so several local instances (each with its own
        /// <c>-hulla-device</c>) sit down under different names; null without it.
        /// </summary>
        private static string? NameFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-hulla-name" && args[i + 1].Trim().Length > 0)
                {
                    return args[i + 1].Trim();
                }
            }

            return null;
        }

        /// <summary>The bottom navigation (GDD 14.1); every tab opens from wherever the player is.</summary>
        private void OnNav(Hulla.Presentation.Navigation.NavTab tab)
        {
            var name = _app.Home.LastName;
            switch (tab)
            {
                case Hulla.Presentation.Navigation.NavTab.Friends:
                    OnFriends(name, null);
                    break;
                case Hulla.Presentation.Navigation.NavTab.Inventory:
                    OnInventory(name, null);
                    break;
                case Hulla.Presentation.Navigation.NavTab.Settings:
                    OpenSettings();
                    break;
                default:
                    ShowHome();
                    break;
            }
        }

        /// <summary>Badges on the tabs: requests waiting on Friends, claimable pass tiers on Inventory.</summary>
        private void RefreshBadges()
        {
            _shell.SetNavBadge(Hulla.Presentation.Navigation.NavTab.Friends, _app.Friends.Received.Count);
            _shell.SetNavBadge(Hulla.Presentation.Navigation.NavTab.Inventory, _app.Pass.Claimable);
        }

        /// <summary>The Inventory tab: the shop, the pass, missions, achievements and the leaderboard.</summary>
        private void OnInventory(string name, string? server)
        {
            CloseView();
            _screen = Screen.Home;
            _view = new Hulla.Presentation.Screens.Inventory.InventoryView(_shell.Show(_shell.Catalog.Inventory), new[]
            {
                new Hulla.Presentation.Screens.Inventory.InventoryView.Entry("shop", UiText.Shop, () => OnShop(name, server)),
                new Hulla.Presentation.Screens.Inventory.InventoryView.Entry("pass", UiText.SeasonPass, () => OnPass(name, server), _app.Pass.Claimable),
                new Hulla.Presentation.Screens.Inventory.InventoryView.Entry("missions", UiText.Missions, () => OnMissions(name, server)),
                new Hulla.Presentation.Screens.Inventory.InventoryView.Entry("achievements", UiText.Achievements, () => OpenAchievements(name, server)),
                new Hulla.Presentation.Screens.Inventory.InventoryView.Entry("leaderboard", UiText.Leaderboard, () => OnLeaderboard(name, server)),
            });
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Inventory);
        }

        private async Awaitable<IReadOnlyList<Hulla.Application.Social.FriendEntry>> FriendsToInviteAsync()
        {
            await _app.Friends.RefreshAsync(_app.Lifetime);
            return _app.Friends.Friends;
        }

        /// <returns>Null when the invite went, else the skip reason or error code.</returns>
        private async Awaitable<string?> InviteAsync(string userId)
        {
            var result = await _app.NotificationsService.InviteAsync(new[] { userId }, _app.Lifetime);
            if (!result.IsOk)
            {
                return result.Error.Code;
            }

            if (result.Value.Sent.Count > 0)
            {
                return null;
            }

            return result.Value.Skipped.TryGetValue(userId, out var reason) ? reason : "unknown";
        }

        /// <summary>
        /// A friend's invite (GDD 10.5): off a table it asks to join; at a table it only shows the code
        /// briefly, so a round isn't interrupted.
        /// </summary>
        private async void OnInvite(string content)
        {
            var invite = Hulla.Infrastructure.Nakama.ServerNotices.Invite(content);
            if (invite == null)
            {
                return;
            }

            var (from, code, _) = invite.Value;
            var text = Strings.Format(UiText.InviteReceived, from, code);
            if (_screen == Screen.Table || _screen == Screen.Lobby)
            {
                _shell.SetBanner(text);
                await Awaitable.WaitForSecondsAsync(5f);
                if (UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(_shell.Root, "shell-banner")?.text == text)
                {
                    _shell.SetBanner(null);
                }

                return;
            }

            _shell.ShowDialog(text, UiText.JoinTable, UiText.NotNow, () => OnJoin(_app.Home.LastName, code, null), () => { });
        }

        /// <summary>
        /// An invite link (GDD 10.5): it joins the table straight away, unless the player is already at
        /// one; then it only names the waiting table, so a round isn't interrupted.
        /// </summary>
        private async void OnInviteLink(string code, float receivedAt)
        {
            _app.Analytics.Track(AnalyticsEvents.InviteOpened(Time.realtimeSinceStartup - receivedAt));
            if (_screen == Screen.Table || _screen == Screen.Lobby)
            {
                var text = Strings.Format(UiText.InviteLinkWhileSeated, code);
                _shell.SetBanner(text);
                await Awaitable.WaitForSecondsAsync(5f);
                if (UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(_shell.Root, "shell-banner")?.text == text)
                {
                    _shell.SetBanner(null);
                }

                return;
            }

            if (_screen == Screen.QuickPlay)
            {
                await _app.QuickPlay.CancelAsync();
            }

            _inviteLinkAt = receivedAt;
            OnJoin(_app.Home.LastName, code, null);
        }

        /// <summary>
        /// A referral link from a share card (GDD 10.9): it offers to add the sharer as a friend. At a
        /// table it only says so, so a round isn't interrupted.
        /// </summary>
        private void OnReferralLink(string friendCode)
        {
            if (friendCode == _app.Profile.Profile?.FriendCode)
            {
                return;
            }

            if (_screen == Screen.Table || _screen == Screen.Lobby)
            {
                ShowBriefly(UiText.ReferralWhileSeated);
                return;
            }

            _shell.ShowDialog(Strings.Format(UiText.ReferralAsk, friendCode), UiText.AddFriend, UiText.NotNow, () => AddReferrer(friendCode), () => { });
        }

        private async void AddReferrer(string friendCode)
        {
            if (!await SignedInAsync(_app.Home.LastName, null))
            {
                return;
            }

            _app.Analytics.Track(AnalyticsEvents.FriendAdd("referral"));
            await _app.Friends.AddByCodeAsync(friendCode, _app.Lifetime);
            ShowBriefly(_app.Friends.RequestSent ? UiText.RequestSent : UiText.Error(_app.Friends.ErrorCode ?? ""));
        }

        private async void ShowBriefly(string text)
        {
            _shell.SetBanner(text);
            await Awaitable.WaitForSecondsAsync(5f);
            if (UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(_shell.Root, "shell-banner")?.text == text)
            {
                _shell.SetBanner(null);
            }
        }

        /// <summary>An unlock shows briefly at the top of whatever screen is open (GDD 12.7).</summary>
        private async void OnNotice(string subject, string content)
        {
            if (subject == Hulla.Infrastructure.Nakama.ServerNotices.TableInvite)
            {
                OnInvite(content);
                return;
            }

            if (subject != Hulla.Infrastructure.Nakama.ServerNotices.AchievementUnlocked)
            {
                return;
            }

            foreach (var id in Hulla.Infrastructure.Nakama.ServerNotices.UnlockedIds(content))
            {
                var text = Strings.Format(UiText.AchievementUnlocked, AchievementText.Name(id, "", 0));
                _shell.SetBanner(text);
                await Awaitable.WaitForSecondsAsync(4f);
                if (UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(_shell.Root, "shell-banner")?.text == text)
                {
                    _shell.SetBanner(null);
                }
            }
        }

        public async void Start()
        {
            ApplySettings();
            // A held invite or referral link opens its own dialog once the flow is ready.
            var invited = _app.Links.HasPending;
            _app.Analytics.Track(AnalyticsEvents.AppOpen(true, _app.Links.PendingType));
            _app.Home.LastName = NameFromCommandLine() ?? PlayerPrefs.GetString(Hulla.Infrastructure.Settings.DeviceProfile.Key(NamePrefKey), "");
            var returning = _app.Home.LastName.Length > 0;
            if (!returning)
            {
                _app.Home.LastName = GeneratedName();
            }

            ShowHome();
            // A player who came in on a link goes to the table or the friend request first; the tutorial
            // and the consent question wait for the next launch.
            if (!invited && PlayerPrefs.GetInt(Hulla.Infrastructure.Settings.DeviceProfile.Key(TutorialSeenPrefKey), 0) == 0 && !returning)
            {
                _shell.ShowDialog(TutorialText.Gate, TutorialText.GateYes, TutorialText.GateNo, OfferRefresher, () => StartTutorial(false));
            }
            else if (!invited && _app.Settings.AnalyticsConsent == null)
            {
                AskConsent();
            }
            // Joining signs in, so a quiet sign-in alongside it would race.
            if (returning && !invited)
            {
                SignInQuietly();
            }

            try
            {
                await _app.Content.OpenAsync(_app.Lifetime);
                await _app.Chat.LoadAsync(_app.Lifetime);
                await _app.Presets.LoadAsync(_app.Lifetime);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _app.Links.Ready();
            OpenFromPush();
        }

        public void Dispose()
        {
            _shell.Ticked -= Tick;
            _shell.NavChosen -= OnNav;
            _app.Friends.Changed -= RefreshBadges;
            _app.Pass.Changed -= RefreshBadges;
            _app.Settings.Changed -= ApplySettings;
            _app.QuickPlay.Found -= OnQuickPlayFound;
            _app.Phone.Switched -= OnPhoneSwitched;
            _phoneView?.Dispose();
            _app.Links.InviteOpened -= OnInviteLink;
            _app.Links.ReferralOpened -= OnReferralLink;
            _app.TableChat.Received -= OnText;
            _app.Tables.Disconnected -= OnDisconnected;
            CloseView();
            _sounds.Dispose();
        }

        private void ShowHome()
        {
            CloseView();
            _screen = Screen.Home;
            var root = _shell.Show(_shell.Catalog.Home);
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Home);
            var server = Debug.isDebugBuild ? _app.Config.HostAndPort : null;
            var home = new HomeView(root, _app.Home, server, OnCreate, OnJoin, OnQuickPlay, OpenSettings, OnFriends, OnProfile, _app.HomeFriends, () => StartTutorial(false), TutorialText.HowToPlay, OnWatch, _app.Progress, OnMissions, OnLeaderboard, OnShop, OnPass, OnRanked);
            _view = home;
            home.SetQuickPlay(_app.RemoteConfig.QuickPlay, QuickPlayNote());
            var left = LastTableCode();
            home.ShowRejoin(left, left == null ? null : () => Rejoin(home, left));
            RefreshRejoin(home);
            _daily = new DailyRewardCard(UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.VisualElement>(root, "daily"), _app.DailyReward, ClaimDailyReward);
            _app.HomeFriends.Active = _app.Sessions.Current != null;
            _app.HomeFriends.RefreshSoon();
            if (_app.Sessions.Current != null)
            {
                _ = _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
                _ = _app.DailyReward.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
            }
        }

        private async void ClaimDailyReward()
        {
            if (await _app.DailyReward.ClaimAsync(_app.Lifetime))
            {
                await _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
            }
        }

        private async void OnCreate(string name, string? server)
        {
            ApplyServer(server);
            var ticket = await _app.Home.CreateAsync(name, _app.Lifetime);
            if (ticket.IsOk)
            {
                _app.Analytics.Track(AnalyticsEvents.TableCreate("private"));
            }

            await EnterAsync(ticket);
        }

        private async void OnJoin(string name, string code, string? server)
        {
            ApplyServer(server);
            var ticket = await _app.Home.JoinAsync(name, code, _app.Lifetime);
            await EnterAsync(ticket);
        }

        /// <summary>Watch a friend's round (GDD 7.7): join by the table's code as a spectator.</summary>
        private async void OnWatch(string name, string code, string? server)
        {
            ApplyServer(server);
            var ticket = await _app.Home.JoinAsync(name, code, _app.Lifetime);
            await EnterAsync(ticket.IsOk ? Result<TableTicket>.Ok(ticket.Value.Watching()) : ticket);
        }

        private async void OnQuickPlay(string name, int seats, string? server)
        {
            ApplyServer(server);
            var signedIn = await _app.Home.SignInAsync(name, _app.Lifetime);
            if (!signedIn.IsOk)
            {
                return;
            }

            CloseView();
            _screen = Screen.QuickPlay;
            _shell.HideNav();
            _view = new QuickPlayView(_shell.Show(_shell.Catalog.QuickPlay), _app.QuickPlay, CancelQuickPlay);
            await _app.QuickPlay.StartAsync(seats, _app.Lifetime);
        }

        private async void OnFriends(string name, string? server)
        {
            if (!await SignedInAsync(name, server))
            {
                return;
            }

            CloseView();
            _screen = Screen.Home;
            var friends = _app.Friends;
            var lifetime = _app.Lifetime;
            _view = new FriendsView(_shell.Show(_shell.Catalog.Friends), friends, ShowHome, new FriendsView.Actions(
                code =>
                {
                    _app.Analytics.Track(AnalyticsEvents.FriendAdd("code"));
                    _ = friends.AddByCodeAsync(code, lifetime);
                },
                id => _ = friends.AddAsync(id, lifetime),
                id => _ = friends.RemoveAsync(id, lifetime),
                id => _ = friends.BlockAsync(id, lifetime),
                entry => _ = friends.ToggleFavouriteAsync(entry, lifetime),
                id => _ = friends.UnblockAsync(id, lifetime)),
                () => _ = friends.RefreshAsync(lifetime),
                () => OnProfile(name, server));
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Friends);
            await friends.RefreshAsync(lifetime);
        }

        private async void OnPass(string name, string? server)
        {
            if (!await SignedInAsync(name, server))
            {
                return;
            }

            CloseView();
            _screen = Screen.Home;
            var pass = _app.Pass;
            _view = new PassView(_shell.Show(_shell.Catalog.Pass), pass, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), () => OnInventory(name, server),
                (tier, premium) => _ = pass.ClaimAsync(tier, premium, _app.Lifetime));
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Inventory);
            await pass.RefreshAsync(_app.Lifetime);
        }

        private async void OnShop(string name, string? server)
        {
            if (!await SignedInAsync(name, server))
            {
                return;
            }

            CloseView();
            _screen = Screen.Home;
            var shop = _app.Shop;
            var shopView = new ShopView(_shell.Show(_shell.Catalog.Shop), shop, () => OnInventory(name, server), item =>
                _shell.ShowDialog(Strings.Format(UiText.BuyQuestion, ItemText.Name(item.Id), ShopView.Price(item)), UiText.Buy, UiText.Cancel, () => _ = shop.BuyAsync(item.Id, _app.Lifetime), () => { }),
                item => _ = shop.EquipAsync(item.Id, _app.Lifetime));
            shopView.Retry = () => _ = shop.RefreshAsync(_app.Lifetime);
            _view = shopView;
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Inventory);
            await _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
            if (_app.Progress.Info != null)
            {
                shop.SetBalances(_app.Progress.Coins, _app.Progress.Gems);
            }

            await shop.RefreshAsync(_app.Lifetime);
        }

        private async void OnLeaderboard(string name, string? server)
        {
            if (!await SignedInAsync(name, server))
            {
                return;
            }

            CloseView();
            _screen = Screen.Home;
            var board = _app.Leaderboard;
            var boardView = new LeaderboardView(_shell.Show(_shell.Catalog.Leaderboard), board, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _app.Sessions.Current?.UserId, () => OnInventory(name, server),
                (period, friends) => _ = board.ShowAsync(period, friends, _app.Lifetime),
                () => OnRanked(name, server));
            boardView.Retry = () => _ = board.RefreshAsync(_app.Lifetime);
            _view = boardView;
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Inventory);
            await board.RefreshAsync(_app.Lifetime);
        }

        private async void OnRanked(string name, string? server)
        {
            if (!await SignedInAsync(name, server))
            {
                return;
            }

            CloseView();
            _screen = Screen.Home;
            var ranked = _app.Ranked;
            var rankedView = new RankedView(_shell.Show(_shell.Catalog.Ranked), ranked, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _app.Sessions.Current?.UserId,
                () => OnLeaderboard(name, server), card => OnRankedPlay(name, server, card));
            rankedView.Retry = () => _ = ranked.RefreshAsync(_app.Lifetime);
            _view = rankedView;
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Home);
            await ranked.RefreshAsync(_app.Lifetime);
        }

        /// <summary>The Ranked queue (GDD 7.5): the Quick Play search screen with the ranked band.</summary>
        private async void OnRankedPlay(string name, string? server, Hulla.Application.Progress.RankedCard card)
        {
            if (!await SignedInAsync(name, server))
            {
                return;
            }

            CloseView();
            _screen = Screen.QuickPlay;
            _shell.HideNav();
            _view = new QuickPlayView(_shell.Show(_shell.Catalog.QuickPlay), _app.QuickPlay, CancelQuickPlay);
            await _app.QuickPlay.StartRankedAsync(card.Band, card.WidenBy, card.MaxBand, _app.Lifetime);
        }

        private async void OnMissions(string name, string? server)
        {
            if (!await SignedInAsync(name, server))
            {
                return;
            }

            CloseView();
            _screen = Screen.Home;
            var missions = _app.Missions;
            var tz = LocalOffsetMinutes();
            var missionsView = new MissionsView(_shell.Show(_shell.Catalog.Missions), missions, () => OnInventory(name, server),
                id => _ = missions.ClaimAsync(id, tz, _app.Lifetime),
                id => _ = missions.RerollAsync(id, tz, _app.Lifetime));
            missionsView.Retry = () => _ = missions.RefreshAsync(tz, _app.Lifetime);
            _view = missionsView;
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Inventory);
            await missions.RefreshAsync(tz, _app.Lifetime);
        }

        private async void OnProfile(string name, string? server)
        {
            if (!await SignedInAsync(name, server))
            {
                return;
            }

            CloseView();
            _screen = Screen.Home;
            var profile = _app.Profile;
            _view = new ProfileView(_shell.Show(_shell.Catalog.Profile), profile, ShowHome, (display, handle, bio) => _ = profile.SaveAsync(display, handle, bio, _app.Lifetime), () => OpenAchievements(name, server));
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Home);
            await profile.RefreshAsync(_app.Lifetime);
        }

        private async void OpenAchievements(string name, string? server)
        {
            CloseView();
            _screen = Screen.Home;
            var achievements = _app.Achievements;
            var achievementsView = new AchievementsView(_shell.Show(_shell.Catalog.Achievements), achievements, () => OnProfile(name, server));
            achievementsView.Retry = () => _ = achievements.RefreshAsync(_app.Lifetime);
            _view = achievementsView;
            _shell.ShowNav(Hulla.Presentation.Navigation.NavTab.Inventory);
            await achievements.RefreshAsync(_app.Lifetime);
        }

        private async Awaitable<bool> SignedInAsync(string name, string? server)
        {
            ApplyServer(server);
            var ok = (await _app.Home.SignInAsync(name, _app.Lifetime)).IsOk;
            if (ok)
            {
                RememberName(name);
                SyncContent();
            }

            return ok;
        }

        /// <summary>Newer signed content downloads once a launch, in the background, for the next launch (ADR-0011).</summary>
        private async void SyncContent()
        {
            if (_contentSynced)
            {
                return;
            }

            _contentSynced = true;
            try
            {
                if (await _app.Content.SyncAsync(_app.Lifetime) == ContentSyncOutcome.Unavailable)
                {
                    _contentSynced = false;
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>A returning player is signed in at launch so Home can show friends' tables (GDD 14.10).</summary>
        private async void SignInQuietly()
        {
            var signedIn = await _app.Home.SignInAsync(_app.Home.LastName, _app.Lifetime);
            if (signedIn.IsOk)
            {
                SyncContent();
                await _app.RemoteConfig.RefreshAsync(UnityEngine.Application.version, "*", _app.Lifetime);
                ApplyRemoteConfig();
                await _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
                await _app.DailyReward.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
            }

            if (signedIn.IsOk && _view is HomeView shown)
            {
                RefreshRejoin(shown);
            }

            if (signedIn.IsOk && _screen == Screen.Home)
            {
                _app.HomeFriends.Active = true;
                _app.HomeFriends.RefreshSoon();
            }
        }

        /// <summary>
        /// The server's switches (GDD 22.5, 20.15): an old client is asked once a launch to update (the
        /// server already refuses its joins); maintenance or a Quick Play switch-off rests Play now.
        /// </summary>
        private void ApplyRemoteConfig()
        {
            var config = _app.RemoteConfig;
            _app.Packs.UseRemoteRoot(config.Current.Text("content_cdn_url", ""));
            if (config.UpdateRequired && !_updateAsked)
            {
                _updateAsked = true;
                _shell.ShowDialog(Strings.Text("config.update_required"), Strings.Text("config.update"), UiText.NotNow, OpenStore, () => { });
            }

            if (_view is HomeView home)
            {
                home.SetQuickPlay(config.QuickPlay, QuickPlayNote());
            }
        }

        private string? QuickPlayNote()
        {
            var config = _app.RemoteConfig;
            return config.Maintenance ? Strings.Text("config.maintenance") : config.QuickPlay ? null : Strings.Text("config.quick_play_off");
        }

        /// <summary>The store page: the config's update_url, else this app on Google Play.</summary>
        private void OpenStore() =>
            UnityEngine.Application.OpenURL(_app.RemoteConfig.Current.Text("update_url", "market://details?id=" + UnityEngine.Application.identifier));

        /// <summary>"Lahori Lion 42": a friendly starting name the player can change (GDD 17.2).</summary>
        private static string GeneratedName() =>
            UiText.NamePlaces[UnityEngine.Random.Range(0, UiText.NamePlaces.Length)] + " "
            + UiText.NameAnimals[UnityEngine.Random.Range(0, UiText.NameAnimals.Length)] + " "
            + UnityEngine.Random.Range(10, 100);

        private static void RememberName(string name)
        {
            PlayerPrefs.SetString(Hulla.Infrastructure.Settings.DeviceProfile.Key(NamePrefKey), name.Trim());
            PlayerPrefs.Save();
        }

        /// <summary>The app went to the background or came back: friends see Away meanwhile (GDD 10.4).</summary>
        public void SetAway(bool away)
        {
            if (away)
            {
                _app.Analytics.Flush();
            }
            else
            {
                OpenFromPush();
            }

            if (_app.Sessions.Current != null)
            {
                _ = _app.Social.SetAwayAsync(away, _app.Lifetime);
            }
        }

        private async void CancelQuickPlay()
        {
            _app.Analytics.Track(AnalyticsEvents.MatchSearch(_app.QuickPlay.Seats, _app.QuickPlay.Elapsed, "abandoned"));
            await _app.QuickPlay.CancelAsync();
            ShowHome();
        }

        private async void OnQuickPlayFound(TableTicket ticket)
        {
            _app.Analytics.Track(AnalyticsEvents.MatchSearch(_app.QuickPlay.Seats, _app.QuickPlay.Elapsed, "humans"));
            await EnterAsync(Result<TableTicket>.Ok(ticket));
        }

        private void ApplyServer(string? server)
        {
            if (server == null)
            {
                return;
            }

            var config = AppConfig.WithServer(server);
            PlayerPrefs.SetString(AppConfig.ServerPrefKey, config.HostAndPort);
            PlayerPrefs.Save();
            _app.UseServer(config);
        }

        private async Awaitable EnterAsync(Result<TableTicket> ticket)
        {
            var invitedAt = _inviteLinkAt;
            _inviteLinkAt = null;
            if (ticket.IsOk)
            {
                RememberName(_app.Home.LastName);
            }

            var player = _app.Sessions.Current;
            if (!ticket.IsOk || player == null)
            {
                return;
            }

            var session = _app.BeginSession(player);
            var table = session.BeginTable(ticket.Value);
            table.Client.Model.SetSort(_app.Settings.HandSort == HandSort.RankThenSuit ? CardOrder.CompareByRank : CardOrder.Compare);
            table.Client.Model.Changed += FollowModel;
            _tracker?.Dispose();
            _tracker = new AnalyticsTracker(_app.Analytics, table.Client);
            var entered = await _app.Tables.EnterAsync(ticket.Value, table.Lifetime);
            if (!entered.IsOk)
            {
                Debug.LogWarning($"could not enter the table: {entered.Error}");
                session.EndTable();
                ShowHome();
                _app.Home.ShowError(entered.Error.Code);
                return;
            }

            FollowModel();
            if (!ticket.Value.IsMatchmade && !ticket.Value.AsSpectator)
            {
                PlayerPrefs.SetString(Hulla.Infrastructure.Settings.DeviceProfile.Key(LastTableCodeKey), ticket.Value.Code);
                PlayerPrefs.SetString(Hulla.Infrastructure.Settings.DeviceProfile.Key(LastTableAtKey), DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                PlayerPrefs.Save();
            }

            if (invitedAt != null)
            {
                _app.Analytics.Track(AnalyticsEvents.InviteSeated(Time.realtimeSinceStartup - invitedAt.Value));
            }

            await _app.TableChat.JoinAsync(ticket.Value.MatchId, table.Lifetime);
            await _app.Mutes.RefreshAsync(table.Lifetime);
        }

        private async void Leave()
        {
            // Leaving mid-round keeps the table on Home to rejoin; leaving between rounds is final.
            if (_app.Session?.Table?.Client.Model.InRound != true)
            {
                ForgetLastTable();
            }

            await _app.TableChat.LeaveAsync();
            await _app.Tables.LeaveAsync();
            _app.Session?.EndTable();
            ShowHome();
            if (!AskForPushIfDue())
            {
                OfferWelcomePrompt();
            }
        }

        /// <summary>
        /// After the first live round (GDD 17.2 step 6), one soft prompt per return to Home: keep your
        /// progress with a phone number, then play with your group.
        /// </summary>
        private async void OfferWelcomePrompt()
        {
            var name = _app.Home.LastName;
            await _app.Phone.RefreshAsync(_app.Lifetime);
            var linked = _app.Phone.Current == PhoneViewModel.Stage.Linked;
            if (_screen != Screen.Home)
            {
                return;
            }

            switch (_app.WelcomePrompts.Next(linked))
            {
                case WelcomePrompts.Prompt.KeepProgress:
                    _shell.ShowDialog(Strings.Text("welcome.keep_progress"), Strings.Text("welcome.add_phone"), UiText.NotNow, OpenPhone, () => { });
                    break;
                case WelcomePrompts.Prompt.Invite:
                    _shell.ShowDialog(Strings.Text("welcome.invite"), UiText.CreateRoom, UiText.NotNow, () => OnCreate(name, null), () => { });
                    break;
            }
        }

        /// <returns>The code of the table the player left mid-round in the last 30 minutes, else null.</returns>
        private static string? LastTableCode()
        {
            var code = PlayerPrefs.GetString(Hulla.Infrastructure.Settings.DeviceProfile.Key(LastTableCodeKey), "");
            if (code.Length == 0 || !long.TryParse(PlayerPrefs.GetString(Hulla.Infrastructure.Settings.DeviceProfile.Key(LastTableAtKey), ""), out var at))
            {
                return null;
            }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - at <= RejoinWindowSeconds ? code : null;
        }

        private static void ForgetLastTable()
        {
            PlayerPrefs.DeleteKey(Hulla.Infrastructure.Settings.DeviceProfile.Key(LastTableCodeKey));
            PlayerPrefs.DeleteKey(Hulla.Infrastructure.Settings.DeviceProfile.Key(LastTableAtKey));
            PlayerPrefs.Save();
        }

        /// <summary>Back to the table the player left: the server gives them their seat back (R46).</summary>
        private async void Rejoin(HomeView home, string code)
        {
            var ticket = await _app.Home.JoinAsync(_app.Home.LastName, code, _app.Lifetime);
            if (!ticket.IsOk)
            {
                // The table has closed; the home screen shows why.
                ForgetLastTable();
                home.ShowRejoin(null, null);
                return;
            }

            await EnterAsync(ticket);
        }

        /// <summary>
        /// Rejoin stays on Home while the server still holds the player's seat, Quick Play included
        /// (GDD 9.8): until the round they left ends and the seat is given up or the table closes.
        /// </summary>
        private async void RefreshRejoin(HomeView home)
        {
            TableTicket? mine;
            try
            {
                mine = await _app.Home.FindMineAsync(_app.Lifetime);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (mine != null && _view == home)
            {
                home.ShowRejoin(mine.Code, () => _ = EnterAsync(Result<TableTicket>.Ok(mine)));
            }
        }

        /// <summary>A tapped notification (GDD 10.10) opens its screen; an invite joins like a link.</summary>
        private void OpenFromPush()
        {
            var tap = PushTap.From(_app.PushDevice.TakeOpenedData());
            if (tap == null)
            {
                return;
            }

            if (tap.Value.Route == PushRoute.Lobby)
            {
                _app.Links.Receive(InviteLink.For(tap.Value.Code!), Time.realtimeSinceStartup);
                return;
            }

            // A tap on a friend or reward notice never pulls the player away from a table.
            if (_screen == Screen.Table || _screen == Screen.Lobby || _screen == Screen.QuickPlay)
            {
                return;
            }

            switch (tap.Value.Route)
            {
                case PushRoute.Friends:
                    OnFriends(_app.Home.LastName, null);
                    break;
                case PushRoute.Pass:
                    OnPass(_app.Home.LastName, null);
                    break;
                default:
                    ShowHome();
                    break;
            }
        }

        /// <summary>The one-line ask before the system's notification dialog (PushPrompt has the timing).</summary>
        /// <returns>True when the ask was shown.</returns>
        private bool AskForPushIfDue()
        {
            var now = DateTimeOffset.Now;
            var day = (int)((now.ToUnixTimeSeconds() + (long)now.Offset.TotalSeconds) / 86400);
            if (!_app.PushPrompt.ShouldAsk(_app.PushDevice.CanAsk, day))
            {
                return false;
            }

            _app.PushPrompt.Asked(day);
            _shell.ShowDialog(PushText.AskTitle, PushText.AskYes, PushText.AskNo, () => _app.PushDevice.Ask(_ => { }), () => { });
            return true;
        }

        /// <summary>
        /// The socket dropped (a locked phone, a network change). At a table the screen stays and the
        /// client reconnects and rejoins inside the server's 30 s window (GDD R43); the server answers
        /// with table_state and any open turn_start. Away from a table the socket is simply reopened.
        /// </summary>
        private async void OnDisconnected()
        {
            if (_reconnecting)
            {
                return;
            }

            _reconnecting = true;
            var table = _app.Session?.Table;
            try
            {
                if (table == null)
                {
                    await _app.Sessions.ReconnectAsync(_app.Lifetime);
                    return;
                }

                _shell.SetBanner(UiText.Reconnecting);
                var started = Time.realtimeSinceStartup;
                var deadline = started + ReconnectWindowSeconds;
                while (Time.realtimeSinceStartup < deadline && _app.Session?.Table == table)
                {
                    var socket = await _app.Sessions.ReconnectAsync(table.Lifetime);
                    var rejoin = table.Ticket.WithResume(table.Client.Model.ResumeToken);
                    if (socket.IsOk && (await _app.Tables.EnterAsync(rejoin, table.Lifetime)).IsOk)
                    {
                        await _app.TableChat.JoinAsync(table.Ticket.MatchId, table.Lifetime);
                        _app.Analytics.Track(AnalyticsEvents.Reconnect("rejoined", Time.realtimeSinceStartup - started));
                        _shell.SetBanner(null);
                        return;
                    }

                    await Awaitable.WaitForSecondsAsync(RetrySeconds, table.Lifetime);
                }

                _shell.SetBanner(null);
                _app.Analytics.Track(AnalyticsEvents.Reconnect("gave_up", Time.realtimeSinceStartup - started));
                if (_app.Session?.Table == table)
                {
                    _app.Session?.EndTable();
                    ShowHome();
                }
            }
            catch (OperationCanceledException)
            {
                _shell.SetBanner(null);
            }
            finally
            {
                _reconnecting = false;
            }
        }

        private void FollowModel()
        {
            var client = _app.Session?.Table?.Client;
            if (client == null)
            {
                return;
            }

            var model = client.Model;
            if (model.Closed)
            {
                Leave();
                return;
            }

            if (model.Seats.Count == 0)
            {
                return;
            }

            if (model.LastResult != null && model.LastResult.RoundId != _friendsForRound)
            {
                _friendsForRound = model.LastResult.RoundId;
                ShowReward(model.LastResult.RoundId);
                new ThullaHints(_app.Store).RoundEnded();
                _app.PushPrompt.RoundFinished();
                OfferFriends();
                OfferBigThulla(client);
            }

            var wanted = model.InRound || model.LastResult != null ? Screen.Table : Screen.Lobby;
            if (wanted == _screen)
            {
                return;
            }

            CloseView();
            _screen = wanted;
            _shell.HideNav();
            if (wanted == Screen.Lobby)
            {
                _lobbyView = new LobbyView(_shell.Show(_shell.Catalog.Lobby), client, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Leave, code => ShareCode(code), text => _app.TableChat.SendAsync(text, _app.Lifetime), () => ThullaRulesEditor.Summary(client.Model, _app.Presets), () => OpenRulesEditor(client));
                _lobbyView.IsMuted = _app.Mutes.IsMuted;
                _lobbyView.SeatSpectator = client.SeatSpectator;
                if (client.Model.IsPrivate)
                {
                    _lobbyView.UseInvites(FriendsToInviteAsync, InviteAsync);
                }
                _view = _lobbyView;
                return;
            }

            client.Events.Clear();
            _tableView = new ThullaTableView(_shell.Show(_shell.Catalog.Table), client, _app.Chat, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Leave, openSettings: OpenSettings, openRules: OpenRules, sendText: text => _app.TableChat.SendAsync(text, _app.Lifetime), sounds: _sounds);
            _tableView.Invite = () => ShareCode(client.Model.Code);
            ApplySettings();
            var hints = new ThullaHints(_app.Store);
            if (hints.Active)
            {
                _tableView.UseHints(hints);
            }

            _tableView.UseMutes(_app.Mutes.IsMuted, (id, muted) => _ = _app.Mutes.SetAsync(id, muted, _app.Lifetime));
            _tableView.UseModeration(BlockPlayer, (id, reason, done) => ReportPlayer(client, id, reason, done));
            _tableView.UseShare(() => AskShareRatio(client));
            _tableView.UseFairness(ExplainFairDeal);
            if (client.Model.IsPrivate)
            {
                _tableView.UseChangeRules(() => OpenRulesEditor(client));
            }

            _scheduler = new AnimationScheduler(client.Events, _tableView);
            _view = _tableView;
        }

        /// <summary>The fair deal in plain words (ADR-0009): the commitment, the seed, and a copy of the seed to check elsewhere.</summary>
        private void ExplainFairDeal(Fairness fairness)
        {
            var text = Strings.Format(UiText.FairDealExplained, Short(fairness.Commitment), Short(fairness.Seed));
            _shell.ShowDialog(text, UiText.Close, UiText.CopySeed, () => { }, () => GUIUtility.systemCopyBuffer = fairness.Seed);

            static string Short(string value) => value.Length > 16 ? value.Substring(0, 8) + "…" + value.Substring(value.Length - 8) : value;
        }

        /// <summary>After a round, co-players who aren't friends or pending get an "Add" chip (GDD 14.8, 10.3).</summary>
        private async void OfferFriends()
        {
            var friends = _app.Friends;
            await friends.RefreshAsync(_app.Lifetime);
            var known = new HashSet<string>();
            foreach (var list in new[] { friends.Friends, friends.Sent, friends.Received, friends.Blocked })
            {
                foreach (var entry in list)
                {
                    known.Add(entry.UserId);
                }
            }

            var me = _app.Sessions.Current?.UserId;
            _tableView?.SetFriendActions(id => id != me && !known.Contains(id), id =>
            {
                _app.Analytics.Track(AnalyticsEvents.FriendAdd("results"));
                _ = friends.AddAsync(id, _app.Lifetime);
            });
        }

        /// <summary>A block hides the player at once and holds both ways on the server (GDD 10.11).</summary>
        private async void BlockPlayer(string userId)
        {
            _app.Analytics.Track(AnalyticsEvents.Block("table"));
            await _app.Mutes.SetAsync(userId, true, _app.Lifetime);
            await _app.Social.BlockAsync(userId, _app.Lifetime);
        }

        private async void ReportPlayer(TableClient client, string userId, string reason, Action<bool> done)
        {
            var model = client.Model;
            var tableId = client.Ticket.TableId.Length > 0 ? client.Ticket.TableId : model.Code.Length > 0 ? "t_" + model.Code : null;
            var sent = await _app.Social.ReportAsync(userId, reason, tableId, model.RoundId.Length > 0 ? model.RoundId : null, _app.Lifetime);
            if (sent.IsOk)
            {
                _app.Analytics.Track(AnalyticsEvents.Report("table", reason));
            }

            done(sent.IsOk);
        }

        private void AskShareRatio(TableClient client) =>
            _shell.ShowDialog(UiText.ShareAs, UiText.SharePost, UiText.ShareStory, () => ShareResult(client, CardRatio.Feed), () => ShareResult(client, CardRatio.Story));

        /// <summary>Once a session, a round with the night's biggest pick-up offers its card (GDD 10.9).</summary>
        private void OfferBigThulla(TableClient client)
        {
            var result = client.Model.LastResult;
            if (result == null || client.Model.Spectating)
            {
                return;
            }

            var (seat, cards) = ThullaShareCard.BiggestPickUp(result);
            if (seat >= 0 && _bigThulla.Consider(cards))
            {
                _tableView?.OfferBigThulla(result.RoundId);
            }
        }

        private async void ShareResult(TableClient client, CardRatio ratio)
        {
            var model = client.Model;
            var result = model.LastResult;
            if (result == null)
            {
                return;
            }

            var code = model.IsPrivate && model.Code.Length > 0 ? model.Code : null;
            var friendCode = code == null ? await MyFriendCodeAsync() : null;
            var footer = code != null ? Strings.Format(UiText.ShareFooterCode, code)
                : friendCode != null ? Strings.Format(UiText.ShareFooterFriend, friendCode)
                : UiText.ShareFooter;
            var png = await CardRenderer.RenderAsync(_shell.PanelSettings, ratio, root => ThullaShareCard.Build(root, model, seat => SeatName(model, seat), footer, ratio == CardRatio.Story));
            var loser = SeatName(model, result.BhabhiSeat);
            var title = ThullaShareCard.LoserTitle(model);
            var text = code != null ? Strings.Format(UiText.ShareResultWithCode, loser, title, code, InviteLink.For(code))
                : friendCode != null ? Strings.Format(UiText.ShareResultReferral, loser, title, InviteLink.ReferralFor(friendCode))
                : Strings.Format(UiText.ShareResultText, loser, title);
            _app.ShareSheet.ShareImage(UiText.ShareResultTitle, text, png, "hulla-" + result.RoundId + ".png");
        }

        /// <summary>The player's friend code for a referral link, loading the profile once; null when it can't.</summary>
        private async Awaitable<string?> MyFriendCodeAsync()
        {
            if (_app.Profile.Profile == null)
            {
                await _app.Profile.RefreshAsync(_app.Lifetime);
            }

            var friendCode = _app.Profile.Profile?.FriendCode;
            return string.IsNullOrEmpty(friendCode) ? null : friendCode;
        }

        private static string SeatName(TableModel model, long seat) =>
            seat >= 0 && seat < model.Seats.Count ? model.Seats[(int)seat].DisplayName ?? "?" : "?";

        private bool ShareCode(string code)
        {
            var shared = _app.ShareSheet.Share(UiText.ShareTitle, Strings.Format(UiText.ShareText, code, InviteLink.For(code)));
            _app.Analytics.Track(AnalyticsEvents.InviteSent(shared ? "share" : "copy"));
            return shared;
        }

        private static int LocalOffsetMinutes() => (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;

        /// <summary>The server pays at round end; the reward line appears once progress_get carries this round.</summary>
        private async void ShowReward(string roundId)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                await _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
                var reward = _app.Progress.RewardFor(roundId);
                if (reward != null)
                {
                    var text = Strings.Format(UiText.RewardLine, reward.Xp, reward.Coins) + (reward.Capped ? UiText.RewardCapped : "");
                    if (reward.WelcomeCoins > 0)
                    {
                        text += "\n" + Strings.Get("welcome.first_round_bonus", reward.WelcomeCoins);
                    }

                    _app.WelcomePrompts.FirstRoundFinished();
                    if (reward.LevelTo > reward.LevelFrom)
                    {
                        text += "\n" + Strings.Format(UiText.LevelUp, reward.LevelFrom, reward.LevelTo);
                    }

                    _tableView?.ShowReward(text);
                    return;
                }

                await Awaitable.WaitForSecondsAsync(1f, _app.Lifetime);
            }
        }

        private void OnText(TextLine line)
        {
            _tableView?.ShowText(line);
            _lobbyView?.ShowText(line);
        }

        private void HideRecap() => _app.Settings.ShowRulesRecap = false;

        /// <summary>Settings open over the current screen, so play goes on underneath (GDD 14.12).</summary>
        private void OpenSettings()
        {
            _settingsView?.Dispose();
            var account = _app.Sessions.Current == null ? null : new SettingsView.AccountActions(ExportData, DeleteAccount, CancelDeletion, PendingDeletion, OpenPhone);
            var signedIn = _app.Sessions.Current != null;
            _settingsView = new SettingsView(_shell.ShowOverlay(_shell.Catalog.Settings), _app.Settings, CloseSettings, account,
                signedIn ? _app.NotificationSettings : null,
                change => _ = _app.NotificationSettings.ChangeAsync(change, _app.Lifetime));
            if (signedIn)
            {
                _ = _app.NotificationSettings.RefreshAsync(_app.Lifetime);
            }
        }

        /// <summary>The phone number screen over Home (GDD 10.2, #99); a switch to the number's account starts over at Home.</summary>
        private void OpenPhone()
        {
            CloseSettings();
            var phone = _app.Phone;
            var lifetime = _app.Lifetime;
            _phoneView?.Dispose();
            _phoneView = new PhoneView(_shell.ShowOverlay(_shell.Catalog.Phone), phone, ClosePhone, new PhoneView.PhoneActions(
                number => _ = phone.SendAsync(number, lifetime),
                code => _ = phone.VerifyAsync(code, lifetime),
                () => _ = phone.RemoveAsync(lifetime),
                () => _ = phone.SwitchAsync(lifetime),
                phone.StartOver));
            _ = phone.RefreshAsync(lifetime);
        }

        private void ClosePhone()
        {
            _phoneView?.Dispose();
            _phoneView = null;
            _shell.CloseOverlay();
        }

        /// <summary>Signed in to the number's account: its name, a fresh session scope, Home again.</summary>
        private void OnPhoneSwitched(PlayerSession player)
        {
            _app.Home.LastName = player.DisplayName;
            RememberName(player.DisplayName);
            _app.EndSession();
            _app.BeginSession(player);
            ClosePhone();
            ShowHome();
        }

        /// <summary>The rulebook opens over the table, so play goes on underneath (GDD 14.11).</summary>
        private void OpenRules(string? section)
        {
            var client = _app.Session?.Table?.Client;
            if (client == null)
            {
                return;
            }

            CloseSettings();
            var sections = ThullaRulebook.Build(client.Model, _app.Presets);
            _ = new RulebookView(_shell.ShowOverlay(_shell.Catalog.Rulebook), RulebookText.Title, sections, _shell.CloseOverlay, section);
        }

        /// <summary>The host edits the next deal's rules over the lobby (GDD 14.9).</summary>
        private void OpenRulesEditor(TableClient client)
        {
            CloseSettings();
            _ = new ThullaRulesEditor(_shell.ShowOverlay(_shell.Catalog.Settings), client.Model, _app.Presets, client.SetRules, _shell.CloseOverlay);
        }

        /// <summary>When a requested deletion happens (Unix ms), kept on the device to show and cancel it.</summary>
        private const string DeleteAtPrefKey = "hulla.delete_at";

        private async void ExportData(Action<string> status)
        {
            var export = await _app.Account.ExportAsync(_app.Lifetime);
            if (!export.IsOk)
            {
                status(UiText.Error(export.Error.Code));
                return;
            }

            status(_app.ShareSheet.Share(UiText.ExportData, export.Value) ? UiText.ExportShared : UiText.ExportCopied);
        }

        private void DeleteAccount(Action<string> status) =>
            _shell.ShowDialog(UiText.DeleteConfirm, UiText.DeleteYes, UiText.DeleteNo, async () =>
            {
                var deleted = await _app.Account.DeleteAsync(_app.Lifetime);
                if (!deleted.IsOk)
                {
                    status(UiText.Error(deleted.Error.Code));
                    return;
                }

                PlayerPrefs.SetString(Hulla.Infrastructure.Settings.DeviceProfile.Key(DeleteAtPrefKey), deleted.Value.ToString());
                PlayerPrefs.Save();
                status(PendingDeletion() ?? "");
            }, () => { });

        private async void CancelDeletion(Action<string> status)
        {
            var cancelled = await _app.Account.CancelDeleteAsync(_app.Lifetime);
            if (!cancelled.IsOk)
            {
                status(UiText.Error(cancelled.Error.Code));
                return;
            }

            PlayerPrefs.DeleteKey(Hulla.Infrastructure.Settings.DeviceProfile.Key(DeleteAtPrefKey));
            PlayerPrefs.Save();
            status(UiText.DeletionCancelled);
        }

        private static string? PendingDeletion()
        {
            if (!long.TryParse(PlayerPrefs.GetString(Hulla.Infrastructure.Settings.DeviceProfile.Key(DeleteAtPrefKey), ""), out var at) || at <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                return null;
            }

            return Strings.Format(UiText.DeletePending, DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString(Strings.Text("format.date_long"), Strings.Culture));
        }

        private void CloseSettings()
        {
            _settingsView?.Dispose();
            _settingsView = null;
            _shell.CloseOverlay();
        }

        private void ApplySettings()
        {
            var settings = _app.Settings;
            _app.Analytics.SetConsent(settings.AnalyticsConsent == true);
            ThemeClasses.Apply(_shell.Root, settings);
            _shell.ApplySkin(settings.Skin, settings.ReduceMotion);
            _sounds.Volume = settings.SfxVolume / 100f;
            _tableView?.ApplySettings(settings.OneTapPlay, settings.ReduceMotion, settings.LargeCards, settings.Haptics, settings.ShowRulesRecap ? HideRecap : null);
            _app.Session?.Table?.Client.Model.SetSort(settings.HandSort == HandSort.RankThenSuit ? CardOrder.CompareByRank : CardOrder.Compare);
        }

        private void Tick(float deltaSeconds)
        {
            _app.QuickPlay.Tick(deltaSeconds);
            _app.Analytics.Tick(deltaSeconds);
            _tutorial?.Tick(deltaSeconds);
            _app.HomeFriends.Tick(deltaSeconds, _app.Lifetime);
            _lobbyView?.Tick();
            _scheduler?.Tick(deltaSeconds);
            _tableView?.Tick(deltaSeconds);
        }

        /// <summary>"Yes" to the gate skips to Home with a short refresher offer (GDD 17.2).</summary>
        /// <summary>Analytics only after the player says yes (GDD 23.6); asked once, then a setting.</summary>
        private void AskConsent() =>
            _shell.ShowDialog(UiText.AnalyticsAsk, UiText.AnalyticsYes, UiText.AnalyticsNo, () => _app.Settings.AnalyticsConsent = true, () => _app.Settings.AnalyticsConsent = false);

        private void OfferRefresher()
        {
            MarkTutorialSeen();
            _shell.ShowDialog(TutorialText.Refresher + "?", TutorialText.Next, TutorialText.Skip, () => StartTutorial(true), () => { });
        }

        /// <summary>The scripted mini-hands through the normal table (GDD 17.3); the refresher is the thulla steps only.</summary>
        private void StartTutorial(bool refresher)
        {
            CloseView();
            _screen = Screen.Table;
            _shell.HideNav();
            _tutorialRefresher = refresher;
            var steps = ThullaTutorial.Steps();
            var chosen = refresher ? new[] { steps[1], steps[2] } : steps;
            var gateway = new ScriptedTableGateway(chosen, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TutorialText.HowToPlay);
            var client = new TableClient(gateway, new TableTicket("tutorial", "", ""), "me", 1);
            var view = new ThullaTableView(_shell.Show(_shell.Catalog.Table), client, _app.Chat, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), EndTutorial, sounds: _sounds, autoPlaySingle: false);
            var settings = _app.Settings;
            view.ApplySettings(settings.OneTapPlay, settings.ReduceMotion, settings.LargeCards, settings.Haptics, null);
            view.CardRefused += _ => gateway.WrongTap();
            gateway.CoachSaid += text => view.ShowCoach(text);
            gateway.StepFinished += (id, outcome, seconds) =>
            {
                // The ftue_step event (GDD 17.6).
                _app.Analytics.Track(AnalyticsEvents.FtueStep(id, outcome, seconds));
                if (gateway.HasNextStep)
                {
                    view.ShowCoach(gateway.DoneText, TutorialText.Next, () =>
                    {
                        view.HideCoach();
                        gateway.BeginNextStep();
                    });
                }
                else
                {
                    view.ShowCoach(gateway.DoneText, TutorialText.Finish, EndTutorial);
                }
            };
            _tutorial = gateway;
            _tutorialClient = client;
            _tableView = view;
            _scheduler = new AnimationScheduler(client.Events, view);
            _view = view;
            gateway.BeginNextStep();
        }

        private void EndTutorial()
        {
            MarkTutorialSeen();
            ShowHome();
            if (!_tutorialRefresher && _app.Sessions.Current != null)
            {
                PayTutorial();
            }
        }

        /// <summary>The tutorial's one-time reward (GDD 17.2), said in the top pill for a few seconds.</summary>
        private async void PayTutorial()
        {
            var paid = await _app.Welcome.TutorialCompleteAsync(_app.Lifetime);
            if (!paid.IsOk || paid.Value <= 0)
            {
                return;
            }

            var text = Strings.Get("welcome.tutorial_bonus", paid.Value);
            _shell.SetBanner(text);
            _ = _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
            await Awaitable.WaitForSecondsAsync(4f);
            if (UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Label>(_shell.Root, "shell-banner")?.text == text)
            {
                _shell.SetBanner(null);
            }
        }

        private static void MarkTutorialSeen()
        {
            PlayerPrefs.SetInt(Hulla.Infrastructure.Settings.DeviceProfile.Key(TutorialSeenPrefKey), 1);
            PlayerPrefs.Save();
        }

        private void CloseView()
        {
            _app.HomeFriends.Active = false;
            _daily?.Dispose();
            _daily = null;
            _tutorialClient?.Dispose();
            _tutorialClient = null;
            _tutorial = null;
            _view?.Dispose();
            _view = null;
            _tableView = null;
            _lobbyView = null;
            _scheduler = null;
            _screen = Screen.None;
        }
    }
}
