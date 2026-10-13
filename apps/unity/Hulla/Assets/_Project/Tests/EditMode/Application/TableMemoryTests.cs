using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Application.Tests
{
    /// <summary>The card-memory assist (GDD 14.7): certain voids and the picked-up tray, from public events only.</summary>
    public sealed class TableMemoryTests
    {
        private static TableModel InRound()
        {
            var model = new TableModel(Messages.Me);
            model.Apply(Messages.TableState(1, new List<string> { "AS", "9H", "7D" }, mySeat: 0, seats: 4));
            return model;
        }

        private static void Trick(TableModel model, long seq, params (long Seat, string Card)[] plays)
        {
            foreach (var (seat, card) in plays)
            {
                model.Apply(Messages.MoveApplied(seq++, seat, card));
            }
        }

        [Test]
        public void ASeatThatPlaysOffSuitIsVoidInTheLedSuit()
        {
            var model = InRound();
            Trick(model, 2, (0, "AS"), (1, "KS"), (2, "7H"));
            Assert.That(model.KnownVoids(2), Is.EquivalentTo(new[] { 'S' }));
            Assert.IsEmpty(model.KnownVoids(1), "following suit tells nothing");
            Assert.IsEmpty(model.KnownVoids(0), "the leader tells nothing");
        }

        [Test]
        public void PickingUpCardsOfASuitClearsThatVoidAndFillsTheTray()
        {
            var model = InRound();
            Trick(model, 2, (0, "AS"), (1, "KS"), (2, "7H"));
            var resolved = new TrickResolved
            {
                RoundId = "r1",
                Cards = new List<string> { "AS", "KS", "7H" },
                Escapes = new List<Escape>(),
                HandCounts = new List<long> { 12, 12, 15, 13 },
                Picker = 2,
                NextLeader = 2,
                LedSuit = Suit.S,
                ThullaBy = 2,
            };
            model.Apply(new ServerMessage(MessageTypes.TrickResolved, 5, resolved));
            Assert.IsEmpty(model.KnownVoids(2), "the picker holds spades again");
            Assert.AreEqual(new[] { "AS", "KS", "7H" }, model.PickedUp(2));
            Assert.IsEmpty(model.PickedUp(1));
        }

        [Test]
        public void ADiscardedTrickKeepsTheVoidsAndANewRoundForgetsThem()
        {
            var model = InRound();
            Trick(model, 2, (0, "AS"), (1, "KS"), (2, "7H"));
            model.Apply(Messages.TrickResolved(5, null, 1, 12, 12, 12, 13));
            Assert.That(model.KnownVoids(2), Is.EquivalentTo(new[] { 'S' }));
            model.Apply(new ServerMessage(MessageTypes.RoundStart, 6, new RoundStart { RoundId = "r2", HandCounts = new List<long> { 13, 13, 13, 13 }, LeaderSeat = 1, Commitment = "c" }));
            Assert.IsEmpty(model.KnownVoids(2));
            Assert.IsEmpty(model.PickedUp(2));
        }

        [Test]
        public void TheAssistIsOnUnlessTheHostTurnedItOff()
        {
            var model = InRound();
            Assert.IsTrue(model.CardMemoryAssist, "no settings means on, as in the private preset (GDD 6.13)");
            model.Apply(new ServerMessage(MessageTypes.RulesChanged, 7, new RulesChanged
            {
                ChangedBy = 0,
                Rules = new RulesSelection { GameId = GameId.Thulla, Settings = new Hulla.Protocol.Settings { CardMemoryAssist = false } },
            }));
            Assert.IsFalse(model.CardMemoryAssist);
        }
    }
}
