using System;
using System.Collections;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Infrastructure.Nakama;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>
    /// Three real client stacks (connection, guest sign-in, gateway, table client) in one process play a
    /// private Thulla round against a running Nakama. Runs only when HULLA_NAKAMA_HOST is set, e.g.
    /// HULLA_NAKAMA_HOST=127.0.0.1 HULLA_NAKAMA_PORT=7350 with server/nakama/compose.yml up.
    /// </summary>
    public sealed class RoundAgainstNakamaTests
    {
        private const float RoundSeconds = 180f;

        private sealed class Player
        {
            public Player(string name, string host, int port, string stamp)
            {
                Name = name;
                Connection = NakamaConnection.Create("http", host, port, "local-only-server-key");
                Session = new NakamaSessionService(Connection, "hulla-it-" + name + "-" + stamp);
                Gateway = new NakamaTableGateway(Connection);
            }

            public string Name { get; }

            public NakamaConnection Connection { get; }

            public NakamaSessionService Session { get; }

            public NakamaTableGateway Gateway { get; }

            public TableClient? Client { get; set; }

            public int Plays { get; set; }

            public bool SawRoundStart { get; set; }
        }

        private readonly List<Player> _players = new List<Player>();

        [TearDown]
        public void TearDown()
        {
            foreach (var player in _players)
            {
                player.Client?.Dispose();
                player.Gateway.Dispose();
                player.Connection.Close();
            }

            _players.Clear();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ThreeClientsCreateJoinByCodeAndPlayARoundToItsResult() => Run(false);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AClientThatDropsMidRoundReconnectsRejoinsAndFinishesTheRound() => Run(true);

        private IEnumerator Run(bool dropOne)
        {
            var host = Environment.GetEnvironmentVariable("HULLA_NAKAMA_HOST");
            if (string.IsNullOrEmpty(host))
            {
                Assert.Ignore("HULLA_NAKAMA_HOST is not set");
            }

            var port = int.TryParse(Environment.GetEnvironmentVariable("HULLA_NAKAMA_PORT"), out var p) ? p : 7350;
            var run = PlayRoundAsync(host!, port, dropOne);
            var awaiter = run.GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            awaiter.GetResult();
        }

        private async Awaitable PlayRoundAsync(string host, int port, bool dropOne)
        {
            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            foreach (var name in new[] { "ana", "bilal", "chand" })
            {
                var player = new Player(name, host, port, stamp);
                _players.Add(player);
                var signedIn = await player.Session.SignInAsync(name, default);
                Assert.IsTrue(signedIn.IsOk, name + " sign-in: " + signedIn.Error);
            }

            var tickets = new Dictionary<Player, TableTicket>();
            var created = await _players[0].Gateway.CreateTableAsync(default);
            Assert.IsTrue(created.IsOk, "table_create: " + created.Error);
            var code = created.Value.Code;
            foreach (var player in _players)
            {
                var ticket = player == _players[0] ? created : await player.Gateway.JoinByCodeAsync(code, default);
                Assert.IsTrue(ticket.IsOk, player.Name + " join by code: " + ticket.Error);
                Assert.AreEqual(created.Value.MatchId, ticket.Value.MatchId);
                tickets[player] = ticket.Value;
                var client = new TableClient(player.Gateway, ticket.Value, player.Session.Current!.UserId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                player.Client = client;
                var captured = player;
                client.Model.Changed += () => Act(captured);
                var entered = await player.Gateway.EnterAsync(ticket.Value, default);
                Assert.IsTrue(entered.IsOk, player.Name + " enter: " + entered.Error);
            }

            await WaitFor(() => Seated(_players[0]) == 3, 20f, "three seats taken");
            foreach (var player in _players)
            {
                Assert.AreEqual(code, player.Client!.Model.Code);
                player.Client.SetReady(true);
            }

            await WaitFor(() => AllReady(_players[0]), 20f, "everyone ready");
            Assert.IsTrue(_players[0].Client!.Model.IsHost);
            _players[0].Client!.StartRound();
            if (dropOne)
            {
                var dropped = _players[2];
                await WaitFor(() => dropped.Client!.Model.InRound, 20f, "the round started");
                dropped.Connection.Close();
                await Awaitable.WaitForSecondsAsync(1f);
                var reconnected = await dropped.Session.ReconnectAsync(default);
                Assert.IsTrue(reconnected.IsOk, "reconnect: " + reconnected.Error);
                var rejoined = await dropped.Gateway.EnterAsync(tickets[dropped], default);
                Assert.IsTrue(rejoined.IsOk, "rejoin: " + rejoined.Error);
                await WaitFor(() => dropped.Client!.Model.Hand.Count > 0 || dropped.Client.Model.LastResult != null, 20f, "the hand came back with table_state");
            }

            await WaitFor(() => _players.TrueForAll(pl => pl.Client!.Model.LastResult != null), RoundSeconds, "every client saw round_end");

            var first = _players[0].Client!.Model.LastResult!;
            Assert.IsNotEmpty(first.Fairness.Seed, "the seed is revealed at round end");
            foreach (var player in _players)
            {
                var result = player.Client!.Model.LastResult!;
                Assert.IsTrue(player.SawRoundStart, player.Name + " saw the round start");
                Assert.Greater(player.Plays, 0, player.Name + " played at least one card");
                Assert.AreEqual(first.RoundId, result.RoundId);
                Assert.AreEqual(first.BhabhiSeat, result.BhabhiSeat);
                CollectionAssert.AreEqual(first.EscapeOrder, result.EscapeOrder);
                Debug.Log($"{player.Name}: {player.Plays} plays, bhabhi seat {result.BhabhiSeat}");
            }
        }

        /// <summary>The scripted policy: play the first legal card, otherwise take the hand when offered.</summary>
        private static void Act(Player player)
        {
            var client = player.Client;
            if (client == null)
            {
                return;
            }

            var model = client.Model;
            player.SawRoundStart |= model.InRound;
            if (!model.MyTurn || model.PendingCard != null)
            {
                return;
            }

            foreach (var card in model.Hand)
            {
                if (model.IsLegal(card) && client.PlayCard(card))
                {
                    player.Plays++;
                    return;
                }
            }

            if (model.CanTakeHand)
            {
                client.TakeHand();
            }
        }

        private static int Seated(Player player)
        {
            var count = 0;
            foreach (var seat in player.Client!.Model.Seats)
            {
                count += seat.Occupied ? 1 : 0;
            }

            return count;
        }

        private static bool AllReady(Player player)
        {
            foreach (var seat in player.Client!.Model.Seats)
            {
                if (seat.Occupied && !seat.Ready)
                {
                    return false;
                }
            }

            return true;
        }

        private static async Awaitable WaitFor(Func<bool> condition, float seconds, string what)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("timed out waiting: " + what);
                }

                await Awaitable.NextFrameAsync();
            }
        }
    }
}
