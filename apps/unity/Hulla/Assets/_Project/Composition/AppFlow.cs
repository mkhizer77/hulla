using System;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Domain;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Screens.Lobby;
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
        }

        private readonly AppScope _app;
        private readonly ShellView _shell;
        private Screen _screen;
        private IDisposable? _view;
        private AnimationScheduler? _scheduler;
        private ThullaTableView? _tableView;

        public AppFlow(AppScope app, ShellView shell)
        {
            _app = app;
            _shell = shell;
            _shell.Ticked += Tick;
            _app.Tables.Disconnected += OnDisconnected;
        }

        public void Start() => ShowHome();

        public void Dispose()
        {
            _shell.Ticked -= Tick;
            _app.Tables.Disconnected -= OnDisconnected;
            CloseView();
        }

        private void ShowHome()
        {
            CloseView();
            _screen = Screen.Home;
            var root = _shell.Show(_shell.Catalog.Home);
            var server = Debug.isDebugBuild ? _app.Config.HostAndPort : null;
            _view = new HomeView(root, _app.Home, server, OnCreate, OnJoin);
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

        private void OnDisconnected()
        {
            _app.Session?.EndTable();
            _app.EndSession();
            ShowHome();
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
                _view = new LobbyView(_shell.Show(_shell.Catalog.Lobby), client, Leave);
                return;
            }

            client.Events.Clear();
            _tableView = new ThullaTableView(_shell.Show(_shell.Catalog.Table), client, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Leave);
            _scheduler = new AnimationScheduler(client.Events, _tableView);
            _view = _tableView;
        }

        private void Tick(float deltaSeconds)
        {
            _scheduler?.Tick(deltaSeconds);
            _tableView?.Tick(deltaSeconds);
        }

        private void CloseView()
        {
            _view?.Dispose();
            _view = null;
            _tableView = null;
            _scheduler = null;
            _screen = Screen.None;
        }
    }
}
