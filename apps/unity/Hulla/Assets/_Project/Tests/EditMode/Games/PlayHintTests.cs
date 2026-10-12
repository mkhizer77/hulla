using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Games.Thulla.Tests
{
    public sealed class PlayHintTests
    {
        private static TableModel InRound(IList<string> hand, IList<Play> trick, long turnSeat)
        {
            var model = new TableModel("me");
            var seats = new List<Seat>();
            var views = new List<SeatView>();
            for (var i = 0; i < 3; i++)
            {
                seats.Add(new Seat { SeatSeat = i, PlayerId = i == 0 ? "me" : "u" + i, Connected = true });
                views.Add(new SeatView { HandCount = 5, Hand = i == 0 ? new List<string>(hand) : null });
            }

            model.Apply(new ServerMessage(MessageTypes.TableState, 1, new TableState
            {
                Code = "ABC123",
                Status = TableStatus.InRound,
                LastSeq = 1,
                Seats = seats,
                Round = new View { Phase = Phase.Play, SeatCount = 3, Seats = views, EscapeOrder = new List<long>(), Trick = new Trick { Plays = new List<Play>(trick) }, TurnSeat = turnSeat },
            }));
            return model;
        }

        private static void TurnStart(TableModel model, params string[] legal) =>
            model.Apply(new ServerMessage(MessageTypes.TurnStart, 1, new TurnStart { RoundId = "r", Seat = 0, Legal = new List<string>(legal), DeadlineTs = 1 }));

        [Test]
        public void R18_HoldingTheLedSuitMeansFollowIt()
        {
            var model = InRound(new[] { "2H", "KS" }, new[] { new Play { Seat = 2, Card = "9H" } }, 0);
            TurnStart(model, "2H");
            Assert.AreEqual(PlayHint.Reason.MustFollow, PlayHint.For(model, "KS"));
            Assert.AreEqual(PlayHint.Reason.None, PlayHint.For(model, "2H"));
        }

        [Test]
        public void R12_TheOpeningLeaderMustLeadTheAceOfSpades()
        {
            var model = InRound(new[] { "AS", "3D" }, new Play[0], 0);
            TurnStart(model, "AS");
            Assert.AreEqual(PlayHint.Reason.LeadAceOfSpades, PlayHint.For(model, "3D"));
        }

        [Test]
        public void OffTurnTheReasonIsToWait()
        {
            var model = InRound(new[] { "AS", "3D" }, new Play[0], 1);
            Assert.AreEqual(PlayHint.Reason.NotYourTurn, PlayHint.For(model, "3D"));
        }
    }
}
