using System.Collections.Generic;
using Hulla.Application.Table;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    public sealed class TableClientTests
    {
        private FakeGateway _gateway = null!;
        private TableClient _client = null!;

        [SetUp]
        public void SetUp()
        {
            _gateway = new FakeGateway();
            _client = new TableClient(_gateway, new Services.TableTicket("m1", "ABC123", "t_ABC123"), Messages.Me, 100);
        }

        [Test]
        public void TableStateSetsTheBaselineAndMySeat()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS", "2H" }, mySeat: 2));
            Assert.AreEqual(5, _client.LastSeq);
            Assert.AreEqual(2, _client.Model.MySeat);
            CollectionAssert.AreEqual(new[] { "AS", "2H" }, _client.Model.Hand);
        }

        [Test]
        public void PublicMessagesApplyInOrderAndDuplicatesAreIgnored()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS" }));
            _gateway.Deliver(Messages.MoveApplied(6, 1, "KS"));
            _gateway.Deliver(Messages.MoveApplied(6, 1, "KS"));
            Assert.AreEqual(6, _client.LastSeq);
            Assert.AreEqual(1, _client.Model.Trick.Count);
        }

        [Test]
        public void AGapHoldsLaterMessagesAndAsksForOneSnapshot()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS" }));
            _gateway.Deliver(Messages.MoveApplied(7, 2, "QS"));
            _gateway.Deliver(Messages.MoveApplied(8, 3, "JS"));
            Assert.AreEqual(5, _client.LastSeq);
            Assert.AreEqual(0, _client.Model.Trick.Count);
            Assert.AreEqual(1, _gateway.Sent.FindAll(s => s.Type == MessageTypes.RequestSnapshot).Count);

            _gateway.Deliver(Messages.MoveApplied(6, 1, "KS"));
            Assert.AreEqual(8, _client.LastSeq);
            Assert.AreEqual(3, _client.Model.Trick.Count);
        }

        [Test]
        public void ASnapshotReplacesTheBaselineAndReleasesHeldMessages()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS" }));
            _gateway.Deliver(Messages.MoveApplied(9, 2, "QS"));
            var state = (TableState)Messages.TableState(8, new[] { "AS", "KH" }).Payload;
            _gateway.Deliver(new Services.ServerMessage(MessageTypes.Snapshot, 8, new Snapshot { State = state, Events = new List<MessageEnvelope>() }));
            Assert.AreEqual(9, _client.LastSeq);
            CollectionAssert.AreEqual(new[] { "AS", "KH" }, _client.Model.Hand);
        }

        [Test]
        public void ABaselineDropsHeldMessagesItAlreadyCovers()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS", "2S" }));
            _gateway.Deliver(Messages.TurnStart(7, 0, "AS"));
            _gateway.Deliver(Messages.TableState(9, new[] { "2S" }));
            _gateway.Deliver(Messages.MoveApplied(10, 1, "KS"));
            Assert.IsFalse(_client.Model.IsLegal("AS"));
            Assert.IsFalse(_client.Model.MyTurn);
        }

        [Test]
        public void ASeatMessageWaitsUntilThePublicStreamReachesItsSeq()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS", "2S" }));
            _gateway.Deliver(Messages.TurnStart(6, 0, "AS"));
            Assert.IsFalse(_client.Model.MyTurn);
            _gateway.Deliver(Messages.MoveApplied(6, 3, "KS"));
            Assert.IsTrue(_client.Model.MyTurn);
        }

        [Test]
        public void OnlyALegalCardIsSentWithARisingClientSeq()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS", "2H" }));
            _gateway.Deliver(Messages.TurnStart(5, 0, "AS"));
            Assert.IsFalse(_client.PlayCard("2H"));
            Assert.IsTrue(_client.PlayCard("AS"));
            Assert.IsFalse(_client.PlayCard("AS"), "a pending card blocks a second play");
            var sent = (PlayCard)_gateway.Sent.Find(s => s.Type == MessageTypes.PlayCard).Payload;
            Assert.AreEqual("AS", sent.Card);
            Assert.AreEqual(101, sent.ClientSeq);
            Assert.AreEqual("AS", _client.Model.PendingCard);
        }

        [Test]
        public void ARejectedMoveReturnsTheCard()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS" }));
            _gateway.Deliver(Messages.TurnStart(5, 0, "AS"));
            _client.PlayCard("AS");
            _gateway.Deliver(Messages.MoveRejected(5, 101));
            Assert.IsNull(_client.Model.PendingCard);
            Assert.IsTrue(_client.Model.IsLegal("AS"));
        }

        [Test]
        public void MyPlayLeavesTheHandAndTheTrickResolvesIntoThePicker()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS", "2H" }));
            _gateway.Deliver(Messages.MoveApplied(6, 0, "AS"));
            CollectionAssert.AreEqual(new[] { "2H" }, _client.Model.Hand);
            Assert.AreEqual(1, _client.Model.TurnSeat);

            _gateway.Deliver(Messages.MoveApplied(7, 1, "3H"));
            _gateway.Deliver(Messages.TrickResolved(8, 0, 0, 3, 12, 13, 13));
            _gateway.Deliver(Messages.Pickup(9, 0, "AS", "3H"));
            Assert.AreEqual(0, _client.Model.Trick.Count);
            Assert.AreEqual(2, _client.Model.LastTrick.Count);
            Assert.AreEqual(0, _client.Model.LastPicker);
            CollectionAssert.AreEqual(new[] { "AS", "2H", "3H" }, _client.Model.Hand);
            Assert.AreEqual(0, _client.Model.TurnSeat);
        }

        [Test]
        public void TakingAHandAsksForASnapshotBecauseTheCardsAreNotInTheEvent()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS" }));
            _gateway.Deliver(Messages.HandTaken(6, 0, 1, 4));
            Assert.AreEqual(5, _client.Model.HandCount(0));
            Assert.AreEqual(0, _client.Model.HandCount(1));
            Assert.IsTrue(_gateway.Sent.Exists(s => s.Type == MessageTypes.RequestSnapshot));
        }

        [Test]
        public void QuickChatAndEmotesReachTheChatEventOnce()
        {
            var lines = new List<ChatLine>();
            _client.Model.Chat += lines.Add;
            _gateway.Deliver(Messages.TableState(5, new[] { "AS" }));
            _gateway.Deliver(new Services.ServerMessage(MessageTypes.QuickChatSent, 6, new QuickChatSent { Seat = 2, PhraseId = "T01" }));
            _gateway.Deliver(new Services.ServerMessage(MessageTypes.QuickChatSent, 6, new QuickChatSent { Seat = 2, PhraseId = "T01" }));
            _gateway.Deliver(new Services.ServerMessage(MessageTypes.EmoteSent, 7, new EmoteSent { Seat = 1, EmoteId = "chai", TargetSeat = 0 }));
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual("T01", lines[0].PhraseId);
            Assert.AreEqual("chai", lines[1].EmoteId);
            Assert.AreEqual(0, lines[1].TargetSeat);
        }

        [Test]
        public void SendingChatUsesTheWireTypes()
        {
            _client.SendQuickChat("T02");
            _client.SendEmote("clap");
            Assert.AreEqual(MessageTypes.QuickChat, _gateway.Sent[0].Type);
            Assert.AreEqual("T02", ((QuickChat)_gateway.Sent[0].Payload).PhraseId);
            Assert.AreEqual(MessageTypes.Emote, _gateway.Sent[1].Type);
        }

        [Test]
        public void R38_ACardDrawnFromMyHandLeavesIt()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "4H", "JC" }, seats: 2));
            _gateway.Deliver(new Services.ServerMessage(MessageTypes.PrivateDraw, 5, new PrivateDraw { RoundId = "r1", Seat = 1, Card = "JC", Source = Source.Opponent, From = 0, AutoLead = true }));
            CollectionAssert.AreEqual(new[] { "4H" }, _client.Model.Hand);
            Assert.AreEqual(1, _client.Model.HandCount(0));
            Assert.AreEqual(14, _client.Model.HandCount(1));
        }

        [Test]
        public void R26_ACardIDrawFromTheDiscardJoinsMyHand()
        {
            _gateway.Deliver(Messages.TableState(5, new string[0]));
            _gateway.Deliver(new Services.ServerMessage(MessageTypes.PrivateDraw, 5, new PrivateDraw { RoundId = "r1", Seat = 0, Card = "9S", Source = Source.Discard, AutoLead = true }));
            CollectionAssert.AreEqual(new[] { "9S" }, _client.Model.Hand);
            Assert.AreEqual(1, _client.Model.HandCount(0));
        }

        [Test]
        public void R13_InTheOpeningWindowAnotherSeatsPlayDoesNotTakeMyTurn()
        {
            // I hold no spades, so every card is legal and I must pick one by hand (R13).
            _gateway.Deliver(Messages.TableState(5, new[] { "9H", "7D", "3C" }, mySeat: 1));
            _gateway.Deliver(Messages.MoveApplied(6, 0, "AS"));
            // A seat message carries the public seq it follows (6), as the server numbers them.
            _gateway.Deliver(new Services.ServerMessage(MessageTypes.TurnStart, 6, new TurnStart { RoundId = "r1", Seat = 1, Legal = new List<string> { "9H", "7D", "3C" }, DeadlineTs = 10000, CanTakeHand = false, Simultaneous = true }));
            Assert.IsTrue(_client.Model.MyTurn);
            _gateway.Deliver(Messages.MoveApplied(7, 2, "KS"));
            Assert.IsTrue(_client.Model.MyTurn, "seat 2 answering the window does not rotate the turn away");
            Assert.IsTrue(_client.Model.IsLegal("7D"), "off-suit is legal with no spades");
            _gateway.Deliver(Messages.MoveApplied(8, 1, "7D"));
            Assert.IsFalse(_client.Model.MyTurn, "my own play ends my part of the window");
            _gateway.Deliver(Messages.TrickResolved(9, null, 0, 12, 12, 12, 12));
            Assert.AreEqual(0, _client.Model.TurnSeat, "the opener leads trick two (R14)");
        }

        [Test]
        public void R20_AfterAThullaNobodyHasTheTurnUntilTheTrickResolves()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS" }));
            _gateway.Deliver(new Services.ServerMessage(MessageTypes.MoveApplied, 6, new MoveApplied { RoundId = "r1", Seat = 2, Move = new Move { Type = TypeEnum.PlayCard, Card = "7C" }, TrickIndex = 0, Thulla = true }));
            Assert.IsNull(_client.Model.TurnSeat);
            _gateway.Deliver(Messages.TrickResolved(7, 1, 1, 1, 13, 12, 13));
            Assert.AreEqual(1, _client.Model.TurnSeat);
        }

        [Test]
        public void AppliedMessagesJoinTheEventQueue()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS" }));
            _gateway.Deliver(Messages.MoveApplied(6, 1, "KS"));
            Assert.AreEqual(2, _client.Events.Count);
        }
    }
}
