using System.Collections.Generic;
using Hulla.Application.Services;

namespace Hulla.Presentation.TableKit
{
    /// <summary>
    /// The contextual quick-chat strip above the hand (GDD 14.3, design 7.6): a few phrases that fit what
    /// just happened, padded from the `any` phrases. The pick rotates with the seed (the trick number)
    /// so the same context shows different phrases over a round. Pure, so it is tested without a view.
    /// </summary>
    public static class QuickStrip
    {
        public const int Slots = 3;
        public const string Any = "any";

        public static IReadOnlyList<ChatPhrase> Pick(IReadOnlyList<ChatPhrase> phrases, string context, int seed, int slots = Slots)
        {
            var fitting = new List<ChatPhrase>();
            var filler = new List<ChatPhrase>();
            foreach (var phrase in phrases)
            {
                if (phrase.Context == context && context != Any)
                {
                    fitting.Add(phrase);
                }
                else if (phrase.Context == Any)
                {
                    filler.Add(phrase);
                }
            }

            var picked = new List<ChatPhrase>(slots);
            Take(fitting, seed, slots, picked);
            Take(filler, seed, slots, picked);
            return picked;
        }

        private static void Take(List<ChatPhrase> from, int seed, int slots, List<ChatPhrase> into)
        {
            if (from.Count == 0)
            {
                return;
            }

            var start = ((seed % from.Count) + from.Count) % from.Count;
            for (var i = 0; i < from.Count && into.Count < slots; i++)
            {
                into.Add(from[(start + i) % from.Count]);
            }
        }
    }
}
