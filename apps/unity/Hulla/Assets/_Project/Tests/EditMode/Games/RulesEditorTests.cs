using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using TableSettings = Hulla.Protocol.Settings;

namespace Hulla.Games.Thulla.Tests
{
    public sealed class RulesEditorTests
    {
        private sealed class Presets : IPresetCatalog
        {
            public (ThullaRules Rules, TableSettings Settings)? Find(string presetId) => presetId switch
            {
                "family" => (new ThullaRules(), new TableSettings { TurnTimerSeconds = 30, LoserTitle = LoserTitle.Donkey, CardMemoryAssist = true }),
                "hardcore" => (new ThullaRules { EmptyHandLeader = EmptyHandLeader.DrawNextPlayer }, new TableSettings { TurnTimerSeconds = 10 }),
                _ => (new ThullaRules(), new TableSettings()),
            };

            public Awaitable LoadAsync(CancellationToken cancellation) => throw new System.NotSupportedException();
        }

        private static VisualElement Root()
        {
            var root = new VisualElement();
            root.Add(new Label { name = "title" });
            root.Add(new Button { name = "close" });
            root.Add(new VisualElement { name = "list" });
            return root;
        }

        private static TableModel Lobby(RulesSelection? rules)
        {
            var model = new TableModel("me");
            model.Apply(new ServerMessage(MessageTypes.TableState, 1, new TableState
            {
                Code = "ABC123",
                Mode = TableMode.Private,
                Status = TableStatus.Open,
                LastSeq = 1,
                Rules = rules ?? new RulesSelection { GameId = GameId.Thulla },
                Seats = new List<Seat> { new Seat { SeatSeat = 0, PlayerId = "me", Connected = true } },
            }));
            return model;
        }

        [Test]
        public void APresetIsSentWithItsTableSettingsSpeltOut()
        {
            var editor = new ThullaRulesEditor(Root(), Lobby(null), new Presets(), _ => { }, () => { });
            editor.ChoosePreset(Preset.Family);
            var rules = editor.Build();
            Assert.AreEqual(Preset.Family, rules.Preset);
            Assert.AreEqual(30, rules.Settings!.TurnTimerSeconds, "the server reads the timer from settings, not from the preset");
            Assert.AreEqual(LoserTitle.Donkey, rules.Settings.LoserTitle);
            Assert.AreEqual(true, rules.Settings.CardMemoryAssist);
        }

        [Test]
        public void TheHostCanTweakAPresetAndTurnTheTimerOff()
        {
            var editor = new ThullaRulesEditor(Root(), Lobby(null), new Presets(), _ => { }, () => { });
            editor.ChoosePreset(Preset.Hardcore);
            editor.ChooseTimer(null);
            editor.ChooseTitle(LoserTitle.Thullad);
            var rules = editor.Build();
            Assert.AreEqual(Preset.Hardcore, rules.Preset);
            Assert.IsNull(rules.Settings!.TurnTimerSeconds);
            Assert.AreEqual(LoserTitle.Thullad, rules.Settings.LoserTitle);
        }

        [Test]
        public void TheLobbyLineSaysPresetTimerAndTitle()
        {
            var model = Lobby(new RulesSelection { GameId = GameId.Thulla, Preset = Preset.Family });
            Assert.AreEqual("Family · 30 s turns · loser: Donkey", ThullaRulesEditor.Summary(model, new Presets()));
        }

        [Test]
        public void TheLastCardRuleTravelsAsAnOverrideOnlyWhenItDiffersFromThePreset()
        {
            var editor = new ThullaRulesEditor(Root(), Lobby(null), new Presets(), _ => { }, () => { });
            Assert.IsNull(editor.Build().Overrides, "Standard draws from the discard, so nothing to override");
            editor.ChooseEmptyHand(EmptyHandLeader.InstantExit);
            var rules = editor.Build();
            Assert.AreEqual("instantExit", rules.Overrides!["emptyHandLeader"]);
            Assert.AreEqual(Preset.Standard, rules.Preset);
            editor.ChooseEmptyHand(EmptyHandLeader.DrawDiscard);
            Assert.IsNull(editor.Build().Overrides);
        }

        [Test]
        public void APresetResetsTheLastCardRuleToItsOwn()
        {
            var editor = new ThullaRulesEditor(Root(), Lobby(null), new Presets(), _ => { }, () => { });
            editor.ChooseEmptyHand(EmptyHandLeader.InstantExit);
            editor.ChoosePreset(Preset.Hardcore);
            Assert.IsNull(editor.Build().Overrides, "Hardcore already draws from the next player");
            editor.ChooseEmptyHand(EmptyHandLeader.DrawDiscard);
            Assert.AreEqual("drawDiscard", editor.Build().Overrides!["emptyHandLeader"]);
        }

        [Test]
        public void TheEditorAndTheLobbyLineStartFromTheTablesOverride()
        {
            var selection = new RulesSelection
            {
                GameId = GameId.Thulla,
                Preset = Preset.Standard,
                Overrides = new Dictionary<string, object> { ["emptyHandLeader"] = "instantExit" },
            };
            var model = Lobby(selection);
            Assert.AreEqual("Standard · 20 s turns · loser: Bhabhi · last card: You're out", ThullaRulesEditor.Summary(model, new Presets()));
            var editor = new ThullaRulesEditor(Root(), model, new Presets(), _ => { }, () => { });
            Assert.AreEqual("instantExit", editor.Build().Overrides!["emptyHandLeader"]);
        }
    }
}
