using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Nakama;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>
    /// Guest sign-in with a device account (GDD 9.1). The device id is created once and kept in player
    /// prefs; <c>-hulla-device &lt;id&gt;</c> on the command line overrides it so several local
    /// instances can sign in as different players.
    /// </summary>
    public sealed class NakamaSessionService : ISessionService
    {
        private const string DeviceKey = "hulla.device_id";
        private readonly NakamaConnection _connection;
        private readonly string? _deviceId;

        /// <param name="deviceId">A fixed device id (tests); null reads the command line, then player prefs.</param>
        public NakamaSessionService(NakamaConnection connection, string? deviceId = null)
        {
            _connection = connection;
            _deviceId = deviceId;
        }

        public PlayerSession? Current { get; private set; }

        public void Forget() => Current = null;

        public async Awaitable<Result<PlayerSession>> SignInAsync(string displayName, CancellationToken cancellation)
        {
            try
            {
                var client = _connection.Client;
                var session = await client.AuthenticateDeviceAsync(_deviceId ?? DeviceId(), null, true, null, null, cancellation);
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    await client.UpdateAccountAsync(session, null, displayName.Trim(), null, null, null, null, null, cancellation);
                }

                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    await SyncProfileNameAsync(client, session, displayName.Trim(), cancellation);
                }

                var socket = client.NewSocket(true);
                await socket.ConnectAsync(session, true);
                _connection.Session = session;
                _connection.Socket = socket;
                Current = new PlayerSession(session.UserId, string.IsNullOrWhiteSpace(displayName) ? session.Username : displayName.Trim());
                return Result<PlayerSession>.Ok(Current);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"sign-in failed: {error.Message}");
                return Result<PlayerSession>.Fail(ServerErrors.CodeOf(error, "session.unavailable"), error.GetType().Name);
            }
        }

        public async Awaitable<Result<bool>> ReconnectAsync(CancellationToken cancellation)
        {
            var session = _connection.Session;
            if (session == null || Current == null)
            {
                return Result<bool>.Fail("session.not_signed_in");
            }

            try
            {
                var client = _connection.Client;
                if (session.HasExpired(DateTime.UtcNow.AddMinutes(1)))
                {
                    session = await client.SessionRefreshAsync(session, null, null, cancellation);
                    _connection.Session = session;
                }

                _connection.Close();
                var socket = client.NewSocket(true);
                await socket.ConnectAsync(session, true);
                _connection.Socket = socket;
                return Result<bool>.Ok(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"reconnect failed: {error.Message}");
                return Result<bool>.Fail("session.unavailable", error.GetType().Name);
            }
        }

        /// <summary>
        /// The profile (what friends, presence and recent players show) starts with a generated guest
        /// name; the name the player typed replaces it. Only a different name is sent.
        /// </summary>
        private static async Awaitable SyncProfileNameAsync(IClient client, ISession session, string name, CancellationToken cancellation)
        {
            try
            {
                var current = await client.RpcAsync(session, "profile_get", "{}", null, cancellation);
                var profile = Newtonsoft.Json.Linq.JObject.Parse(current.Payload)["profile"];
                if ((string?)profile?["displayName"] == name)
                {
                    return;
                }

                var update = new Newtonsoft.Json.Linq.JObject { ["displayName"] = name };
                await client.RpcAsync(session, "profile_update", update.ToString(Newtonsoft.Json.Formatting.None), null, cancellation);
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                Debug.LogWarning($"profile name not updated: {error.Message}");
            }
        }

        private static string DeviceId()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-hulla-device")
                {
                    // Nakama wants 10 to 128 bytes; short names from the command line get a fixed prefix.
                    return args[i + 1].Length >= 10 ? args[i + 1] : "hulla-dev-" + args[i + 1];
                }
            }

            var stored = PlayerPrefs.GetString(DeviceKey, "");
            if (stored.Length > 0)
            {
                return stored;
            }

            var created = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(DeviceKey, created);
            PlayerPrefs.Save();
            return created;
        }
    }
}
