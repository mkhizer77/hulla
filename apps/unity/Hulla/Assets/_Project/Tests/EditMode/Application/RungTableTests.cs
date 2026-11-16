using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    /// <summary>The Rung deal as the client rebuilds it from the server's messages, and what it may send.</summary>
    public sealed class RungTableTests
    {
        private FakeGateway _gateway = null!;
        private TableClient _client = null!;

        private TableModel Model => _client.Model;

        private RungModel Rung => _client.Model.Rung;

        [SetUp]
        public void SetUp()
        {
            _gateway = new FakeGateway();
            _client = new TableClient(_gateway, new TableTicket("m1", "ABC123", "t_ABC123"), Messages.Me, 100);
        }

        private static ServerMessage Table(long seq, GameId game, RungView? view = null, long mySeat = 0, SeriesState? series = null)
        {
            var seats = new List<Seat>();
            for (var i = 0; i < 4; i++)
            {
                seats.Add(new Seat { SeatSeat = i, PlayerId = i == mySeat ? Messages.Me : "user-" + i, DisplayName = "P" + i, Connected = true });
            }

            return new ServerMessage(MessageTypes.TableState, seq, new TableState
            {
                Code = "ABC123",
                GameId = game,
                Status = view == null ? TableStatus.Open : TableStatus.InRound,
                HostSeat = 0,
                LastSeq = seq,
                Seats = seats,
                RungRound = view,
                Series = series ?? new SeriesState { Points = new List<long>(), LoserTally = new List<long>() },
                Rules = new RulesSelection { GameId = game },
            });
        }

        private static RungView View(RungPhase phase, IList<string> hand, long caller = 1, Suit? trump = null, long? turn = null) => new RungView
        {
            Phase = phase,
            DealerSeat = (caller + 3) % 4,
            CallerSeat = caller,
            Trump = trump,
            TurnSeat = turn ?? caller,
            Counts = new List<long> { 0, 0 },
            HandCounts = new List<long> { hand.Count, hand.Count, hand.Count, hand.Count },
            Hand = new List<string>(hand),
            Rules = new RungRules { Collect = Collect.Double },
            Commitment = "abc",
            Trick = new RungTrick { LeaderSeat = caller, Plays = new List<RungPlay>() },
            Stats = new List<RungSeatStats>(),
        };

        private static ServerMessage Msg(string type, long seq, object payload) => new ServerMessage(type, seq, payload);

        private static ServerMessage Turn(long seq, long seat, string phase, IList<string>? legal = null, IList<Suit>? suits = null, bool? canRedeal = null) =>
            Msg(MessageTypes.TurnStart, seq, new TurnStart
            {
                RoundId = "r_1",
                Seat = seat,
                Legal = new List<string>(legal ?? new string[0]),
                DeadlineTs = 9000,
                Phase = phase,
                Suits = suits == null ? null : new List<Suit>(suits),
                CanRedeal = canRedeal,
            });

        private static ServerMessage Card(long seq, long seat, string card) =>
            Msg(MessageTypes.RungCardPlayed, seq, new RungCardPlayed { RoundId = "r_1", Seat = seat, Card = card });

        private static ServerMessage Start(long seq, long caller) =>
            Msg(MessageTypes.RungRoundStart, seq, new RungRoundStart { RoundId = "r_1", CallerSeat = caller, DealerSeat = (caller + 3) % 4, HandCounts = new List<long> { 5, 5, 5, 5 }, Commitment = "abc" });

        /// <summary>A deal dealt with me as the caller: five cards, the round start, my turn to call.</summary>
        private void DealToMeAsCaller(bool canRedeal = false)
        {
            _gateway.Deliver(Table(5, GameId.Rung, View(RungPhase.Call, new[] { "AS", "KS", "2H", "3C", "4D" }, caller: 0)));
            _gateway.Deliver(Start(6, 0));
            _gateway.Deliver(Turn(6, 0, "call", suits: new[] { Suit.S, Suit.H, Suit.C, Suit.D }, canRedeal: canRedeal));
        }

        [Test]
        public void ARungTableStateFillsTheRungDealAndLeavesThullaEmpty()
        {
            _gateway.Deliver(Table(5, GameId.Rung, View(RungPhase.Call, new[] { "AS", "KS", "2H", "3C", "4D" })));
            Assert.IsTrue(Model.IsRung);
            Assert.IsTrue(Model.InRound);
            Assert.IsTrue(Model.ShowsTable);
            Assert.AreEqual(5, Rung.Hand.Count);
            Assert.AreEqual(0, Model.Hand.Count);
            Assert.AreEqual(RungPhase.Call, Rung.Phase);
            Assert.AreEqual(1, Model.TurnSeat);
            Assert.IsFalse(Model.MyTurn);
            Assert.IsNull(Rung.Trump);
        }

        [Test]
        public void TheCallerNamesTheRungOnlyOnItsTurnAndOnlyARealSuit()
        {
            _gateway.Deliver(Table(5, GameId.Rung, View(RungPhase.Call, new[] { "AS", "KS", "2H", "3C", "4D" }, caller: 0)));
            Assert.IsFalse(_client.CallTrump('S'), "no turn_start yet");
            _gateway.Deliver(Start(6, 0));
            _gateway.Deliver(Turn(6, 0, "call", suits: new[] { Suit.S, Suit.H, Suit.C, Suit.D }));
            Assert.IsTrue(Model.MyTurn);
            Assert.AreEqual(4, Rung.CallableSuits.Count);
            Assert.IsFalse(_client.CallTrump('X'));
            Assert.IsFalse(_client.Redeal(), "the server did not offer a redeal");
            Assert.IsFalse(_client.PlayCard("AS"), "no card is legal during the call");
            Assert.IsTrue(_client.CallTrump('H'));
            var sent = _gateway.Sent[_gateway.Sent.Count - 1];
            Assert.AreEqual(MessageTypes.CallTrump, sent.Type);
            Assert.AreEqual(Suit.H, ((CallTrump)sent.Payload).Suit);
            Assert.AreEqual("r_1", ((CallTrump)sent.Payload).RoundId);
        }

        [Test]
        public void TheCallThenTheRestOfTheDealArrivesAsMyOwnHand()
        {
            DealToMeAsCaller();
            _gateway.Deliver(Msg(MessageTypes.RungTrumpCalled, 7, new RungTrumpCalled { RoundId = "r_1", Seat = 0, Suit = Suit.S }));
            Assert.AreEqual('S', Rung.Trump);
            Assert.IsFalse(Model.MyTurn, "the call is made; the suits are gone");
            _gateway.Deliver(Msg(MessageTypes.RungHandsCompleted, 8, new RungHandsCompleted { RoundId = "r_1", HandCounts = new List<long> { 13, 13, 13, 13 } }));
            // The hand is a seat message carrying the public seq it follows.
            _gateway.Deliver(Msg(MessageTypes.RungHand, 8, new RungHand { RoundId = "r_1", Cards = new List<string> { "AS", "KS", "2H", "3C", "4D", "5D", "6D", "7D", "8D", "9D", "TD", "JD", "QD" } }));
            Assert.AreEqual(RungPhase.Play, Rung.Phase);
            Assert.AreEqual(13, Rung.Hand.Count);
            Assert.AreEqual(13, Rung.HandCount(2));
            Assert.AreEqual(0, Model.TurnSeat, "the caller leads");
        }

        [Test]
        public void ARedealIsSentOnlyWhenOfferedAndBringsFiveNewCards()
        {
            DealToMeAsCaller(canRedeal: true);
            Assert.IsTrue(_client.Redeal());
            Assert.AreEqual(MessageTypes.Redeal, _gateway.Sent[_gateway.Sent.Count - 1].Type);
            _gateway.Deliver(Msg(MessageTypes.RungRedealt, 7, new RungRedealt { RoundId = "r_1", Seat = 0, Redeals = 1, Commitment = "def", HandCounts = new List<long> { 5, 5, 5, 5 } }));
            _gateway.Deliver(Msg(MessageTypes.RungHand, 7, new RungHand { RoundId = "r_1", Cards = new List<string> { "2S", "3S", "4S", "5S", "6S" } }));
            Assert.AreEqual(1, Rung.Redeals);
            Assert.AreEqual("def", Model.Commitment);
            CollectionAssert.AreEqual(new[] { "2S", "3S", "4S", "5S", "6S" }, Rung.Hand);
            Assert.IsFalse(Model.MyTurn, "a new turn_start follows the redeal");
        }

        [Test]
        public void PlayingACardFollowsTheTrickAroundTheTableAndThePile()
        {
            _gateway.Deliver(Table(5, GameId.Rung, View(RungPhase.Play, new[] { "AS", "2H" }, caller: 0, trump: Suit.S, turn: 0)));
            _gateway.Deliver(Turn(5, 0, "play", legal: new[] { "AS", "2H" }));
            Assert.IsFalse(_client.PlayCard("KD"), "not in the legal mask");
            Assert.IsTrue(_client.PlayCard("AS"));
            Assert.AreEqual("AS", Rung.PendingCard);
            Assert.IsFalse(_client.PlayCard("2H"), "one card at a time");
            _gateway.Deliver(Card(6, 0, "AS"));
            Assert.IsNull(Rung.PendingCard);
            CollectionAssert.AreEqual(new[] { "2H" }, Rung.Hand);
            Assert.AreEqual(1, Model.TurnSeat);
            Assert.AreEqual('S', Rung.LedSuit);
            _gateway.Deliver(Card(7, 1, "2S"));
            _gateway.Deliver(Card(8, 2, "3S"));
            _gateway.Deliver(Card(9, 3, "4S"));
            Assert.IsNull(Model.TurnSeat, "the fourth card ends the trick");
            _gateway.Deliver(Msg(MessageTypes.RungTrickWon, 10, new RungTrickWon { RoundId = "r_1", Winner = 0, Pile = 1, Counts = new List<long> { 0, 0 }, Cards = new List<string>() }));
            Assert.AreEqual(0, Rung.Trick.Count);
            Assert.AreEqual(4, Rung.LastTrick.Count);
            Assert.AreEqual(1, Rung.Pile);
            Assert.AreEqual(0, Rung.RunSeat, "my next trick in a row takes the pile");
            Assert.AreEqual(0, Model.TurnSeat);
            _gateway.Deliver(Msg(MessageTypes.RungPileTaken, 11, new RungPileTaken { RoundId = "r_1", Seat = 0, Team = 0, Tricks = 2, Counts = new List<long> { 2, 0 } }));
            Assert.AreEqual(0, Rung.Pile);
            Assert.IsNull(Rung.RunSeat);
            Assert.AreEqual(2, Rung.Counts[0]);
            Assert.AreEqual(2, Rung.LastPile!.Tricks);
        }

        [Test]
        public void ARejectedCardComesBackToTheHand()
        {
            _gateway.Deliver(Table(5, GameId.Rung, View(RungPhase.Play, new[] { "AS", "2H" }, caller: 0, trump: Suit.S, turn: 0)));
            _gateway.Deliver(Turn(5, 0, "play", legal: new[] { "AS", "2H" }));
            _client.PlayCard("AS");
            _gateway.Deliver(Msg(MessageTypes.MoveRejected, 5, new MoveRejected()));
            Assert.IsNull(Rung.PendingCard);
            Assert.IsTrue(Rung.IsLegal("AS"));
        }

        [Test]
        public void TheCourtChoiceIsMineToAnswerOnce()
        {
            _gateway.Deliver(Table(5, GameId.Rung, View(RungPhase.Play, new[] { "AS" }, caller: 0, trump: Suit.S, turn: 1)));
            _gateway.Deliver(Msg(MessageTypes.RungCourtChoiceOpened, 6, new RungCourtChoiceOpened { RoundId = "r_1", Seat = 0, Team = 0 }));
            Assert.AreEqual(RungPhase.CourtChoice, Rung.Phase);
            Assert.IsFalse(_client.CourtChoice(true), "the turn_start has not arrived");
            _gateway.Deliver(Turn(6, 0, "courtChoice"));
            Assert.IsTrue(Model.MyTurn);
            Assert.IsTrue(_client.CourtChoice(true));
            Assert.IsTrue(((CourtChoice)_gateway.Sent[_gateway.Sent.Count - 1].Payload).PlayOn);
            _gateway.Deliver(Msg(MessageTypes.RungCourtChoiceMade, 7, new RungCourtChoiceMade { RoundId = "r_1", Seat = 0, PlayOn = true }));
            Assert.AreEqual(0, Rung.Chasing);
            Assert.AreEqual(RungPhase.Play, Rung.Phase);
            Assert.IsFalse(_client.CourtChoice(false));
        }

        [Test]
        public void TheEndOfADealKeepsTheResultAndTheBaziScore()
        {
            _gateway.Deliver(Table(5, GameId.Rung, View(RungPhase.Play, new[] { "AS" }, caller: 0, trump: Suit.S, turn: 1)));
            _gateway.Deliver(Msg(MessageTypes.RungRoundEnd, 6, new RungRoundEnd
            {
                RoundId = "r_1",
                WinnerTeam = 1,
                Kind = RungOutcomeKind.Win,
                Counts = new List<long> { 3, 7 },
                DealPoints = 2,
                Points = new List<long> { 0, 2, 0, 2 },
                Stats = new List<RungSeatStats>(),
                Fairness = new RungFairness { Commitment = "abc", Seed = "00", DeckOrder = new List<string>() },
                Series = new SeriesState
                {
                    RoundsPlayed = 1,
                    Points = new List<long> { 0, 2, 0, 2 },
                    LoserTally = new List<long>(),
                    Teams = new RungTeams { Points = new List<long> { 0, 2 }, Courts = new List<long> { 0, 0 }, DealsWon = new List<long> { 0, 1 } },
                },
            }));
            Assert.IsFalse(Model.InRound);
            Assert.IsTrue(Model.ShowsTable, "the result stays on the table screen");
            Assert.AreEqual(1, Rung.LastResult!.WinnerTeam);
            Assert.AreEqual(2, Rung.Teams!.Points[1]);
            Assert.AreEqual(1, Rung.DealsPlayed);
            Assert.IsNull(Model.TurnSeat);
        }

        [Test]
        public void SwitchingGameClearsTheOldGameAndTheHostMaySendIt()
        {
            _gateway.Deliver(Messages.TableState(5));
            Assert.IsFalse(Model.IsRung);
            _client.ChangeGame(GameId.Rung);
            Assert.AreEqual(GameId.Rung, ((ChangeGame)_gateway.Sent[_gateway.Sent.Count - 1].Payload).GameId);
            _gateway.Deliver(Msg(MessageTypes.GameChanged, 6, new GameChanged { GameId = GameId.Rung, ChangedBy = 0, Rules = new RulesSelection { GameId = GameId.Rung, Preset = Preset.Rung } }));
            Assert.IsTrue(Model.IsRung);
            Assert.AreEqual(Preset.Rung, Model.Selection!.Preset);
            Assert.IsFalse(Model.ShowsTable);
            Assert.AreEqual(0, Model.LastGameChange!.ChangedBy);
            // Back to Thulla: nothing of the Rung bazi is left on screen.
            _gateway.Deliver(Table(7, GameId.Thulla));
            Assert.IsFalse(Model.IsRung);
            Assert.IsNull(Rung.Teams);
        }

        [Test]
        public void AQueuedSwitchShowsUntilTheRoundEnds()
        {
            var state = (TableState)Table(5, GameId.Thulla).Payload;
            state.QueuedGame = new QueuedGame { GameId = GameId.Rung, Rules = new RulesSelection { GameId = GameId.Rung } };
            _gateway.Deliver(new ServerMessage(MessageTypes.TableState, 5, state));
            Assert.AreEqual(GameId.Rung, Model.QueuedGame!.GameId);
        }

        [Test]
        public void TheHostAddsAndRemovesBots()
        {
            _gateway.Deliver(Table(5, GameId.Rung));
            _client.AddBot();
            Assert.AreEqual(MessageTypes.AddBot, _gateway.Sent[_gateway.Sent.Count - 1].Type);
            Assert.IsNull(((AddBot)_gateway.Sent[_gateway.Sent.Count - 1].Payload).Seat);
            _client.AddBot(3);
            Assert.AreEqual(3, ((AddBot)_gateway.Sent[_gateway.Sent.Count - 1].Payload).Seat);
            _client.RemoveBot(3);
            Assert.AreEqual(3, ((RemoveBot)_gateway.Sent[_gateway.Sent.Count - 1].Payload).Seat);
        }

        [Test]
        public void TeamsAndPartnersFollowTheSeats()
        {
            Assert.AreEqual(0, RungModel.TeamOf(2));
            Assert.AreEqual(1, RungModel.TeamOf(3));
            Assert.AreEqual(3, RungModel.PartnerOf(1));
        }
    }
}
