using System;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Domain;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Screens.Lobby;
using Hulla.Presentation.Screens.QuickPlay;
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

        /// <summary>Set to 1 for reduce-motion until the settings screen (#64) offers the switch.</summary>
        private const string ReduceMotionPrefKey = "hulla.reduce_motion";

        /// <summary>1 once the player chose "don't show again" on the rules recap.</summary>
        private const string RecapOffPrefKey = "hulla.recap_off";

        private readonly AppScope _app;
        private readonly ShellView _shell;
        private Screen _screen;
        private IDisposable? _view;
        private AnimationScheduler? _scheduler;
        private ThullaTableView? _tableView;
        private bool _reconnecting;
        private LobbyView? _lobbyView;

        public AppFlow(AppScope app, ShellView shell)
        {
            _app = app;
            _shell = shell;
            _shell.Ticked += Tick;
            _app.QuickPlay.Found += OnQuickPlayFound;
            _app.Tables.Disconnected += OnDisconnected;
        }

        public async void Start()
        {
            ShowHome();
            try
            {
                await _app.Chat.LoadAsync(_app.Lifetime);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose()
        {
            _shell.Ticked -= Tick;
            _app.QuickPlay.Found -= OnQuickPlayFound;
            _app.Tables.Disconnected -= OnDisconnected;
            CloseView();
        }

        private void ShowHome()
        {
            CloseView();
            _screen = Screen.Home;
            var root = _shell.Show(_shell.Catalog.Home);
            var server = Debug.isDebugBuild ? _app.Config.HostAndPort : null;
            _view = new HomeView(root, _app.Home, server, OnCreate, OnJoin, OnQuickPlay);
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
            var player = _app.Sessions.Current;
            if (!ticket.IsOk || player == null)
            {
                return;
            }

            var session = _app.BeginSession(player);
            var table = session.BeginTable(ticket.Value);
            table.Client.Model.Changed += FollowModel;
            var entered = await _app.Tables.EnterAsync(ticket.Value, table.Lifetime);
            if (!entered.IsOk)
            {
                Debug.LogWarning($"could not enter the table: {entered.Error}");
                session.EndTable();
                return;
            }

            FollowModel();
        }

        private async void Leave()
        {
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

            var wanted = model.InRound || model.LastResult != null ? Screen.Table : Screen.Lobby;
            if (wanted == _screen)
            {
                return;
            }

            CloseView();
            _screen = wanted;
            if (wanted == Screen.Lobby)
            {
                _lobbyView = new LobbyView(_shell.Show(_shell.Catalog.Lobby), client, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Leave);
                _view = _lobbyView;
                return;
            }

            client.Events.Clear();
            _tableView = new ThullaTableView(_shell.Show(_shell.Catalog.Table), client, _app.Chat, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Leave, PlayerPrefs.GetInt(ReduceMotionPrefKey, 0) == 1, PlayerPrefs.GetInt(RecapOffPrefKey, 0) == 1 ? null : HideRecap);
            _scheduler = new AnimationScheduler(client.Events, _tableView);
            _view = _tableView;
        }

        private static void HideRecap()
        {
            PlayerPrefs.SetInt(RecapOffPrefKey, 1);
            PlayerPrefs.Save();
        }

        private void Tick(float deltaSeconds)
        {
            _app.QuickPlay.Tick(deltaSeconds);
            _lobbyView?.Tick();
            _scheduler?.Tick(deltaSeconds);
            _tableView?.Tick(deltaSeconds);
        }

        private void CloseView()
        {
            _view?.Dispose();
            _view = null;
            _tableView = null;
            _lobbyView = null;
            _scheduler = null;
            _screen = Screen.None;
        }
    }
}
