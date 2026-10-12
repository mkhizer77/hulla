using System.Collections.Generic;

namespace Hulla.Application.Tutorial
{
    /// <summary>One moment in a tutorial step: a coach line, a scripted play, the player's turn, or the table resolving.</summary>
    public sealed class ScriptBeat
    {
        public enum BeatKind
        {
            Coach,
            Play,
            MyTurn,
            Resolve,
            Escape,
            Draw,
        }

        private ScriptBeat(BeatKind kind)
        {
            Kind = kind;
        }

        public BeatKind Kind { get; }

        public long Seat { get; private set; }

        public string Card { get; private set; } = "";

        public bool Thulla { get; private set; }

        public string Text { get; private set; } = "";

        /// <summary>For the player's turn: the cards the script allows, or null to follow <see cref="Suit"/>.</summary>
        public IReadOnlyList<string>? Legal { get; private set; }

        /// <summary>For the player's turn: the suit to follow; any card goes when the hand has none.</summary>
        public char? Suit { get; private set; }

        public long? Picker { get; private set; }

        public long NextLeader { get; private set; }

        public long From { get; private set; }

        public static ScriptBeat Coach(string text) => new ScriptBeat(BeatKind.Coach) { Text = text };

        public static ScriptBeat Play(long seat, string card, bool thulla = false) =>
            new ScriptBeat(BeatKind.Play) { Seat = seat, Card = card, Thulla = thulla };

        /// <param name="hint">The corrective hint after one wrong tap.</param>
        public static ScriptBeat Mine(string hint, char? suit = null, IReadOnlyList<string>? legal = null, bool thulla = false) =>
            new ScriptBeat(BeatKind.MyTurn) { Text = hint, Suit = suit, Legal = legal, Thulla = thulla };

        public static ScriptBeat Resolve(long? picker, long nextLeader) =>
            new ScriptBeat(BeatKind.Resolve) { Picker = picker, NextLeader = nextLeader };

        public static ScriptBeat Escape(long seat) => new ScriptBeat(BeatKind.Escape) { Seat = seat };

        public static ScriptBeat Draw(long seat, string card, long from) =>
            new ScriptBeat(BeatKind.Draw) { Seat = seat, Card = card, From = from };
    }
}
