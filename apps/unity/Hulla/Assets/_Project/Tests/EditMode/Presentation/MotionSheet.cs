using System.Collections.Generic;
using Hulla.Presentation.Theme;

namespace Hulla.Presentation.Tests
{
    /// <summary>
    /// The motion sheet (GDD 15.7, #81, docs/design/motion.md): every transition in the stylesheets
    /// with its duration, curve and what it does under reduce motion. MotionSheetTests fails when a
    /// stylesheet animates something this list does not name, or names a different duration.
    /// </summary>
    internal static class MotionSheet
    {
        /// <summary>The root class for the reduce-motion setting.</summary>
        public const string ReducedClass = ThemeClasses.ReducedMotion;

        public enum Reduced
        {
            /// <summary>A rule under <see cref="ReducedClass"/> in the stylesheet takes the movement out.</summary>
            Stylesheet,

            /// <summary>The code picks another class (a fade) when the setting is on.</summary>
            Code,

            /// <summary>Kept as it is: an opacity change, or a press the player's own finger makes.</summary>
            Kept,
        }

        public sealed class Entry
        {
            public Entry(string selector, string durations, string curve, Reduced reduced, string what)
            {
                Selector = selector;
                Durations = durations;
                Curve = curve;
                Reduce = reduced;
                What = what;
            }

            /// <summary>The selector as the stylesheet writes it.</summary>
            public string Selector { get; }

            /// <summary>The transition-duration value as the stylesheet writes it.</summary>
            public string Durations { get; }

            public string Curve { get; }

            public Reduced Reduce { get; }

            public string What { get; }
        }

        public static readonly IReadOnlyList<Entry> Entries = new[]
        {
            new Entry(".screen-anim", "240ms", "ease-out-cubic", Reduced.Code, "screen enters, rising 24 px; reduced: a 120 ms fade (ShellView)"),
            new Entry(".screen--enter-fade", "120ms", "ease-out-cubic", Reduced.Kept, "the reduced screen entry: opacity only"),
            new Entry(".button", "80ms", "ease", Reduced.Kept, "press dip to 0.97"),
            new Entry(".hand .card", "120ms", "ease", Reduced.Stylesheet, "tap to raise a card; reduced: it snaps up"),
            new Entry(".pile__slot--fly", "340ms", "ease-out-cubic", Reduced.Code, "a played card flies to the trick; reduced: fades in (PileView)"),
            new Entry(".pile__slot--fade", "200ms", "ease", Reduced.Kept, "the reduced card play: opacity only"),
            new Entry(".pile__slot--pickup", "460ms, 460ms, 460ms, 100ms", "ease-in-out-cubic", Reduced.Code, "a trick flies to the picker or the discard, 70 ms apart; reduced: fades (PileView)"),
            new Entry(".pile__slot--gone", "300ms", "ease", Reduced.Kept, "the reduced pick-up: opacity only"),
            new Entry(".pile__slot--slot-in", "460ms, 460ms, 100ms", "ease-in-out-cubic", Reduced.Code, "my picked-up cards glide into the hand; reduced: they appear in place"),
            new Entry(".discard__count", "120ms", "ease", Reduced.Stylesheet, "the discard count bumps to 1.3 as a card lands; reduced: no bump"),
            new Entry(".seat__avatar", "160ms", "ease", Reduced.Stylesheet, "the seat whose turn it is grows to 1.08 under its gold ring; reduced: it snaps"),
            new Entry(".seat__avatar", "220ms", "ease-out-back", Reduced.Stylesheet, "a lobby seat's face pops in from 0.6; reduced: it is there at full size"),
            new Entry(".skin--basant .home .daily-pop", "1.3s, 1.3s", "ease-out, ease-in", Reduced.Stylesheet, "the daily coins rise 120 px and fade; reduced: fade in place"),
        };
    }
}
