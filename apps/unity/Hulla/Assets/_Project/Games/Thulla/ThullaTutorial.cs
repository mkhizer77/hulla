using System.Collections.Generic;
using Hulla.Application.Tutorial;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The five scripted mini-hands of GDD 17.3, played at a four-seat table: you (seat 0), Sara, Bilal
    /// and Noor, in play order. Every deal, play and pick-up follows the rules (R12 to R25, R38).
    /// </summary>
    public static class ThullaTutorial
    {
        private static readonly string[] Table = { "You", "Sara", "Bilal", "Noor" };

        public static IReadOnlyList<TutorialStep> Steps() => new[]
        {
            new TutorialStep(
                "opening",
                Table,
                new long[] { 5, 5, 5, 5 },
                new[] { "AS", "7S", "4S", "KH", "9D" },
                0,
                new[]
                {
                    ScriptBeat.Coach(TutorialText.Step1),
                    ScriptBeat.Mine(TutorialText.Step1Hint, legal: new[] { "AS" }),
                    ScriptBeat.Play(1, "9S"),
                    ScriptBeat.Play(2, "QS"),
                    ScriptBeat.Play(3, "3S"),
                    ScriptBeat.Resolve(null, 0),
                },
                TutorialText.Step1Done),
            new TutorialStep(
                "thulla",
                Table,
                new long[] { 4, 4, 4, 4 },
                new[] { "2H", "8H", "3C", "6S" },
                3,
                new[]
                {
                    ScriptBeat.Coach(TutorialText.Step2Lead),
                    ScriptBeat.Play(3, "5H"),
                    ScriptBeat.Mine(TutorialText.Step2Hint, suit: 'H'),
                    ScriptBeat.Play(1, "KH"),
                    ScriptBeat.Play(2, "7C", thulla: true),
                    ScriptBeat.Coach(TutorialText.Step2),
                    ScriptBeat.Resolve(1, 1),
                },
                TutorialText.Step2Done),
            new TutorialStep(
                "give-thulla",
                Table,
                new long[] { 3, 4, 4, 4 },
                new[] { "KS", "2C", "6H" },
                1,
                new[]
                {
                    ScriptBeat.Play(1, "KD"),
                    ScriptBeat.Play(2, "6D"),
                    ScriptBeat.Play(3, "8D"),
                    ScriptBeat.Coach(TutorialText.Step3),
                    ScriptBeat.Mine(TutorialText.Step3Hint, suit: 'D', thulla: true),
                    ScriptBeat.Resolve(1, 1),
                },
                TutorialText.Step3Done),
            new TutorialStep(
                "escape",
                Table,
                new long[] { 2, 4, 3, 3 },
                new[] { "3C", "5C" },
                2,
                new[]
                {
                    ScriptBeat.Coach(TutorialText.Step4),
                    ScriptBeat.Play(2, "JC"),
                    ScriptBeat.Play(3, "TC"),
                    ScriptBeat.Mine(TutorialText.Step4Hint, suit: 'C'),
                    ScriptBeat.Play(1, "QC"),
                    ScriptBeat.Resolve(null, 1),
                    ScriptBeat.Play(1, "8C"),
                    ScriptBeat.Play(2, "9C"),
                    ScriptBeat.Play(3, "4C"),
                    ScriptBeat.Mine(TutorialText.Step4Hint, suit: 'C'),
                    ScriptBeat.Resolve(null, 2),
                    ScriptBeat.Escape(0),
                },
                TutorialText.Step4Done),
            new TutorialStep(
                "endgame",
                Table,
                new long[] { 2, 1, 0, 0 },
                new[] { "4H", "JC" },
                1,
                new[]
                {
                    ScriptBeat.Coach(TutorialText.Step5Lead),
                    ScriptBeat.Play(1, "9H"),
                    ScriptBeat.Mine(TutorialText.Step5Hint, suit: 'H'),
                    ScriptBeat.Resolve(null, 1),
                    ScriptBeat.Coach(TutorialText.Step5),
                    ScriptBeat.Draw(1, "JC", 0),
                    ScriptBeat.Escape(0),
                },
                TutorialText.Step5Done,
                new long[] { 2, 3 }),
        };
    }
}
