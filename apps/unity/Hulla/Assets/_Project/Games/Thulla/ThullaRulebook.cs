using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Presentation.Screens.Rulebook;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using TableSettings = Hulla.Protocol.Settings;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The Thulla rulebook (GDD 14.11): the table's preset as sentences, one per toggle with its current
    /// value, why a card couldn't be played, the auto-play policy (R44), the glossary and three tips.
    /// During a round the toggles are the ones the engine resolved; before it, the preset's with the host's overrides.
    /// </summary>
    public static class ThullaRulebook
    {
        public const string WhyNotSection = "why-not";

        public static IReadOnlyList<RulebookSection> Build(TableModel model, IPresetCatalog presets)
        {
            var selection = model.Selection;
            var preset = selection?.Preset ?? Preset.Standard;
            var fromPreset = presets.Find(PresetId(preset));
            var rules = model.ActiveRules ?? ThullaRuleOverrides.Apply(fromPreset?.Rules, selection?.Overrides);
            var defaults = fromPreset?.Settings ?? new TableSettings();
            var chosen = selection?.Settings;
            var timer = chosen?.TurnTimerSeconds ?? defaults.TurnTimerSeconds ?? (model.IsPrivate ? 20 : model.IsRanked ? 12 : 15);
            var loser = LoserName(chosen?.LoserTitle ?? defaults.LoserTitle ?? LoserTitle.Bhabhi);

            var table = new List<string>
            {
                Strings.Format(RulebookText.Preset, PresetName(selection?.Preset)),
                rules.OpeningSimultaneous ?? true ? RulebookText.OpeningSimultaneousOn : RulebookText.OpeningSimultaneousOff,
                rules.OpeningThullaApplies ?? false ? RulebookText.OpeningThullaOn : RulebookText.OpeningThullaOff,
                rules.ThullaEndsTrick ?? true ? RulebookText.ThullaEndsTrickOn : RulebookText.ThullaEndsTrickOff,
                (rules.EmptyHandLeader ?? EmptyHandLeader.DrawDiscard) switch
                {
                    EmptyHandLeader.DrawNextPlayer => RulebookText.EmptyHandDrawNext,
                    EmptyHandLeader.InstantExit => RulebookText.EmptyHandInstantExit,
                    _ => RulebookText.EmptyHandDrawDiscard,
                },
                (rules.HandTaking ?? (model.IsPrivate ? HandTaking.LeaderOnly : HandTaking.Off)) switch
                {
                    HandTaking.Anyone => RulebookText.HandTakingAnyone,
                    HandTaking.LeaderOnly => RulebookText.HandTakingLeader,
                    _ => RulebookText.HandTakingOff,
                },
                (rules.TwoPlayerEndgame ?? TwoPlayerEndgame.DrawOpponent) switch
                {
                    TwoPlayerEndgame.DrawDiscard => RulebookText.EndgameDrawDiscard,
                    TwoPlayerEndgame.ThullaEndsRound => Strings.Format(RulebookText.EndgameThullaEndsRound, loser),
                    _ => RulebookText.EndgameDrawOpponent,
                },
                rules.ConfirmedFastForward ?? true ? RulebookText.FastForwardOn : RulebookText.FastForwardOff,
                timer > 0 ? Strings.Format(RulebookText.TurnTimer, timer) : RulebookText.NoTurnTimer,
                Strings.Format(RulebookText.LoserTitle, loser),
            };

            return new[]
            {
                new RulebookSection("table", RulebookText.ThisTable, table),
                new RulebookSection("basics", RulebookText.Basics, new[] { RulebookText.Basic1, RulebookText.Basic2, RulebookText.Basic3 }),
                new RulebookSection(WhyNotSection, RulebookText.WhyNot, new[] { RulebookText.WhyFollow, RulebookText.WhyAce, RulebookText.WhyTurn, RulebookText.WhyServer }),
                new RulebookSection("auto-play", RulebookText.AutoPlay, new[] { RulebookText.AutoFollow, RulebookText.AutoThulla, RulebookText.AutoLead, RulebookText.AutoOpening, RulebookText.AutoNever }),
                new RulebookSection("glossary", RulebookText.Glossary, new[] { RulebookText.WordThulla, RulebookText.WordBhabhi, RulebookText.WordHaath, RulebookText.WordHaathLena, RulebookText.WordSuits, RulebookText.WordCourt, RulebookText.WordEscape }),
                new RulebookSection("tips", RulebookText.Tips, new[] { RulebookText.Tip1, RulebookText.Tip2, RulebookText.Tip3 }),
            };
        }

        public static string PresetId(Preset preset) => preset switch
        {
            Preset.Family => "family",
            Preset.Hardcore => "hardcore",
            Preset.ClassicPagat => "classicPagat",
            _ => "standard",
        };

        private static string PresetName(Preset? preset) => preset switch
        {
            Preset.Family => RulebookText.PresetFamily,
            Preset.Hardcore => RulebookText.PresetHardcore,
            Preset.ClassicPagat => RulebookText.PresetClassicPagat,
            null => RulebookText.PresetStandard,
            _ => RulebookText.PresetStandard,
        };

        private static string LoserName(LoserTitle title) => title switch
        {
            LoserTitle.Donkey => RulebookText.Donkey,
            LoserTitle.Thullad => RulebookText.Thullad,
            _ => RulebookText.Bhabhi,
        };
    }
}
