#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Games.Rung;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Lobby;
using Hulla.Presentation.Share;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// The Rung table and the lobby's game switch on the real screen assets, driven by server messages.
    /// The Render tests are explicit and draw PNGs for eyeballing: <c>-testFilter Hulla.Screens.Tests.RungScreens</c>,
    /// folder in HULLA_SNAPSHOT_OUT.
    /// </summary>
    public sealed class RungScreens
    {
        private static readonly string[] Names = { "Karachi Tiger 81", "Ali", "Sara", "Noor" };

        private static ScreenCatalog Catalog => AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");

        private static List<Seat> Seats(int bots = 0, int empty = 0)
        {
            var seats = new List<Seat>();
            for (var i = 0; i < 4; i++)
            {
                var free = i >= 4 - empty;
                var bot = !free && i >= 4 - empty - bots;
                seats.Add(new Seat { SeatSeat = i, PlayerId = free ? null : i == 0 ? "me" : bot ? "bot_" + i : "u" + i, DisplayName = free ? null : Names[i], Connected = !free, Ready = !free, Bot = bot ? true : (bool?)null, Avatar = free ? null : "a0" + (i + 2) });
            }

            return seats;
        }

        private static TableState Table(GameId game, RungView? view, long seq, int bots = 0, int empty = 0, SeriesState? series = null) => new TableState
        {
            GameId = game,
            Code = "K7Q2MX",
            Mode = TableMode.Private,
            Status = view == null ? TableStatus.Open : TableStatus.InRound,
            HostSeat = 0,
            LastSeq = seq,
            Seats = Seats(bots, empty),
            Spectators = new List<Spectator>(),
            Rules = new RulesSelection { GameId = game, Preset = game == GameId.Rung ? Preset.Rung : (Preset?)null },
            RungRound = view,
            Series = series ?? new SeriesState { Points = new List<long>(), LoserTally = new List<long>() },
        };

        private static RungView View(RungPhase phase, IList<string> hand, long caller, Suit? trump, long turn, IList<RungPlay>? plays = null, long pile = 0, long? lastWinner = null, long us = 0, long them = 0) => new RungView
        {
            Phase = phase,
            DealerSeat = (caller + 3) % 4,
            CallerSeat = caller,
            Trump = trump,
            TurnSeat = turn,
            Counts = new List<long> { us, them },
            Pile = pile,
            LastWinner = lastWinner,
            HandCounts = new List<long> { hand.Count, hand.Count, hand.Count, hand.Count },
            Hand = new List<string>(hand),
            Rules = new RungRules { Collect = Collect.Double },
            Commitment = "abc",
            Trick = new RungTrick { LeaderSeat = caller, Plays = new List<RungPlay>(plays ?? new RungPlay[0]) },
            Stats = new List<RungSeatStats>(),
        };

        private static readonly string[] Five = { "AS", "KS", "9H", "4C", "JD" };

        private static readonly string[] Thirteen = { "AS", "KS", "9S", "4S", "QH", "8H", "2H", "JD", "9D", "5D", "5C", "3C", "2C" };

        private static (Fakes.Gateway Gateway, TableClient Client) NewClient()
        {
            var gateway = new Fakes.Gateway();
            return (gateway, new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1));
        }

        private static void DeliverCall(Fakes.Gateway gateway, bool mine, bool canRedeal)
        {
            gateway.Deliver(MessageTypes.TableState, 5, Table(GameId.Rung, View(RungPhase.Call, Five, mine ? 0 : 1, null, mine ? 0 : 1), 5));
            if (mine)
            {
                gateway.Deliver(MessageTypes.TurnStart, 5, new TurnStart { RoundId = "r_1", Seat = 0, Legal = new List<string>(), DeadlineTs = 20000, Phase = "call", Suits = new List<Suit> { Suit.S, Suit.H, Suit.C, Suit.D }, CanRedeal = canRedeal });
            }
        }

        private static void DeliverPlay(Fakes.Gateway gateway)
        {
            var plays = new List<RungPlay> { new RungPlay { Seat = 2, Card = "KH" }, new RungPlay { Seat = 3, Card = "6H" } };
            gateway.Deliver(MessageTypes.TableState, 5, Table(GameId.Rung, View(RungPhase.Play, Thirteen, 2, Suit.S, 0, plays, pile: 2, lastWinner: 2, us: 3, them: 2), 5,
                series: new SeriesState { RoundsPlayed = 2, Points = new List<long>(), LoserTally = new List<long>(), Teams = new RungTeams { Points = new List<long> { 2, 1 }, Courts = new List<long> { 0, 0 }, DealsWon = new List<long> { 1, 1 } } }));
            gateway.Deliver(MessageTypes.TurnStart, 5, new TurnStart { RoundId = "r_1", Seat = 0, Legal = new List<string> { "QH", "8H", "2H" }, DeadlineTs = 20000, Phase = "play" });
        }

        private static void DeliverResult(Fakes.Gateway gateway) => DeliverResult(gateway, RungOutcomeKind.Win);

        private static void DeliverResult(Fakes.Gateway gateway, RungOutcomeKind kind)
        {
            DeliverPlay(gateway);
            gateway.Deliver(MessageTypes.RungRoundEnd, 6, new RungRoundEnd
            {
                RoundId = "r_1", WinnerTeam = 0, Kind = kind, ByCaller = true, Counts = new List<long> { 7, kind == RungOutcomeKind.Win ? 4 : 0 }, DealPoints = 1,
                Points = new List<long> { 1, 0, 1, 0 }, Stats = new List<RungSeatStats>(), Trump = Suit.S,
                Fairness = new RungFairness { Commitment = "abc", Seed = "00", DeckOrder = new List<string>() },
                Series = new SeriesState { RoundsPlayed = 3, Points = new List<long>(), LoserTally = new List<long>(), Teams = new RungTeams { Points = new List<long> { 3, 1 }, Courts = new List<long> { 0, 0 }, DealsWon = new List<long> { 2, 1 } } },
            });
        }

        private static RungTableView NewTable(TableClient client, out VisualElement screen)
        {
            screen = Catalog.Table!.Instantiate();
            return new RungTableView(screen, client, new Fakes.Chat(), () => 0, () => { });
        }

        [Test]
        public void TheCallerSeesFourSuitsAndTheRedealWhenOffered()
        {
            var (gateway, client) = NewClient();
            var view = NewTable(client, out var screen);
            DeliverCall(gateway, mine: true, canRedeal: true);
            Assert.AreEqual(RungText.Choose, view.AskTitle);
            Assert.AreEqual(4, screen.Query<Button>(className: "rung-ask__suit").ToList().Count);
            Assert.IsNotNull(screen.Q<Button>("call-redeal"));
            Assert.AreEqual(RungText.YourTurn, view.StatusText);
            view.Dispose();
        }

        [Test]
        public void EveryoneElseSeesWhoIsChoosing()
        {
            var (gateway, client) = NewClient();
            var view = NewTable(client, out var screen);
            DeliverCall(gateway, mine: false, canRedeal: false);
            StringAssert.Contains("Ali", view.AskTitle);
            Assert.AreEqual(0, screen.Query<Button>(className: "rung-ask__suit").ToList().Count);
            view.Dispose();
        }

        [Test]
        public void InPlayThePanelIsGoneAndTheBarShowsTheTricksFromMySide()
        {
            var (gateway, client) = NewClient();
            var view = NewTable(client, out var screen);
            DeliverPlay(gateway);
            Assert.AreEqual("", view.AskTitle);
            StringAssert.Contains("3", view.ScoreText);
            StringAssert.StartsWith(RungText.Us, view.ScoreText);
            Assert.AreEqual(DisplayStyle.Flex, screen.Q<Label>("rung-pile").style.display.value);
            Assert.AreEqual(DisplayStyle.Flex, screen.Q<Label>("rung-trump").style.display.value);
            // Thulla's own controls are not on a Rung table.
            Assert.AreEqual(DisplayStyle.None, screen.Q<Button>("take-hand").style.display.value);
            view.Dispose();
        }

        [Test]
        public void TheDealResultOffersTheNextDealAndTheHostTheSwitch()
        {
            var (gateway, client) = NewClient();
            var view = NewTable(client, out var screen);
            DeliverResult(gateway);
            Assert.AreEqual(RungText.DealWon, view.ResultTitle);
            Assert.AreEqual(DisplayStyle.Flex, screen.Q<Button>("rung-switch").style.display.value);
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = screen.Q<Button>("rung-again");
                screen.Q<Button>("rung-again").SendEvent(click);
            }

            view.Dispose();
        }

        [Test]
        public void TheLobbyOffersTheSwitchToTheHostAndBotsOnlyInRung()
        {
            var (gateway, client) = NewClient();
            var screen = Catalog.Lobby!.Instantiate();
            var lobby = new LobbyView(screen, client, () => 0, () => { }) { RungOffered = true };
            gateway.Deliver(MessageTypes.TableState, 3, Table(GameId.Thulla, null, 3, empty: 2));
            Assert.IsTrue(lobby.ShowsSwitchGame);
            StringAssert.Contains("Rung", lobby.SwitchGameText);
            Assert.IsFalse(lobby.ShowsAddBot);
            gateway.Deliver(MessageTypes.GameChanged, 4, new GameChanged { GameId = GameId.Rung, ChangedBy = 0, Rules = new RulesSelection { GameId = GameId.Rung, Preset = Preset.Rung } });
            gateway.Deliver(MessageTypes.TableState, 4, Table(GameId.Rung, null, 4, empty: 2));
            Assert.IsTrue(lobby.ShowsAddBot);
            StringAssert.Contains("Thulla", lobby.SwitchGameText);
            lobby.Dispose();
        }

        [Test]
        public void TheRungLobbyShowsTwoPairsAndTheHostSwapsPartners()
        {
            var (gateway, client) = NewClient();
            var screen = Catalog.Lobby!.Instantiate();
            var lobby = new LobbyView(screen, client, () => 0, () => { }) { RungOffered = true };
            gateway.Deliver(MessageTypes.TableState, 3, Table(GameId.Rung, null, 3));
            Assert.AreEqual(UiText.RoomRung, screen.Q<Label>("title").text);
            Assert.AreEqual(2, screen.Query(className: "lobby__seat--us").ToList().Count, "my pair");
            Assert.AreEqual(2, screen.Query(className: "lobby__seat--them").ToList().Count, "the other pair");
            Assert.IsTrue(lobby.ShowsSwapPartners);
            lobby.SwapPartners();
            lobby.SwapPartners();
            var swaps = gateway.Sent.FindAll(m => m.Type == MessageTypes.SwapSeats);
            Assert.AreEqual(2, swaps.Count);
            Assert.AreEqual((1L, 2L), (((SwapSeats)swaps[0].Payload).A, ((SwapSeats)swaps[0].Payload).B), "the seat beside me trades with my partner");
            Assert.AreEqual((2L, 3L), (((SwapSeats)swaps[1].Payload).A, ((SwapSeats)swaps[1].Payload).B), "then the other side, so every partner comes round");
            gateway.Deliver(MessageTypes.TableState, 4, Table(GameId.Thulla, null, 4));
            Assert.AreEqual(UiText.RoomThulla, screen.Q<Label>("title").text);
            Assert.AreEqual(0, screen.Query(className: "lobby__seat--us").ToList().Count, "Thulla has no pairs");
            Assert.IsFalse(lobby.ShowsSwapPartners);
            lobby.Dispose();
        }

        [Test]
        public void WithoutTheFlagAThullaRoomShowsNoSwitch()
        {
            var (gateway, client) = NewClient();
            var lobby = new LobbyView(Catalog.Lobby!.Instantiate(), client, () => 0, () => { });
            gateway.Deliver(MessageTypes.TableState, 3, Table(GameId.Thulla, null, 3, empty: 2));
            Assert.IsFalse(lobby.ShowsSwitchGame);
            lobby.Dispose();
        }

        [Test]
        public void TheRungTableHasChatAndTheMenuLikeThulla()
        {
            var (gateway, client) = NewClient();
            var view = NewTable(client, out var screen);
            DeliverPlay(gateway);
            Assert.AreNotEqual(DisplayStyle.None, screen.Q<Button>("chat").style.display.value);
            Assert.AreNotEqual(DisplayStyle.None, screen.Q<Button>("more").style.display.value);
            // A phrase from a seat shows on that seat; mine shows by my hand.
            gateway.Deliver(MessageTypes.QuickChatSent, 6, new QuickChatSent { Seat = 0, PhraseId = new Fakes.Chat().Phrases[0].Id });
            Assert.AreEqual(DisplayStyle.Flex, screen.Q<Label>("my-say").style.display.value);
            view.Dispose();
        }

        [Test]
        public void AKaatStampsTheFeltForAMoment()
        {
            var (gateway, client) = NewClient();
            var view = NewTable(client, out _);
            DeliverPlay(gateway);
            var played = new RungCardPlayed { RoundId = "r_1", Seat = 0, Card = "9S", Cut = true };
            gateway.Deliver(MessageTypes.RungCardPlayed, 6, played);
            view.Present(new ServerMessage(MessageTypes.RungCardPlayed, 6, played));
            Assert.AreEqual(RungText.Kaat, view.StampText);
            view.Tick(RungTableView.StampSeconds + 0.1f);
            Assert.AreEqual("", view.StampText);
            view.Dispose();
        }

        [Test]
        public void TheRulesEditorBuildsARungSelectionAndTheLobbyLineNamesIt()
        {
            var (gateway, client) = NewClient();
            gateway.Deliver(MessageTypes.TableState, 3, Table(GameId.Rung, null, 3));
            RulesSelection? saved = null;
            var closed = false;
            var editor = new RungRulesEditor(Catalog.Settings!.Instantiate(), client.Model, s => saved = s, () => closed = true);
            Assert.AreEqual(Preset.Rung, editor.Build().Preset);
            Assert.IsNull(editor.Build().Overrides, "seven points is the rule set's own");
            editor.ChoosePreset(Preset.Hokm);
            editor.ChooseTarget(10);
            editor.ChooseTimer(30);
            var built = editor.Build();
            Assert.AreEqual(GameId.Rung, built.GameId);
            Assert.AreEqual(Preset.Hokm, built.Preset);
            Assert.AreEqual(10L, built.Overrides![RungRulesEditor.TargetKey]);
            Assert.AreEqual(30, built.Settings!.TurnTimerSeconds);
            gateway.Deliver(MessageTypes.RulesChanged, 4, new RulesChanged { Rules = built, ChangedBy = 0, ChangedKeys = new List<string>() });
            StringAssert.Contains("Hokm", RungRulesEditor.Summary(client.Model));
            StringAssert.Contains("10", RungRulesEditor.Summary(client.Model));
            Assert.IsNull(saved);
            Assert.IsFalse(closed);
        }

        [UnityTest, Explicit]
        public IEnumerator Render()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var folder = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_OUT") ?? "Temp";
            var shots = new (string Name, System.Action<Fakes.Gateway> Deliver)[]
            {
                ("rung-call", g => DeliverCall(g, mine: true, canRedeal: true)),
                ("rung-calling", g => DeliverCall(g, mine: false, canRedeal: false)),
                ("rung-play", DeliverPlay),
                ("rung-result", DeliverResult),
                ("rung-court", g => DeliverResult(g, RungOutcomeKind.Court)),
            };
            foreach (var shot in shots)
            {
                var (gateway, client) = NewClient();
                var task = CardRenderer.RenderAsync(settings, 1080, 2400, root =>
                {
                    // The one look the app ships (Basant), with its scene behind the screen.
                    ScreenCases.Skin(root, Hulla.Application.Settings.TableSkin.Basant);
                    var screen = Catalog.Table!.Instantiate();
                    screen.style.flexGrow = 1;
                    root.Add(screen);
                    if (screen.Q(className: "screen") is { } face)
                    {
                        Hulla.Presentation.Theme.AmbientLayer.Decorate(face, Hulla.Application.Settings.TableSkin.Basant, false);
                    }

                    new RungTableView(screen, client, new Fakes.Chat(), () => 0, () => { });
                    shot.Deliver(gateway);
                });
                var awaiter = task.GetAwaiter();
                while (!awaiter.IsCompleted) yield return null;
                File.WriteAllBytes(Path.Combine(folder, shot.Name + ".png"), awaiter.GetResult());
            }

            {
                var (gateway, client) = NewClient();
                var task = CardRenderer.RenderAsync(settings, 1080, 2400, root =>
                {
                    ScreenCases.Skin(root, Hulla.Application.Settings.TableSkin.Basant);
                    var screen = Catalog.Lobby!.Instantiate();
                    screen.style.flexGrow = 1;
                    root.Add(screen);
                    if (screen.Q(className: "screen") is { } face)
                    {
                        Hulla.Presentation.Theme.AmbientLayer.Decorate(face, Hulla.Application.Settings.TableSkin.Basant, false);
                    }

                    _ = new LobbyView(screen, client, () => 0, () => { }, _ => true, null, () => RungText.Summary(Preset.Rung), () => { }) { RungOffered = true };
                    gateway.Deliver(MessageTypes.TableState, 3, Table(GameId.Rung, null, 3, bots: 1, empty: 1));
                });
                var awaiter = task.GetAwaiter();
                while (!awaiter.IsCompleted) yield return null;
                File.WriteAllBytes(Path.Combine(folder, "rung-lobby.png"), awaiter.GetResult());
            }

            Assert.Pass(folder);
        }
    }
}
#endif
