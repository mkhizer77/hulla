using System;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using UnityEngine.UIElements;
using TableSettings = Hulla.Protocol.Settings;

namespace Hulla.Games.Thulla
{
    /// <summary>
    /// The host's rules editor in a private lobby (GDD 6.13, 14.9): a preset, the turn timer, the
    /// loser's title and what happens when you win a trick with your last card (R26, R28). Choosing a
    /// preset resets the others to its values; Save sends set_rules with the preset's table settings
    /// made explicit and the empty-hand rule as an override only when it differs from the preset's.
    /// </summary>
    public sealed class ThullaRulesEditor
    {
        private static readonly Preset[] Presets = { Preset.Standard, Preset.Family, Preset.Hardcore, Preset.ClassicPagat };
        private static readonly long?[] Timers = { 10, 15, 20, 30, 60, null };
        private static readonly LoserTitle[] Titles = { LoserTitle.Bhabhi, LoserTitle.Donkey, LoserTitle.Thullad };
        private static readonly EmptyHandLeader[] EmptyHands = { EmptyHandLeader.DrawDiscard, EmptyHandLeader.DrawNextPlayer, EmptyHandLeader.InstantExit };

        private readonly IPresetCatalog _presets;
        private readonly VisualElement _list;
        private Preset _preset;
        private long? _timer;
        private LoserTitle _title;
        private EmptyHandLeader _emptyHand;

        public ThullaRulesEditor(VisualElement root, TableModel model, IPresetCatalog presets, Action<RulesSelection> save, Action close)
        {
            _presets = presets;
            var selection = model.Selection;
            _preset = selection?.Preset ?? Preset.Standard;
            var found = presets.Find(ThullaRulebook.PresetId(_preset));
            var preset = found?.Settings;
            _timer = selection?.Settings != null && selection.Settings.TurnTimerSeconds != null ? selection.Settings.TurnTimerSeconds : preset?.TurnTimerSeconds ?? 20;
            _title = selection?.Settings?.LoserTitle ?? preset?.LoserTitle ?? LoserTitle.Bhabhi;
            _emptyHand = ThullaRuleOverrides.EmptyHandLeaderOf(selection?.Overrides) ?? PresetEmptyHand(found?.Rules);
            root.Q<Label>("title").text = UiText.EditRules;
            var saveButton = root.Q<Button>("close");
            saveButton.text = UiText.Save;
            saveButton.clicked += () =>
            {
                save(Build());
                close();
            };
            _list = root.Q<VisualElement>("list");
            Draw();
        }

        /// <summary>One line for the lobby: "Family · 30 s turns · loser: Donkey", plus the last-card rule when it is not the preset's.</summary>
        public static string Summary(TableModel model, IPresetCatalog presets)
        {
            var selection = model.Selection;
            var preset = selection?.Preset ?? Preset.Standard;
            var found = presets.Find(ThullaRulebook.PresetId(preset));
            var fromPreset = found?.Settings;
            var timer = selection?.Settings?.TurnTimerSeconds ?? fromPreset?.TurnTimerSeconds ?? 20;
            var title = selection?.Settings?.LoserTitle ?? fromPreset?.LoserTitle ?? LoserTitle.Bhabhi;
            var line = string.Format(UiText.RulesSummary, PresetName(preset), timer > 0 ? string.Format(UiText.TurnSeconds, timer) : UiText.NoTimer, TitleName(title));
            var emptyHand = ThullaRuleOverrides.EmptyHandLeaderOf(selection?.Overrides);
            return emptyHand == null || emptyHand == PresetEmptyHand(found?.Rules)
                ? line
                : string.Format(UiText.RulesSummaryLastCard, line, EmptyHandName(emptyHand.Value));
        }

