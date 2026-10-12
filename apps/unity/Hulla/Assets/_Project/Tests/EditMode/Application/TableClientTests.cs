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
        public void AppliedMessagesJoinTheEventQueue()
        {
            _gateway.Deliver(Messages.TableState(5, new[] { "AS" }));
            _gateway.Deliver(Messages.MoveApplied(6, 1, "KS"));
            Assert.AreEqual(2, _client.Events.Count);
        }
    }
}
