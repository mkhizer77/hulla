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
    /// Quick Play against a running Nakama (GDD 7.2, 9.6): four searches for a four-seat table land
    /// at one table, which deals itself, and the round plays to its result; a cancelled search is
    /// never matched. Runs only when HULLA_NAKAMA_HOST is set.
    /// </summary>
    public sealed class QuickPlayAgainstNakamaTests
    {
        private sealed class Searcher
        {
            public Searcher(string name, string host, int port, string stamp)
            {
                Name = name;
                Connection = NakamaConnection.Create("http", host, port, "local-only-server-key");
                Session = new NakamaSessionService(Connection, "hulla-qp-" + name + "-" + stamp);
                Gateway = new NakamaTableGateway(Connection);
                Matchmaker = new NakamaMatchmaker(Connection);
                Matchmaker.Matched += t => Ticket = t;
            }

            public string Name { get; }

            public NakamaConnection Connection { get; }

            public NakamaSessionService Session { get; }

            public NakamaTableGateway Gateway { get; }

            public NakamaMatchmaker Matchmaker { get; }

            public TableTicket? Ticket { get; set; }

            public TableClient? Client { get; set; }

            public int Plays { get; set; }
        }

        private readonly List<Searcher> _searchers = new List<Searcher>();

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _searchers)
            {
                s.Client?.Dispose();
                s.Matchmaker.Dispose();
                s.Gateway.Dispose();
                s.Connection.Close();
            }

            _searchers.Clear();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator FourSearchesLandAtOneTableThatDealsItselfAndPlaysARound()
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
            var quitter = new Searcher("quitter", host, port, stamp);
            _searchers.Add(quitter);
            Assert.IsTrue((await quitter.Session.SignInAsync("quitter", default)).IsOk);
            Assert.IsTrue((await quitter.Matchmaker.SearchAsync(4, false, default)).IsOk);
            await quitter.Matchmaker.CancelAsync();

            foreach (var name in new[] { "asad", "bina", "chen", "dua" })
            {
                var s = new Searcher(name, host, port, stamp);
                _searchers.Add(s);
                Assert.IsTrue((await s.Session.SignInAsync(name, default)).IsOk, name + " sign-in");
                var searched = await s.Matchmaker.SearchAsync(4, false, default);
                Assert.IsTrue(searched.IsOk, name + " search: " + searched.Error);
            }

            var players = _searchers.GetRange(1, 4);
            await WaitFor(() => players.TrueForAll(s => s.Ticket != null), 40f, "all four matched");
            Assert.IsNull(quitter.Ticket, "a cancelled search is never matched");
            var matchId = players[0].Ticket!.MatchId;
            foreach (var s in players)
            {
                Assert.AreEqual(matchId, s.Ticket!.MatchId, s.Name + " landed at the same table");
                s.Client = new TableClient(s.Gateway, s.Ticket, s.Session.Current!.UserId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var captured = s;
                s.Client.Model.Changed += () => Act(captured);
                Assert.IsTrue((await s.Gateway.EnterAsync(s.Ticket, default)).IsOk, s.Name + " enter");
            }

            await WaitFor(() => players.TrueForAll(s => s.Client!.Model.Code.Length > 0), 15f, "table_state named the table");
            Assert.IsFalse(players[0].Client!.Model.IsPrivate);
            await WaitFor(() => players.TrueForAll(s => s.Client!.Model.LastResult != null), 180f, "the self-dealt round ended");
            var first = players[0].Client!.Model.LastResult!;
            foreach (var s in players)
            {
                Assert.Greater(s.Plays, 0, s.Name + " played (envelopes carried the learned table id)");
                Assert.AreEqual(first.BhabhiSeat, s.Client!.Model.LastResult!.BhabhiSeat);
            }
        }

        private static void Act(Searcher s)
        {
            var model = s.Client!.Model;
            if (!model.MyTurn || model.PendingCard != null)
            {
                return;
            }

            foreach (var card in model.Hand)
            {
                if (model.IsLegal(card) && s.Client.PlayCard(card))
                {
                    s.Plays++;
                    return;
                }
            }

            if (model.CanTakeHand)
            {
                s.Client.TakeHand();
            }
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
