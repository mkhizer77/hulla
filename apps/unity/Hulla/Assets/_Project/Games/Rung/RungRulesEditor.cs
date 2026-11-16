using System;
using System.Collections.Generic;
using Hulla.Application.Table;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using UnityEngine.UIElements;

namespace Hulla.Games.Rung
{
    /// <summary>
    /// The host's rules for a private Rung room (Rung design 3): one of the three rule sets, the turn
    /// timer and the points a bazi is played to. It only builds a selection; the server checks it.
    /// </summary>
    public sealed class RungRulesEditor
    {
        public const string TargetKey = "target";

        private static readonly Preset[] Presets = { Preset.Rung, Preset.CourtPiece, Preset.Hokm };
        private static readonly long?[] Timers = { 10, 15, 20, 30, 60, null };
        private static readonly long[] Targets = { 5, 7, 10 };

        /// <summary>Every rule set plays to seven in a private room unless the host says otherwise.</summary>
        private const long PresetTarget = 7;

        private readonly VisualElement _list;
        private Preset _preset;
        private long? _timer;
        private long _target;

        public RungRulesEditor(VisualElement root, TableModel model, Action<RulesSelection> save, Action close)
        {
            var selection = model.Selection;
            _preset = PresetOf(selection);
            _timer = selection?.Settings?.TurnTimerSeconds ?? 20;
            _target = TargetOf(selection);
            root.Q<Label>("title").text = UiText.EditRules;
            var saveButton = root.Q<Button>("close");
            saveButton.text = UiText.Save;
            saveButton.clicked += () =>
            {
                save(Build());
                close();
            };
            var cancel = root.Q<Button>("back-left");
            if (cancel != null)
            {
                cancel.text = "";
                cancel.tooltip = UiText.Cancel;
                cancel.clicked += close;
                var icon = cancel.Q(className: "kit__icon");
                icon?.RemoveFromClassList("kit__icon--back");
                icon?.AddToClassList("kit__icon--close");
            }

            _list = root.Q<VisualElement>("list");
            Draw();
        }

        /// <summary>One line for the lobby: "Rung · 20 s turns · 7 points".</summary>
        public static string Summary(TableModel model)
        {
            var selection = model.Selection;
            var timer = selection?.Settings?.TurnTimerSeconds ?? 20;
            return PresetName(PresetOf(selection))
                + " · " + Strings.Format(UiText.TurnSeconds, timer)
                + " · " + Strings.Format(RungText.PointsN, TargetOf(selection));
        }

        internal RulesSelection Build() => new RulesSelection
        {
            GameId = GameId.Rung,
            Preset = _preset,
            Overrides = _target == PresetTarget ? null : new Dictionary<string, object> { [TargetKey] = _target },
            Settings = new Settings { TurnTimerSeconds = _timer },
        };

        internal void ChoosePreset(Preset preset)
        {
            _preset = preset;
            Draw();
        }

        internal void ChooseTimer(long? seconds)
        {
            _timer = seconds;
            Draw();
        }

        internal void ChooseTarget(long target)
        {
            _target = target;
            Draw();
        }

        private static Preset PresetOf(RulesSelection? selection) =>
            selection?.Preset is Preset chosen && Array.IndexOf(Presets, chosen) >= 0 ? chosen : Preset.Rung;

        private static long TargetOf(RulesSelection? selection)
        {
            if (selection?.Overrides != null && selection.Overrides.TryGetValue(TargetKey, out var value))
            {
                try
                {
                    var target = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                    return Array.IndexOf(Targets, target) >= 0 ? target : PresetTarget;
                }
                catch (Exception error) when (error is FormatException || error is InvalidCastException || error is OverflowException)
                {
                    return PresetTarget;
                }
            }

            return PresetTarget;
        }

        private void Draw()
        {
            _list.Clear();
            Choice(RungText.RuleSet, Array.ConvertAll(Presets, PresetName), Array.IndexOf(Presets, _preset), i => ChoosePreset(Presets[i]));
            var about = new Label(Strings.Text("preset." + PresetId(_preset) + ".description"));
            about.AddToClassList("caption");
            _list.Add(about);
            Choice(UiText.TurnTimerLabel, Array.ConvertAll(Timers, t => t == null ? UiText.NoTimer : Strings.Format(UiText.TurnSeconds, t.Value)), Array.IndexOf(Timers, _timer), i => ChooseTimer(Timers[i]));
            Choice(RungText.PlayTo, Array.ConvertAll(Targets, t => Strings.Format(RungText.PointsN, t)), Array.IndexOf(Targets, _target), i => ChooseTarget(Targets[i]));
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

        internal static string PresetId(Preset preset) => preset == Preset.CourtPiece ? "courtPiece" : preset == Preset.Hokm ? "hokm" : "rung";

        private static string PresetName(Preset preset) => Strings.Text("preset." + PresetId(preset) + ".name");
    }
}
