using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Protocol;

namespace Hulla.Application.Tests
{
    /// <summary>Server messages for the tests, shaped like the server sends them.</summary>
    internal static class Messages
    {
        public const string Me = "user-me";

        public static ServerMessage TableState(long lastSeq, IList<string>? myHand = null, long mySeat = 0, long seats = 4)
        {
            var seatList = new List<Seat>();
            var views = new List<SeatView>();
            for (var i = 0; i < seats; i++)
            {
                seatList.Add(new Seat { SeatSeat = i, PlayerId = i == mySeat ? Me : "user-" + i, DisplayName = "P" + i, Connected = true });
                views.Add(new SeatView { HandCount = i == mySeat && myHand != null ? myHand.Count : 13, Hand = i == mySeat && myHand != null ? new List<string>(myHand) : null });
            }

            var state = new TableState
            {
                Code = "ABC123",
                Status = myHand == null ? TableStatus.Open : TableStatus.InRound,
                HostSeat = 0,
                LastSeq = lastSeq,
                Seats = seatList,
                Round = myHand == null
                    ? null
                    : new View
                    {
                        Phase = Phase.Play,
                        SeatCount = seats,
                        Seats = views,
                        EscapeOrder = new List<long>(),
                        Trick = new Trick { Plays = new List<Play>() },
                        TurnSeat = 0,
                    },
            };
            return new ServerMessage(MessageTypes.TableState, lastSeq, state);
        }

        public static ServerMessage MoveApplied(long seq, long seat, string card) =>
            new ServerMessage(MessageTypes.MoveApplied, seq, new MoveApplied
            {
                RoundId = "r1",
                Seat = seat,
                Move = new Move { Type = TypeEnum.PlayCard, Card = card },
                TrickIndex = 0,
            });

        public static ServerMessage TurnStart(long seq, long seat, params string[] legal) =>
            new ServerMessage(MessageTypes.TurnStart, seq, new TurnStart
            {
                RoundId = "r1",
                Seat = seat,
                Legal = new List<string>(legal),
                DeadlineTs = 1000,
            });

        public static ServerMessage TrickResolved(long seq, long? picker, long nextLeader, params long[] handCounts) =>
            new ServerMessage(MessageTypes.TrickResolved, seq, new TrickResolved
            {
                RoundId = "r1",
                Cards = new List<string>(),
                Escapes = new List<Escape>(),
                HandCounts = new List<long>(handCounts),
                Picker = picker,
                NextLeader = nextLeader,
                LedSuit = Suit.S,
            });

        public static ServerMessage Pickup(long seq, long seat, params string[] cards) =>
            new ServerMessage(MessageTypes.Pickup, seq, new Pickup { RoundId = "r1", Seat = seat, Cards = new List<string>(cards) });

        public static ServerMessage HandTaken(long seq, long taker, long target, long count) =>
            new ServerMessage(MessageTypes.HandTaken, seq, new HandTaken { RoundId = "r1", Taker = taker, Target = target, CardCount = count });

        public static ServerMessage MoveRejected(long seq, long clientSeq) =>
            new ServerMessage(MessageTypes.MoveRejected, seq, new MoveRejected { RoundId = "r1", ClientSeq = clientSeq, Error = new RuleError { Code = "rule.not_legal" } });
    }
}