        internal RulesSelection Build()
        {
            var found = _presets.Find(ThullaRulebook.PresetId(_preset));
            var preset = found?.Settings ?? new TableSettings();
            return new RulesSelection
            {
                GameId = GameId.Thulla,
                Preset = _preset,
                Overrides = ThullaRuleOverrides.ForEmptyHandLeader(_emptyHand, PresetEmptyHand(found?.Rules)),
                Settings = new TableSettings
                {
                    TurnTimerSeconds = _timer,
                    LoserTitle = _title,
                    CardMemoryAssist = preset.CardMemoryAssist,
                    ShowCardCounts = preset.ShowCardCounts,
                },
            };
        }

        /// <summary>A preset resets the timer, the title and the last-card rule to its own values.</summary>
        internal void ChoosePreset(Preset preset)
        {
            _preset = preset;
            var found = _presets.Find(ThullaRulebook.PresetId(_preset));
            var settings = found?.Settings;
            _timer = settings?.TurnTimerSeconds ?? 20;
            _title = settings?.LoserTitle ?? LoserTitle.Bhabhi;
            _emptyHand = PresetEmptyHand(found?.Rules);
            Draw();
        }

        internal void ChooseTimer(long? seconds)
        {
            _timer = seconds;
            Draw();
        }

        internal void ChooseTitle(LoserTitle title)
        {
            _title = title;
            Draw();
        }

        internal void ChooseEmptyHand(EmptyHandLeader rule)
        {
            _emptyHand = rule;
            Draw();
        }

        private void Draw()
        {
            _list.Clear();
            Choice(UiText.PresetLabel, Array.ConvertAll(Presets, PresetName), Array.IndexOf(Presets, _preset), i => ChoosePreset(Presets[i]));
            Choice(UiText.TurnTimerLabel, Array.ConvertAll(Timers, t => t == null ? UiText.NoTimer : t + " s"), Array.IndexOf(Timers, _timer), i => ChooseTimer(Timers[i]));
            Choice(UiText.LoserTitleLabel, Array.ConvertAll(Titles, TitleName), Array.IndexOf(Titles, _title), i => ChooseTitle(Titles[i]));
            Choice(UiText.EmptyHandLabel, Array.ConvertAll(EmptyHands, EmptyHandName), Array.IndexOf(EmptyHands, _emptyHand), i => ChooseEmptyHand(EmptyHands[i]));
        }

        private void Choice(string label, IReadOnlyList<string> options, int selected, Action<int> pick)
        {
            var row = new VisualElement();
            row.AddToClassList("settings__row");
            row.AddToClassList("settings__choice");
            row.Add(new Label(label));
            var chips = new VisualElement();
            chips.AddToClassList("chips");
            for (var i = 0; i < options.Count; i++)
            {
                var index = i;
                var chip = new Button(() => pick(index)) { text = options[i] };
                chip.AddToClassList("chip");
                chip.EnableInClassList("chip--on", i == selected);
                chips.Add(chip);
            }

            row.Add(chips);
            _list.Add(row);
        }

        private static EmptyHandLeader PresetEmptyHand(ThullaRules? rules) => rules?.EmptyHandLeader ?? EmptyHandLeader.DrawDiscard;

        private static string PresetName(Preset preset) => preset switch
        {
            Preset.Family => RulebookText.PresetFamily,
            Preset.Hardcore => RulebookText.PresetHardcore,
            Preset.ClassicPagat => RulebookText.PresetClassicPagat,
            _ => RulebookText.PresetStandard,
        };

        private static string TitleName(LoserTitle title) => title switch
        {
            LoserTitle.Donkey => RulebookText.Donkey,
            LoserTitle.Thullad => RulebookText.Thullad,
            _ => RulebookText.Bhabhi,
        };

        private static string EmptyHandName(EmptyHandLeader rule) => rule switch
        {
            EmptyHandLeader.DrawNextPlayer => UiText.EmptyHandDrawNext,
            EmptyHandLeader.InstantExit => UiText.EmptyHandInstantExit,
            _ => UiText.EmptyHandDrawDiscard,
        };
    }
}
