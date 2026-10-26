using System;
using System.Collections;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Infrastructure.Nakama;
using Hulla.Infrastructure.Voice;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>
    /// Two seated clients take room tokens from `voice_token` and join the compose LiveKit with the real
    /// adapter (ADR-0024): one publishes its microphone and sends, one listens; the listener leaves the table and the
    /// server drops it from the room. Runs only when
    /// HULLA_NAKAMA_HOST is set, with server/nakama/compose.yml up.
    /// </summary>
    public sealed class VoiceAgainstLiveKitTests
    {
        private readonly List<IDisposable> _owned = new List<IDisposable>();
        private readonly List<NakamaConnection> _connections = new List<NakamaConnection>();

        [TearDown]
        public void TearDown()
        {
            foreach (var owned in _owned)
            {
                owned.Dispose();
            }

            foreach (var connection in _connections)
            {
                connection.Close();
            }

            _owned.Clear();
            _connections.Clear();
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator ASenderAndAListenerJoinTheTableRoomAndALeaverIsRemoved()
        {
            var host = Environment.GetEnvironmentVariable("HULLA_NAKAMA_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Ignore("HULLA_NAKAMA_HOST is not set");
            }

            var port = int.TryParse(Environment.GetEnvironmentVariable("HULLA_NAKAMA_PORT"), out var p) ? p : 7350;
            var awaiter = RunAsync(host!, port).GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            awaiter.GetResult();
        }

        private async Awaitable RunAsync(string host, int port)
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var gateways = new List<NakamaTableGateway>();
            var tokens = new List<NakamaVoiceTokens>();
            var accounts = new List<string>();
            foreach (var name in new[] { "vana", "vbilal" })
            {
                var connection = NakamaConnection.Create("http", host, port, "local-only-server-key");
                _connections.Add(connection);
                var session = new NakamaSessionService(connection, "hulla-it-" + name + "-" + stamp);
                var signedIn = await session.SignInAsync(name, default);
                Assert.IsTrue(signedIn.IsOk, name + " sign-in: " + signedIn.Error);
                var gateway = new NakamaTableGateway(connection);
                _owned.Add(gateway);
                gateways.Add(gateway);
                tokens.Add(new NakamaVoiceTokens(connection));
                accounts.Add(session.Current!.UserId);
            }

            var created = await gateways[0].CreateTableAsync(default);
            Assert.IsTrue(created.IsOk, "table_create: " + created.Error);
            var tickets = new List<TableTicket> { created.Value };
            var joined = await gateways[1].JoinByCodeAsync(created.Value.Code, default);
            Assert.IsTrue(joined.IsOk, "join by code: " + joined.Error);
            tickets.Add(joined.Value);
            var clients = new List<TableClient>();
            for (var i = 0; i < 2; i++)
            {
                var client = new TableClient(gateways[i], tickets[i], accounts[i], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                _owned.Add(client);
                clients.Add(client);
                var entered = await gateways[i].EnterAsync(tickets[i], default);
                Assert.IsTrue(entered.IsOk, "enter: " + entered.Error);
            }

            await WaitFor(() => clients.TrueForAll(c => c.Model.MySeat != null), 20f, "both seated");

            var voices = new List<LiveKitVoiceService>();
            for (var i = 0; i < 2; i++)
            {
                var ticket = await tokens[i].TokenAsync(created.Value.MatchId, default);
                Assert.IsTrue(ticket.IsOk, "voice_token: " + ticket.Error);
                Assert.AreEqual(accounts[i], ticket.Value.Account);
                StringAssert.StartsWith("ws", ticket.Value.Url);
                var voice = new LiveKitVoiceService();
                _owned.Add(voice);
                voices.Add(voice);
                // The first seat sends; the second listens through a Unity audio source without the microphone.
                var inRoom = await voice.JoinAsync(ticket.Value, i == 0, default);
                Assert.IsTrue(inRoom.IsOk, "join the room: " + inRoom.Error);
            }

            voices[0].SetTransmit(true);
            await Awaitable.WaitForSecondsAsync(2f);
            voices[0].SetTransmit(false);
            voices[1].SetPeerMuted(accounts[0], true);
            voices[1].SetPeerMuted(accounts[0], false);

            // Leaving the table takes the player out of the voice room on the server side too.
            var dropped = false;
            voices[1].Dropped += () => dropped = true;
            await gateways[1].LeaveAsync();
            await WaitFor(() => dropped, 15f, "the server to remove the leaver from voice");
            await voices[0].LeaveAsync();
        }

        private static async Awaitable WaitFor(Func<bool> done, float seconds, string what)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("timed out waiting for " + what);
                }

                await Awaitable.NextFrameAsync();
            }
        }
    }
}
