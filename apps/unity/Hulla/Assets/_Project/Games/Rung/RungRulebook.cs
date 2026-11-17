using System.Collections.Generic;
using Hulla.Application.Table;
using Hulla.Presentation.Screens.Rulebook;
using Hulla.Presentation.Text;

namespace Hulla.Games.Rung
{
    /// <summary>
    /// The Rung rulebook (Rung design 2 and 3) in short plain sections: the goal and the pairs (RG1,
    /// RG24), the deal and the call (RG5 to RG9), following suit and the kaat (RG10 to RG14), the pile
    /// (RG15 to RG20), courts (RG26, RG27, RG31), scoring a bazi (RG32 to RG36), the three rule sets and
    /// what happens on a timeout (RG42, RG43). At a table it closes with that room's rule set.
    /// </summary>
    public static class RungRulebook
    {
        public static IReadOnlyList<RulebookSection> Build(TableModel? model)
        {
            var sections = new List<RulebookSection>
            {
                Section("goal", 3),
                Section("deal", 4),
                Section("play", 4),
                Section("pile", 5),
                Section("courts", 4),
                Section("scoring", 4),
                Section("sets", 3),
                Section("away", 2),
            };
            if (model != null && model.IsRung)
            {
                sections.Add(new RulebookSection("table", Strings.Text("rulebook.this_table"), new[] { RungText.Summary(RungRulesEditor.PresetOf(model.Selection)), RungRulesEditor.Summary(model) }));
            }

            return sections;
        }

        /// <summary>A section's title and its numbered lines, by key: rulebook.rung.goal, rulebook.rung.goal1…</summary>
        private static RulebookSection Section(string id, int lines)
        {
            var text = new string[lines];
            for (var i = 0; i < lines; i++)
            {
                text[i] = Strings.Text("rulebook.rung." + id + (i + 1));
            }

            return new RulebookSection(id, Strings.Text("rulebook.rung." + id), text);
        }
    }
}
