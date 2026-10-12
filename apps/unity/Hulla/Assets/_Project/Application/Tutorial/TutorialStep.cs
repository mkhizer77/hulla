using System.Collections.Generic;

namespace Hulla.Application.Tutorial
{
    /// <summary>A scripted mini-hand (GDD 17.3): the starting table, the beats, and what the coach says at the end.</summary>
    public sealed class TutorialStep
    {
        public TutorialStep(string id, IReadOnlyList<string> names, IReadOnlyList<long> handCounts, IReadOnlyList<string> myHand, long leader, IReadOnlyList<ScriptBeat> beats, string done, IReadOnlyList<long>? escaped = null)
        {
            Id = id;
            Names = names;
            HandCounts = handCounts;
            MyHand = myHand;
            Leader = leader;
            Beats = beats;
            Done = done;
            Escaped = escaped ?? new long[0];
        }

        public string Id { get; }

        /// <summary>Seat 0 is the player.</summary>
        public IReadOnlyList<string> Names { get; }

        public IReadOnlyList<long> HandCounts { get; }

        public IReadOnlyList<string> MyHand { get; }

        public long Leader { get; }

        public IReadOnlyList<ScriptBeat> Beats { get; }

        public string Done { get; }

        /// <summary>Seats already out when the step starts (the endgame step).</summary>
        public IReadOnlyList<long> Escaped { get; }
    }
}
