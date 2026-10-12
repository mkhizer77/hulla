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

        public NakamaSessionService(NakamaConnection connection)
        {
            _connection = connection;
        }

        public PlayerSession? Current { get; private set; }

        public void Forget() => Current = null;

        public async Awaitable<Result<PlayerSession>> SignInAsync(string displayName, CancellationToken cancellation)
        {
            try
            {
                var client = _connection.Client;
                var session = await client.AuthenticateDeviceAsync(DeviceId(), null, true, null, null, cancellation);
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    await client.UpdateAccountAsync(session, null, displayName.Trim(), null, null, null, null, null, cancellation);
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
                return Result<PlayerSession>.Fail("session.unavailable", error.GetType().Name);
            }
        }

        private static string DeviceId()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-hulla-device")
                {
                    return args[i + 1];
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
