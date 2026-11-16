using System;
using System.Collections;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Infrastructure.Nakama;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Integration.Tests
{
    /// <summary>
    /// The real client against a running Nakama (Rung design 8): a private Thulla room becomes a Rung
    /// room without leaving the match, the host fills it with bots, plays a whole deal from what the
    /// server sends, and moves the room back. Runs only when HULLA_NAKAMA_HOST is set.
    /// </summary>
    public sealed class RungAgainstNakamaTests
    {
        private NakamaConnection? _connection;
        private NakamaTableGateway? _gateway;
        private TableClient? _client;

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
            _gateway?.Dispose();
            _connection?.Close();
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AThullaRoomSwitchesToRungPlaysADealWithBotsAndSwitchesBack()
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
            _connection = NakamaConnection.Create(IntegrationServer.Scheme, host, port, IntegrationServer.ServerKey);
            var session = new NakamaSessionService(_connection, "hulla-rung-" + stamp);
            Assert.IsTrue((await session.SignInAsync("rungster", default)).IsOk, "sign-in");
            _gateway = new NakamaTableGateway(_connection);
            var created = await _gateway.CreateTableAsync(default);
            Assert.IsTrue(created.IsOk, "table_create: " + created.Error);
            var ticket = created.Value!;
            _client = new TableClient(_gateway, ticket, session.Current!.UserId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var model = _client.Model;
            Assert.IsTrue((await _gateway.EnterAsync(ticket, default)).IsOk, "enter");
            await WaitFor(() => model.MySeat != null, 10f, "seated");
            Assert.IsFalse(model.IsRung);

            // The room changes game; the match, and so the voice room, stays.
            _client.SwitchGame();
            await WaitFor(() => model.IsRung && model.Seats.Count == RungModel.Seats, 10f, "the room plays Rung");
            Assert.AreEqual(Preset.Rung, model.Selection?.Preset ?? Preset.Rung);
            for (var i = 0; i < 3; i++)
            {
                _client.AddBot();
            }

            await WaitFor(() => model.Seats.Count == 4 && Bots(model) == 3, 10f, "three bots seated");
            _client.SetReady(true);
            await WaitFor(() => model.Seats[(int)model.MySeat!.Value].Ready, 10f, "ready");
            _client.StartRound();
            await WaitFor(() => model.InRound, 10f, "the deal starts");
            Assert.AreEqual(5, model.Rung.Hand.Count, "five cards before the call");

            var rung = model.Rung;
            var sawThirteen = false;
            var plays = 0;
            var deadline = Time.realtimeSinceStartup + 200f;
            while (rung.LastResult == null)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "the deal ends; phase " + rung.Phase + ", turn " + model.TurnSeat + ", error " + model.LastErrorCode);
                sawThirteen |= rung.Hand.Count == 13;
                if (model.MyTurn && rung.PendingCard == null)
                {
                    if (rung.Phase == RungPhase.Call && rung.CallableSuits.Count > 0)
                    {
                        Assert.IsTrue(_client.CallTrump(rung.CallableSuits[0]));
                        await WaitFor(() => !model.MyTurn || rung.Phase != RungPhase.Call, 10f, "the call is answered");
                    }
                    else if (rung.Phase == RungPhase.CourtChoice)
                    {
                        Assert.IsTrue(_client.CourtChoice(false));
                        await WaitFor(() => rung.Phase != RungPhase.CourtChoice, 10f, "the choice is answered");
                    }
                    else
                    {
                        string? card = null;
                        foreach (var held in rung.Hand)
                        {
                            if (rung.IsLegal(held))
                            {
                                card = held;
                                break;
                            }
                        }

                        if (card != null)
                        {
                            var before = rung.Hand.Count;
                            Assert.IsTrue(_client.PlayCard(card));
                            plays++;
                            await WaitFor(() => rung.Hand.Count < before || rung.LastResult != null, 10f, "my card is applied");
                        }
                    }
                }

                await Awaitable.NextFrameAsync();
            }

            Assert.IsTrue(sawThirteen, "the rest of the deal reached my hand");
            Assert.Greater(plays, 3);
            Assert.IsNull(model.LastErrorCode, "nothing was refused");
            Assert.IsNotNull(rung.Trump);
            Assert.GreaterOrEqual(rung.LastResult!.Counts[(int)rung.LastResult.WinnerTeam], 7);
            Assert.IsNotNull(rung.Teams, "the bazi score arrived with the result");
            Assert.IsTrue(model.ShowsTable);
            Assert.IsFalse(model.InRound);

            // Back to Thulla in the same match: the bots go home and the lobby shows again.
            _client.SwitchGame();
            // game_changed comes first; the table state with the seats follows it.
            await WaitFor(() => !model.IsRung && Bots(model) == 0, 10f, "the room plays Thulla again without bots");
            Assert.IsFalse(model.ShowsTable);
            Assert.AreEqual(ticket.Code, model.Code, "the same room");
        }

        private static int Bots(TableModel model)
        {
            var count = 0;
            foreach (var seat in model.Seats)
            {
                count += seat.Bot ? 1 : 0;
            }

            return count;
        }

        private static async Awaitable WaitFor(Func<bool> done, float seconds, string what)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                Assert.Less(Time.realtimeSinceStartup, until, "timed out waiting for " + what);
                await Awaitable.NextFrameAsync();
            }
        }
    }
}
