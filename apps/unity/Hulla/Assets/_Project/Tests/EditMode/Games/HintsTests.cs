using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Settings;
using Hulla.Application.Table;
using Hulla.Protocol;
using NUnit.Framework;

namespace Hulla.Games.Thulla.Tests
{
    public sealed class HintsTests
    {
        private sealed class MemoryStore : ISettingsStore
        {
            private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

            public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var v) ? v : fallback;

            public void SetInt(string key, int value) => _values[key] = value;

            public void Save()
            {
            }
        }

        private static TableModel MyTurn(params string[] legal)
        {
            var model = new TableModel("me");
            var seats = new List<Seat>();
            var views = new List<SeatView>();
            for (var i = 0; i < 3; i++)
            {
                seats.Add(new Seat { SeatSeat = i, PlayerId = i == 0 ? "me" : "u" + i, Connected = true });
                views.Add(new SeatView { HandCount = 2, Hand = i == 0 ? new List<string> { "2H", "KS" } : null });
            }

            model.Apply(new ServerMessage(MessageTypes.TableState, 1, new TableState { Code = "ABC123", Status = TableStatus.InRound, LastSeq = 1, Seats = seats, Round = new View { Phase = Phase.Play, SeatCount = 3, Seats = views, EscapeOrder = new List<long>(), Trick = new Trick { Plays = new List<Play> { new Play { Seat = 2, Card = "9H" } } }, TurnSeat = 0 } }));
            model.Apply(new ServerMessage(MessageTypes.TurnStart, 1, new TurnStart { RoundId = "r", Seat = 0, Legal = new List<string>(legal), DeadlineTs = 1 }));
            return model;
        }

        [Test]
        public void EachHintShowsOnceEvenAcrossTables()
        {
            var store = new MemoryStore();
            var hints = new ThullaHints(store);
            Assert.AreEqual(TutorialText.HintLegal, hints.Due(MyTurn("2H"), false));
            Assert.AreEqual(TutorialText.HintLeader, hints.Due(MyTurn("2H"), false), "the next due hint, not the same one");
            Assert.AreEqual(TutorialText.HintQuickChat, hints.Due(MyTurn("2H"), true));
            Assert.IsNull(hints.Due(MyTurn("2H"), true), "nothing left to say");
            Assert.IsNull(new ThullaHints(store).Due(MyTurn("2H"), true), "saved, so a new table doesn't repeat them");
        }

        [Test]
        public void HintsStopAfterTheFirstFiveRounds()
        {
            var hints = new ThullaHints(new MemoryStore());
            for (var i = 0; i < ThullaHints.FirstRounds; i++)
            {
                Assert.IsTrue(hints.Active);
                hints.RoundEnded();
            }

            Assert.IsFalse(hints.Active);
            Assert.IsNull(hints.Due(MyTurn("2H"), false));
        }
    }
}
