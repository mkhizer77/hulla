using System.Collections.Generic;
using Hulla.Protocol;
using Newtonsoft.Json;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The host's rule overrides (GDD 6.13) as they travel in RulesSelection.overrides: a loose map
    /// validated by the server against the Thulla rules schema. Values are the wire strings.
    /// </summary>
    public static class ThullaRuleOverrides
    {
        public const string EmptyHandLeaderKey = "emptyHandLeader";

        /// <summary>The preset's toggles with the overrides on top; unreadable overrides are ignored.</summary>
        public static ThullaRules Apply(ThullaRules? preset, Dictionary<string, object>? overrides)
        {
            var merged = Copy(preset ?? new ThullaRules());
            var parsed = Parse(overrides);
            if (parsed == null)
            {
                return merged;
            }

            merged.DeckCount = parsed.DeckCount ?? merged.DeckCount;
            merged.OpeningSimultaneous = parsed.OpeningSimultaneous ?? merged.OpeningSimultaneous;
            merged.OpeningThullaApplies = parsed.OpeningThullaApplies ?? merged.OpeningThullaApplies;
            merged.ThullaEndsTrick = parsed.ThullaEndsTrick ?? merged.ThullaEndsTrick;
            merged.EmptyHandLeader = parsed.EmptyHandLeader ?? merged.EmptyHandLeader;
            merged.HandTaking = parsed.HandTaking ?? merged.HandTaking;
            merged.TwoPlayerEndgame = parsed.TwoPlayerEndgame ?? merged.TwoPlayerEndgame;
            merged.ConfirmedFastForward = parsed.ConfirmedFastForward ?? merged.ConfirmedFastForward;
            merged.BigBhabhiPenalty = parsed.BigBhabhiPenalty ?? merged.BigBhabhiPenalty;
            merged.Format = parsed.Format ?? merged.Format;
            return merged;
        }

        /// <summary>The empty-hand rule in the overrides, or null when they leave the preset's.</summary>
        public static EmptyHandLeader? EmptyHandLeaderOf(Dictionary<string, object>? overrides) =>
            Parse(overrides)?.EmptyHandLeader;

        /// <summary>Overrides carrying only the empty-hand rule, or null when it matches the preset's.</summary>
        public static Dictionary<string, object>? ForEmptyHandLeader(EmptyHandLeader chosen, EmptyHandLeader presetValue) =>
            chosen == presetValue ? null : new Dictionary<string, object> { [EmptyHandLeaderKey] = Wire(chosen) };

        public static string Wire(EmptyHandLeader value) => value switch
        {
            EmptyHandLeader.DrawNextPlayer => "drawNextPlayer",
            EmptyHandLeader.InstantExit => "instantExit",
            _ => "drawDiscard",
        };

        private static ThullaRules? Parse(Dictionary<string, object>? overrides)
        {
            if (overrides == null || overrides.Count == 0)
            {
                return null;
            }

            try
            {
                var json = JsonConvert.SerializeObject(overrides, ProtocolJson.Settings);
                return JsonConvert.DeserializeObject<ThullaRules>(json, ProtocolJson.Settings);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (System.Exception)
            {
                // The generated enum converters throw a plain Exception on a value outside the enum.
                return null;
            }
        }

        private static ThullaRules Copy(ThullaRules rules) => new ThullaRules
        {
            DeckCount = rules.DeckCount,
            OpeningSimultaneous = rules.OpeningSimultaneous,
            OpeningThullaApplies = rules.OpeningThullaApplies,
            ThullaEndsTrick = rules.ThullaEndsTrick,
            EmptyHandLeader = rules.EmptyHandLeader,
            HandTaking = rules.HandTaking,
            TwoPlayerEndgame = rules.TwoPlayerEndgame,
            ConfirmedFastForward = rules.ConfirmedFastForward,
            BigBhabhiPenalty = rules.BigBhabhiPenalty,
            Format = rules.Format,
        };
    }
}
