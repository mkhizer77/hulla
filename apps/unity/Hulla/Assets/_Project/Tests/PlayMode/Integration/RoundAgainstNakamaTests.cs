using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

            public List<ChatLine> Chat { get; } = new List<ChatLine>();

            public List<TextLine> Texts { get; } = new List<TextLine>();

            public NakamaTableChat? TableChat { get; set; }
        }

        private readonly List<Player> _players = new List<Player>();

        [TearDown]
        public void TearDown()
        {
            foreach (var player in _players)
            {
                player.Client?.Dispose();
                player.TableChat?.Dispose();
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
                client.Model.Chat += captured.Chat.Add;
                var entered = await player.Gateway.EnterAsync(ticket.Value, default);
                Assert.IsTrue(entered.IsOk, player.Name + " enter: " + entered.Error);
            }

            await WaitFor(() => Seated(_players[0]) == 3, 20f, "three seats taken");
            await WaitFor(() => _players.TrueForAll(pl => pl.Client!.Model.Code.Length > 0), 10f, "every client got its table_state");
            if (!dropOne)
            {
                // The host's rules reach everyone in the lobby (GDD 6.13, E19).
                _players[0].Client!.SetRules(new Protocol.RulesSelection
                {
                    GameId = Protocol.GameId.Thulla,
                    Preset = Protocol.Preset.Family,
                    Settings = new Protocol.Settings { TurnTimerSeconds = 30, LoserTitle = Protocol.LoserTitle.Donkey },
                });
                await WaitFor(() => _players.TrueForAll(pl => pl.Client!.Model.Selection?.Preset == Protocol.Preset.Family), 10f, "the rules reached every client");
                Assert.AreEqual(30, _players[2].Client!.Model.Selection!.Settings!.TurnTimerSeconds);
                Assert.AreEqual(_players[0].Client!.Model.MySeat, _players[2].Client!.Model.RulesChangedBy);
            }

            foreach (var player in _players)
            {
                Assert.AreEqual(code, player.Client!.Model.Code);
                player.Client.SetReady(true);
            }

            await WaitFor(() => AllReady(_players[0]), 20f, "everyone ready");
            if (!dropOne)
            {
                _players[1].Client!.SendQuickChat("T01");
                _players[2].Client!.SendEmote("chai", _players[0].Client!.Model.MySeat);
                await WaitFor(() => _players.TrueForAll(pl => pl.Chat.Count >= 2), 10f, "chat reached every client");
                var said = _players[0].Chat.Find(c => c.PhraseId != null);
                Assert.AreEqual("T01", said.PhraseId);
                Assert.AreEqual(_players[1].Client!.Model.MySeat, said.Seat);
                Assert.AreEqual(_players[0].Client!.Model.MySeat, _players[0].Chat.Find(c => c.EmoteId == "chai").TargetSeat);

                foreach (var player in _players)
                {
                    player.TableChat = new NakamaTableChat(player.Connection);
                    var captured = player;
                    player.TableChat.Received += captured.Texts.Add;
                    Assert.IsTrue((await player.TableChat.JoinAsync(created.Value.MatchId, default)).IsOk, player.Name + " joins the table channel");
                }

                Assert.IsTrue((await _players[1].TableChat!.SendAsync("salaam sab ko", default)).IsOk);
                Assert.IsFalse((await _players[1].TableChat!.SendAsync("again", default)).IsOk, "one message per 2 s");
                Assert.IsFalse((await _players[2].TableChat!.SendAsync("see www.example.com", default)).IsOk, "no links");
                await WaitFor(() => _players.TrueForAll(pl => pl.Texts.Count >= 1), 10f, "the text reached every client");
                Assert.AreEqual("salaam sab ko", _players[0].Texts[0].Text);
                Assert.AreEqual(_players[1].Session.Current!.UserId, _players[0].Texts[0].SenderId);
            }

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

            if (!dropOne)
            {
                // Each player is paid for the round (GDD 12.2, 12.3) through the ledger.
                foreach (var player in _players)
                {
                    var progress = new Hulla.Application.ViewModels.ProgressViewModel(new NakamaProgressService(player.Connection));
                    for (var attempt = 0; attempt < 5 && progress.RewardFor(first.RoundId) == null; attempt++)
                    {
                        await progress.RefreshAsync(0, default);
                        if (progress.RewardFor(first.RoundId) == null)
                        {
                            await Awaitable.WaitForSecondsAsync(0.5f);
                        }
                    }

                    var reward = progress.RewardFor(first.RoundId);
                    Assert.IsNotNull(reward, player.Name + " was paid for the round");
                    Assert.Greater(reward!.Xp, 0, player.Name + " got XP");
                    Assert.AreEqual(250, reward.WelcomeCoins, player.Name + "'s first round paid the welcome reward (GDD 17.2)");
                    Assert.AreEqual(reward.Coins + reward.WelcomeCoins, progress.Coins, player.Name + "'s wallet holds the round's coins and the welcome reward");
                }

                // Missions count the round (GDD 12.5).
                var missions = new Hulla.Application.ViewModels.MissionsViewModel(new NakamaMissionsService(_players[0].Connection));
                await missions.RefreshAsync(0, default);
                Assert.AreEqual(3, missions.Daily.Count, "three dailies");
                Assert.AreEqual(5, missions.Weekly.Count, "five weeklies");
                Assert.IsTrue(missions.Weekly.Concat(missions.Daily).Any(m => m.Progress > 0), "the round counted toward something");

                // The daily reward pays once a day through the ledger (GDD 12.6).
                var progressAfter = new Hulla.Application.ViewModels.ProgressViewModel(new NakamaProgressService(_players[1].Connection));
                await progressAfter.RefreshAsync(0, default);
                var coinsBefore = progressAfter.Coins;
                var daily = new Hulla.Application.ViewModels.DailyRewardViewModel(new NakamaDailyRewardService(_players[1].Connection));
                await daily.RefreshAsync(0, default);
                Assert.IsTrue(daily.Status!.Claimable, "a new player's first daily reward is waiting");
                var dayOne = daily.Status.Today.Coins;
                Assert.IsTrue(await daily.ClaimAsync(default));
                Assert.IsFalse(daily.Status!.Claimable, "claimed for today");
                await progressAfter.RefreshAsync(0, default);
                Assert.AreEqual(coinsBefore + dayOne, progressAfter.Coins, "the wallet holds the daily coins");
                await daily.RefreshAsync(0, default);
                Assert.IsFalse(daily.Status!.Claimable, "still claimed after a refresh");

                // Round points post to the weekly board, friends scope included (GDD 12.8).
                var board = new Hulla.Application.ViewModels.LeaderboardViewModel(new NakamaLeaderboardService(_players[0].Connection));
                await board.ShowAsync(Hulla.Application.Progress.LeaderboardPeriod.Weekly, false, default);
                Assert.IsNull(board.ErrorCode, "the weekly board answers");
                Assert.Greater(board.Page!.Rows.Count, 0, "the round's points are on the weekly board");
                await board.ShowAsync(Hulla.Application.Progress.LeaderboardPeriod.Monthly, true, default);
                Assert.IsNull(board.ErrorCode, "the friends view answers");
                Debug.Log($"friends' monthly board: {board.Page!.Rows.Count} rows, me {board.Page.Me?.Points}");

                // The shop: credit the account with the server key, buy a table and equip it (GDD 12.11).
                var buyer = _players[2];
                await GrantAsync(buyer.Session.Current!.UserId, 5000);
                var shop = new Hulla.Application.ViewModels.ShopViewModel(new NakamaShopService(buyer.Connection));
                await shop.RefreshAsync(default);
                Assert.IsNull(shop.ErrorCode, "the catalog loads");
                Assert.IsTrue(await shop.BuyAsync("table_marble", default), "bought with granted coins");
                Assert.IsTrue(await shop.EquipAsync("table_marble", default), "equipped");
                shop.Show("table");
                Assert.IsTrue(shop.Shown.Any(i => i.Id == "table_marble" && i.Equipped));
                Assert.IsFalse(await shop.BuyAsync("table_marble", default), "a second purchase is refused");
                Assert.AreEqual("economy.item_owned", shop.ErrorCode);

                // The season pass answers; between seasons there is none (GDD 12.9).
                var pass = new Hulla.Application.ViewModels.PassViewModel(new NakamaPassService(_players[0].Connection));
                await pass.RefreshAsync(default);
                Assert.IsNull(pass.ErrorCode, "pass_get answers");
                Assert.IsTrue(pass.NoSeason || pass.Info!.Tiers.Count == 40, "no season, or forty tiers");

                // Invites and the notification switches (GDD 10.5, 10.10): seats are still held after the round.
                string? inviteNotice = null;
                _players[1].Session.Notified += (subject, content) =>
                {
                    if (subject == ServerNotices.TableInvite)
                    {
                        inviteNotice = content;
                    }
                };
                var notifications = new NakamaNotificationsService(_players[0].Connection);
                var invited = await notifications.InviteAsync(new[] { _players[1].Session.Current!.UserId, _players[0].Session.Current!.UserId }, default);
                Assert.IsTrue(invited.IsOk, invited.IsOk ? "" : invited.Error.Code);
                CollectionAssert.Contains(invited.Value.Sent, _players[1].Session.Current!.UserId);
                Assert.AreEqual("self", invited.Value.Skipped[_players[0].Session.Current!.UserId]);
                for (var i = 0; i < 20 && inviteNotice == null; i++)
                {
                    await Awaitable.WaitForSecondsAsync(0.1f);
                }

                Assert.IsNotNull(inviteNotice, "the invited player got the in-app notice");
                Assert.AreEqual(_players[0].Client!.Model.Code, ServerNotices.Invite(inviteNotice!)!.Value.Code);
                var switches = new Hulla.Application.ViewModels.NotificationSettingsViewModel(notifications);
                await switches.RefreshAsync(default);
                await switches.ChangeAsync(n => n.FriendOnline = false, default);
                await switches.RefreshAsync(default);
                Assert.IsFalse(switches.Settings!.FriendOnline, "the switch is kept on the server");

                // The round counts toward achievements (GDD 12.7).
                var achievements = new Hulla.Application.ViewModels.AchievementsViewModel(new NakamaAchievementsService(_players[0].Connection));
                await achievements.RefreshAsync(default);
                Assert.IsNull(achievements.ErrorCode);
                Assert.Greater(achievements.Total, 0, "the list came from content");
                Assert.IsTrue(achievements.Groups.SelectMany(g => g.Items).Any(a => a.Metric == "rounds" && a.Progress >= 1), "the round counted");
            }
        }

        /// <summary>Credits an account through admin_grant with the server's HTTP key (local compose only).</summary>
        private static async Awaitable GrantAsync(string userId, long coins)
        {
            var host = System.Environment.GetEnvironmentVariable("HULLA_NAKAMA_HOST") ?? "127.0.0.1";
            var port = System.Environment.GetEnvironmentVariable("HULLA_NAKAMA_PORT") ?? "7350";
            var body = $"{{\"userId\":\"{userId}\",\"coins\":{coins},\"gems\":0,\"reason\":\"test\",\"sourceId\":\"it-{userId}\"}}";
            using var request = new UnityEngine.Networking.UnityWebRequest($"http://{host}:{port}/v2/rpc/admin_grant?http_key=local-only-http-key&unwrap", "POST");
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            await request.SendWebRequest();
            Assert.AreEqual(UnityEngine.Networking.UnityWebRequest.Result.Success, request.result, request.downloadHandler.text);
            StringAssert.Contains("\"ok\":true", request.downloadHandler.text);
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
