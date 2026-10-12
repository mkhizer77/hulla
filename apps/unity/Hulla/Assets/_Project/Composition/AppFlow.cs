using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Domain;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Screens.Friends;
using Hulla.Presentation.Screens.Lobby;
using Hulla.Presentation.Screens.Profile;
using Hulla.Presentation.Screens.QuickPlay;
using Hulla.Presentation.Screens.Rulebook;
using Hulla.Presentation.Screens.Settings;
using Hulla.Presentation.Theme;
using Hulla.Application.Settings;
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


        private readonly AppScope _app;
        private readonly ShellView _shell;
        private Screen _screen;
        private IDisposable? _view;
        private AnimationScheduler? _scheduler;
        private ThullaTableView? _tableView;
        private bool _reconnecting;
        private SettingsView? _settingsView;
        private LobbyView? _lobbyView;
        private string _friendsForRound = "";

        public AppFlow(AppScope app, ShellView shell)
        {
            _app = app;
            _shell = shell;
            _shell.Ticked += Tick;
            _app.Settings.Changed += ApplySettings;
            _app.QuickPlay.Found += OnQuickPlayFound;
            _app.TableChat.Received += OnText;
            _app.Tables.Disconnected += OnDisconnected;
        }

        public async void Start()
        {
            ApplySettings();
            _app.Home.LastName = PlayerPrefs.GetString(NamePrefKey, "");
            ShowHome();
            if (_app.Home.LastName.Length > 0)
            {
                SignInQuietly();
            }
            try
            {
                await _app.Chat.LoadAsync(_app.Lifetime);
                await _app.Presets.LoadAsync(_app.Lifetime);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose()
        {
            _shell.Ticked -= Tick;
            _app.Settings.Changed -= ApplySettings;
            _app.QuickPlay.Found -= OnQuickPlayFound;
            _app.TableChat.Received -= OnText;
            _app.Tables.Disconnected -= OnDisconnected;
            CloseView();
        }

        private void ShowHome()
        {
            CloseView();
            _screen = Screen.Home;
            var root = _shell.Show(_shell.Catalog.Home);
            var server = Debug.isDebugBuild ? _app.Config.HostAndPort : null;
            _view = new HomeView(root, _app.Home, server, OnCreate, OnJoin, OnQuickPlay, OpenSettings, OnFriends, OnProfile, _app.HomeFriends);
            _app.HomeFriends.Active = _app.Sessions.Current != null;
            _app.HomeFriends.RefreshSoon();
        }

        private async void OnCreate(string name, string? server)
        {
            ApplyServer(server);
            var ticket = await _app.Home.CreateAsync(name, _app.Lifetime);
            await EnterAsync(ticket);
        }

        private async void OnJoin(string name, string code, string? server)
        {
            ApplyServer(server);
            var ticket = await _app.Home.JoinAsync(name, code, _app.Lifetime);
            await EnterAsync(ticket);
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
                code => _ = friends.AddByCodeAsync(code, lifetime),
                id => _ = friends.AddAsync(id, lifetime),
                id => _ = friends.RemoveAsync(id, lifetime),
                id => _ = friends.BlockAsync(id, lifetime),
                entry => _ = friends.ToggleFavouriteAsync(entry, lifetime)));
            await friends.RefreshAsync(lifetime);
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
            _view = new ProfileView(_shell.Show(_shell.Catalog.Profile), profile, ShowHome, (display, handle, bio) => _ = profile.SaveAsync(display, handle, bio, _app.Lifetime));
            await profile.RefreshAsync(_app.Lifetime);
        }

        private async Awaitable<bool> SignedInAsync(string name, string? server)
        {
            ApplyServer(server);
            var ok = (await _app.Home.SignInAsync(name, _app.Lifetime)).IsOk;
            if (ok)
            {
                RememberName(name);
            }

            return ok;
        }

        /// <summary>A returning player is signed in at launch so Home can show friends' tables (GDD 14.10).</summary>
        private async void SignInQuietly()
        {
            var signedIn = await _app.Home.SignInAsync(_app.Home.LastName, _app.Lifetime);
            if (signedIn.IsOk && _screen == Screen.Home)
            {
                _app.HomeFriends.Active = true;
                _app.HomeFriends.RefreshSoon();
            }
        }

        private static void RememberName(string name)
        {
            PlayerPrefs.SetString(NamePrefKey, name.Trim());
            PlayerPrefs.Save();
        }

        /// <summary>The app went to the background or came back: friends see Away meanwhile (GDD 10.4).</summary>
        public void SetAway(bool away)
        {
            if (_app.Sessions.Current != null)
            {
                _ = _app.Social.SetAwayAsync(away, _app.Lifetime);
            }
        }

        private async void CancelQuickPlay()
        {
            await _app.QuickPlay.CancelAsync();
            ShowHome();
        }

        private async void OnQuickPlayFound(TableTicket ticket) => await EnterAsync(Result<TableTicket>.Ok(ticket));

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
            var entered = await _app.Tables.EnterAsync(ticket.Value, table.Lifetime);
            if (!entered.IsOk)
            {
                Debug.LogWarning($"could not enter the table: {entered.Error}");
                session.EndTable();
                return;
            }

            FollowModel();
            await _app.TableChat.JoinAsync(ticket.Value.MatchId, table.Lifetime);
        }

        private async void Leave()
        {
            await _app.TableChat.LeaveAsync();
            await _app.Tables.LeaveAsync();
            _app.Session?.EndTable();
            ShowHome();
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
                var deadline = Time.realtimeSinceStartup + ReconnectWindowSeconds;
                while (Time.realtimeSinceStartup < deadline && _app.Session?.Table == table)
                {
                    var socket = await _app.Sessions.ReconnectAsync(table.Lifetime);
                    if (socket.IsOk && (await _app.Tables.EnterAsync(table.Ticket, table.Lifetime)).IsOk)
                    {
                        await _app.TableChat.JoinAsync(table.Ticket.MatchId, table.Lifetime);
                        _shell.SetBanner(null);
                        return;
                    }

                    await Awaitable.WaitForSecondsAsync(RetrySeconds, table.Lifetime);
                }

                _shell.SetBanner(null);
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

            if (model.MySeat == null && model.Seats.Count == 0)
            {
                return;
            }

            if (model.LastResult != null && model.LastResult.RoundId != _friendsForRound)
            {
                _friendsForRound = model.LastResult.RoundId;
                OfferFriends();
            }

            var wanted = model.InRound || model.LastResult != null ? Screen.Table : Screen.Lobby;
            if (wanted == _screen)
            {
                return;
            }

            CloseView();
            _screen = wanted;
            if (wanted == Screen.Lobby)
            {
                _lobbyView = new LobbyView(_shell.Show(_shell.Catalog.Lobby), client, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Leave, code => _app.ShareSheet.Share(UiText.ShareTitle, string.Format(UiText.ShareText, code)), text => _app.TableChat.SendAsync(text, _app.Lifetime), () => ThullaRulesEditor.Summary(client.Model, _app.Presets), () => OpenRulesEditor(client));
                _view = _lobbyView;
                return;
            }

            client.Events.Clear();
            _tableView = new ThullaTableView(_shell.Show(_shell.Catalog.Table), client, _app.Chat, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Leave, openSettings: OpenSettings, openRules: OpenRules, sendText: text => _app.TableChat.SendAsync(text, _app.Lifetime));
            ApplySettings();
            _scheduler = new AnimationScheduler(client.Events, _tableView);
            _view = _tableView;
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
            _tableView?.SetFriendActions(id => id != me && !known.Contains(id), id => _ = friends.AddAsync(id, _app.Lifetime));
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
            _settingsView = new SettingsView(_shell.ShowOverlay(_shell.Catalog.Settings), _app.Settings, CloseSettings);
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

        private void CloseSettings()
        {
            _settingsView?.Dispose();
            _settingsView = null;
            _shell.CloseOverlay();
        }

        private void ApplySettings()
        {
            var settings = _app.Settings;
            ThemeClasses.Apply(_shell.Root, settings);
            _tableView?.ApplySettings(settings.OneTapPlay, settings.ReduceMotion, settings.LargeCards, settings.Haptics, settings.ShowRulesRecap ? HideRecap : null);
            _app.Session?.Table?.Client.Model.SetSort(settings.HandSort == HandSort.RankThenSuit ? CardOrder.CompareByRank : CardOrder.Compare);
        }

        private void Tick(float deltaSeconds)
        {
            _app.QuickPlay.Tick(deltaSeconds);
            _app.HomeFriends.Tick(deltaSeconds, _app.Lifetime);
            _lobbyView?.Tick();
            _scheduler?.Tick(deltaSeconds);
            _tableView?.Tick(deltaSeconds);
        }

        private void CloseView()
        {
            _app.HomeFriends.Active = false;
            _view?.Dispose();
            _view = null;
            _tableView = null;
            _lobbyView = null;
            _scheduler = null;
            _screen = Screen.None;
        }
    }
}
