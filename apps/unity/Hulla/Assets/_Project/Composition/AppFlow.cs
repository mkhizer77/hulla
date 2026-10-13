using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Application.Tutorial;
using Hulla.Domain;
using Hulla.Protocol;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Screens.Friends;
using Hulla.Presentation.Screens.Lobby;
using Hulla.Presentation.Screens.Missions;
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
        private AnalyticsTracker? _tracker;
        private ScriptedTableGateway? _tutorial;
        private TableClient? _tutorialClient;

        /// <summary>1 once the player has answered "Do you know Bhabhi?" or finished the tutorial.</summary>
        private const string TutorialSeenPrefKey = "hulla.tutorial_seen";

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
            _app.Analytics.Track(AnalyticsEvents.AppOpen(true, "none"));
            _app.Home.LastName = PlayerPrefs.GetString(NamePrefKey, "");
            var returning = _app.Home.LastName.Length > 0;
            if (!returning)
            {
                _app.Home.LastName = GeneratedName();
            }

            ShowHome();
            if (PlayerPrefs.GetInt(TutorialSeenPrefKey, 0) == 0 && !returning)
            {
                _shell.ShowDialog(TutorialText.Gate, TutorialText.GateYes, TutorialText.GateNo, OfferRefresher, () => StartTutorial(false));
            }
            else if (_app.Settings.AnalyticsConsent == null)
            {
                AskConsent();
            }
            if (returning)
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
            _view = new HomeView(root, _app.Home, server, OnCreate, OnJoin, OnQuickPlay, OpenSettings, OnFriends, OnProfile, _app.HomeFriends, () => StartTutorial(false), TutorialText.HowToPlay, OnWatch, _app.Progress, OnMissions);
            _app.HomeFriends.Active = _app.Sessions.Current != null;
            _app.HomeFriends.RefreshSoon();
            if (_app.Sessions.Current != null)
            {
                _ = _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
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
                id => _ = friends.UnblockAsync(id, lifetime)));
            await friends.RefreshAsync(lifetime);
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
            _view = new MissionsView(_shell.Show(_shell.Catalog.Missions), missions, ShowHome,
                id => _ = missions.ClaimAsync(id, tz, _app.Lifetime),
                id => _ = missions.RerollAsync(id, tz, _app.Lifetime));
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
            if (signedIn.IsOk)
            {
                await _app.Progress.RefreshAsync(LocalOffsetMinutes(), _app.Lifetime);
            }

            if (signedIn.IsOk && _screen == Screen.Home)
            {
                _app.HomeFriends.Active = true;
                _app.HomeFriends.RefreshSoon();
            }
        }

        /// <summary>"Lahori Lion 42": a friendly starting name the player can change (GDD 17.2).</summary>
        private static string GeneratedName() =>
            UiText.NamePlaces[UnityEngine.Random.Range(0, UiText.NamePlaces.Length)] + " "
            + UiText.NameAnimals[UnityEngine.Random.Range(0, UiText.NameAnimals.Length)] + " "
            + UnityEngine.Random.Range(10, 100);

        private static void RememberName(string name)
        {
            PlayerPrefs.SetString(NamePrefKey, name.Trim());
            PlayerPrefs.Save();
        }

        /// <summary>The app went to the background or came back: friends see Away meanwhile (GDD 10.4).</summary>
        public void SetAway(bool away)
        {
            if (away)
            {
                _app.Analytics.Flush();
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
                return;
            }

            FollowModel();
            await _app.TableChat.JoinAsync(ticket.Value.MatchId, table.Lifetime);
            await _app.Mutes.RefreshAsync(table.Lifetime);
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
                _lobbyView = new LobbyView(_shell.Show(_shell.Catalog.Lobby), client, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Leave, code => ShareCode(code), text => _app.TableChat.SendAsync(text, _app.Lifetime), () => ThullaRulesEditor.Summary(client.Model, _app.Presets), () => OpenRulesEditor(client));
                _lobbyView.IsMuted = _app.Mutes.IsMuted;
                _lobbyView.SeatSpectator = client.SeatSpectator;
                _view = _lobbyView;
                return;
            }

            client.Events.Clear();
            _tableView = new ThullaTableView(_shell.Show(_shell.Catalog.Table), client, _app.Chat, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Leave, openSettings: OpenSettings, openRules: OpenRules, sendText: text => _app.TableChat.SendAsync(text, _app.Lifetime));
            ApplySettings();
            var hints = new ThullaHints(_app.Store);
            if (hints.Active)
            {
                _tableView.UseHints(hints);
            }

            _tableView.UseMutes(_app.Mutes.IsMuted, (id, muted) => _ = _app.Mutes.SetAsync(id, muted, _app.Lifetime));
            _tableView.UseModeration(BlockPlayer, (id, reason, done) => ReportPlayer(client, id, reason, done));
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

        private bool ShareCode(string code)
        {
            var shared = _app.ShareSheet.Share(UiText.ShareTitle, string.Format(UiText.ShareText, code));
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
                    var text = string.Format(UiText.RewardLine, reward.Xp, reward.Coins) + (reward.Capped ? UiText.RewardCapped : "");
                    if (reward.LevelTo > reward.LevelFrom)
                    {
                        text += "\n" + string.Format(UiText.LevelUp, reward.LevelFrom, reward.LevelTo);
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
            var account = _app.Sessions.Current == null ? null : new SettingsView.AccountActions(ExportData, DeleteAccount, CancelDeletion, PendingDeletion);
            _settingsView = new SettingsView(_shell.ShowOverlay(_shell.Catalog.Settings), _app.Settings, CloseSettings, account);
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

                PlayerPrefs.SetString(DeleteAtPrefKey, deleted.Value.ToString());
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

            PlayerPrefs.DeleteKey(DeleteAtPrefKey);
            PlayerPrefs.Save();
            status(UiText.DeletionCancelled);
        }

        private static string? PendingDeletion()
        {
            if (!long.TryParse(PlayerPrefs.GetString(DeleteAtPrefKey, ""), out var at) || at <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                return null;
            }

            return string.Format(UiText.DeletePending, DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString("d MMMM yyyy"));
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
            var steps = ThullaTutorial.Steps();
            var chosen = refresher ? new[] { steps[1], steps[2] } : steps;
            var gateway = new ScriptedTableGateway(chosen, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), TutorialText.HowToPlay);
            var client = new TableClient(gateway, new TableTicket("tutorial", "", ""), "me", 1);
            var view = new ThullaTableView(_shell.Show(_shell.Catalog.Table), client, _app.Chat, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), EndTutorial);
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
        }

        private static void MarkTutorialSeen()
        {
            PlayerPrefs.SetInt(TutorialSeenPrefKey, 1);
            PlayerPrefs.Save();
        }

        private void CloseView()
        {
            _app.HomeFriends.Active = false;
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
