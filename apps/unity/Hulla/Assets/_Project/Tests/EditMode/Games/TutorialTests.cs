using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Application.Tutorial;
using NUnit.Framework;

namespace Hulla.Games.Thulla.Tests
{
    public sealed class TutorialTests
    {
        private ScriptedTableGateway _gateway = null!;
        private TableClient _client = null!;
        private readonly List<(string Id, string Outcome)> _finished = new List<(string Id, string Outcome)>();
        private readonly List<string> _coach = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _finished.Clear();
            _coach.Clear();
            _gateway = new ScriptedTableGateway(ThullaTutorial.Steps(), () => 1_000_000);
            _client = new TableClient(_gateway, new TableTicket("tutorial", "", ""), "me", 1);
            _gateway.StepFinished += (id, outcome, _) => _finished.Add((id, outcome));
            _gateway.CoachSaid += _coach.Add;
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        /// <summary>Runs the current step to its end, playing the first lit card on each turn.</summary>
        private void PlayStep()
        {
            for (var guard = 0; guard < 200 && !_gateway.StepDone; guard++)
            {
                _gateway.Tick(ScriptedTableGateway.BeatSeconds);
                if (_gateway.WaitingForMe)
                {
                    foreach (var card in _client.Model.Hand)
                    {
                        if (_client.Model.IsLegal(card))
                        {
                            Assert.IsTrue(_client.PlayCard(card));
                            break;
                        }
                    }
                }
            }

            Assert.IsTrue(_gateway.StepDone, "the step reached its end");
        }

        [Test]
        public void AllFiveStepsRunThroughTheTableClient()
        {
            _gateway.BeginNextStep();
            Assert.IsTrue(_client.Model.IsLegal("AS") == false, "nothing is lit before the turn");
            PlayStep();
            Assert.AreEqual(0, _client.Model.TurnSeat, "R12: the Ace was highest, so you lead next");
            Assert.AreEqual(4, _client.Model.DiscardCount, "R14: the opening trick goes to the discard");

            _gateway.BeginNextStep();
            PlayStep();
            Assert.AreEqual(1, _client.Model.LastPicker, "R21: the highest heart picks up");
            Assert.AreEqual(7, _client.Model.HandCount(1), "Sara took four cards into her three");

            _gateway.BeginNextStep();
            PlayStep();
            Assert.AreEqual(1, _client.Model.LastPicker, "your thulla made Sara pick up");
            Assert.AreEqual(2, _client.Model.Hand.Count);

            _gateway.BeginNextStep();
            PlayStep();
            Assert.IsTrue(_client.Model.IsEscaped(0), "R25: out of cards and not leading means escaped");
            Assert.AreEqual(0, _client.Model.Hand.Count);

            _gateway.BeginNextStep();
            PlayStep();
            CollectionAssert.IsEmpty(_client.Model.Hand, "R38: Sara drew your last card");
            Assert.AreEqual(1, _client.Model.HandCount(1));
            Assert.IsTrue(_client.Model.IsEscaped(0));

            Assert.IsFalse(_gateway.HasNextStep);
            CollectionAssert.AreEqual(
                new[] { ("opening", "clean"), ("thulla", "clean"), ("give-thulla", "clean"), ("escape", "clean"), ("endgame", "clean") },
                _finished);
        }

        [Test]
        public void OneWrongTapGetsAHintAndASecondLetsTheCoachPlay()
        {
            _gateway.BeginNextStep();
            for (var i = 0; i < 10 && !_gateway.WaitingForMe; i++)
            {
                _gateway.Tick(ScriptedTableGateway.BeatSeconds);
            }

            Assert.IsTrue(_gateway.WaitingForMe);
            _gateway.WrongTap();
            Assert.AreEqual(TutorialText.Step1Hint, _coach[_coach.Count - 1], "the first wrong tap is a hint");
            Assert.IsTrue(_gateway.WaitingForMe, "and the player still has the turn");
            _gateway.WrongTap();
            Assert.IsFalse(_gateway.WaitingForMe, "the second one and the coach plays the card");
            Assert.AreEqual(1, _client.Model.Trick.Count);
            Assert.AreEqual("AS", _client.Model.Trick[0].Card);
            PlayStep();
            Assert.AreEqual(("opening", "coached"), _finished[0]);
        }

        [Test]
        public void AHintedStepIsRecordedAsHinted()
        {
            _gateway.BeginNextStep();
            for (var i = 0; i < 10 && !_gateway.WaitingForMe; i++)
            {
                _gateway.Tick(ScriptedTableGateway.BeatSeconds);
            }

            _gateway.WrongTap();
            PlayStep();
            Assert.AreEqual(("opening", "hinted"), _finished[0]);
        }
    }
}
