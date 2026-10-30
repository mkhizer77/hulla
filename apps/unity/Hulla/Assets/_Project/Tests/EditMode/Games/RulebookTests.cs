using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using NUnit.Framework;
using TableSettings = Hulla.Protocol.Settings;
using UnityEngine;

namespace Hulla.Games.Thulla.Tests
{
    public sealed class RulebookTests
    {
        private sealed class Presets : IPresetCatalog
        {
            public (ThullaRules Rules, TableSettings Settings)? Find(string presetId) => presetId == "family"
                ? (new ThullaRules { HandTaking = HandTaking.LeaderOnly }, new TableSettings { TurnTimerSeconds = 30, LoserTitle = LoserTitle.Donkey })
                : (new ThullaRules(), new TableSettings());

            public Awaitable LoadAsync(CancellationToken cancellation) => throw new System.NotSupportedException();
        }

        private static TableModel Table(RulesSelection selection, ThullaRules? resolved, TableMode mode = TableMode.Private)
        {
            var model = new TableModel("me");
            model.Apply(new ServerMessage(MessageTypes.TableState, 1, new TableState
            {
                Code = "ABC123",
                Mode = mode,
                Status = resolved == null ? TableStatus.Open : TableStatus.InRound,
                LastSeq = 1,
                Rules = selection,
                Seats = new List<Seat> { new Seat { SeatSeat = 0, PlayerId = "me", Connected = true } },
                Round = resolved == null ? null : new View
                {
                    Phase = Phase.Play,
                    SeatCount = 1,
                    Rules = resolved,
                    Seats = new List<SeatView> { new SeatView { HandCount = 0 } },
                    EscapeOrder = new List<long>(),
                    Trick = new Trick { Plays = new List<Play>() },
                },
            }));
            return model;
        }

        private static List<string> TableLines(TableModel model) =>
            ThullaRulebook.Build(model, new Presets()).First(s => s.Id == "table").Lines.ToList();

        [Test]
        public void R31_HandTakingIsLeaderOnlyAtPublicTablesAndOffInRankedByDefault()
        {
            var standard = new RulesSelection { GameId = GameId.Thulla, Preset = Preset.Standard };
            CollectionAssert.Contains(TableLines(Table(standard, null, TableMode.Public)), RulebookText.HandTakingLeader);
            CollectionAssert.Contains(TableLines(Table(standard, null, TableMode.Ranked)), RulebookText.HandTakingOff);
        }

        [Test]
        public void BeforeTheDealThePresetsSettingsAreDescribed()
        {
            var lines = TableLines(Table(new RulesSelection { GameId = GameId.Thulla, Preset = Preset.Family }, null));
            CollectionAssert.Contains(lines, "Preset: Family.");
            CollectionAssert.Contains(lines, Strings.Format(RulebookText.TurnTimer, 30));
            CollectionAssert.Contains(lines, Strings.Format(RulebookText.LoserTitle, RulebookText.Donkey));
            CollectionAssert.Contains(lines, RulebookText.HandTakingLeader);
        }

        [Test]
        public void BeforeTheDealTheHostsOverridesSitOnThePreset()
        {
            var selection = new RulesSelection
            {
                GameId = GameId.Thulla,
                Preset = Preset.Family,
                Overrides = new Dictionary<string, object> { ["emptyHandLeader"] = "instantExit" },
            };
            var lines = TableLines(Table(selection, null));
            CollectionAssert.Contains(lines, RulebookText.EmptyHandInstantExit);
            CollectionAssert.Contains(lines, RulebookText.HandTakingLeader, "the preset's other toggles stay");
        }

        [Test]
        public void DuringARoundTheResolvedTogglesWin()
        {
            var hardcore = new ThullaRules
            {
                OpeningThullaApplies = true,
                ThullaEndsTrick = false,
                EmptyHandLeader = EmptyHandLeader.DrawNextPlayer,
                HandTaking = HandTaking.Anyone,
                TwoPlayerEndgame = TwoPlayerEndgame.ThullaEndsRound,
                ConfirmedFastForward = false,
                OpeningSimultaneous = false,
            };
            var lines = TableLines(Table(new RulesSelection { GameId = GameId.Thulla, Preset = Preset.Standard }, hardcore));
            CollectionAssert.IsSubsetOf(new[]
            {
                RulebookText.OpeningThullaOn,
                RulebookText.ThullaEndsTrickOff,
                RulebookText.EmptyHandDrawNext,
                RulebookText.HandTakingAnyone,
                Strings.Format(RulebookText.EndgameThullaEndsRound, RulebookText.Bhabhi),
                RulebookText.FastForwardOff,
                RulebookText.OpeningSimultaneousOff,
            }, lines);
        }

        [Test]
        public void EveryToggleValueHasASentence()
        {
            var all = new List<string>();
            foreach (var empty in new[] { EmptyHandLeader.DrawDiscard, EmptyHandLeader.DrawNextPlayer, EmptyHandLeader.InstantExit })
            foreach (var taking in new[] { HandTaking.Off, HandTaking.LeaderOnly, HandTaking.Anyone })
            foreach (var endgame in new[] { TwoPlayerEndgame.DrawOpponent, TwoPlayerEndgame.DrawDiscard, TwoPlayerEndgame.ThullaEndsRound })
            foreach (var flag in new[] { true, false })
            {
                var rules = new ThullaRules
                {
                    EmptyHandLeader = empty,
                    HandTaking = taking,
                    TwoPlayerEndgame = endgame,
                    OpeningSimultaneous = flag,
                    OpeningThullaApplies = flag,
                    ThullaEndsTrick = flag,
                    ConfirmedFastForward = flag,
                };
                all.AddRange(TableLines(Table(new RulesSelection { GameId = GameId.Thulla }, rules)));
            }

            var distinct = all.Distinct().Count(line => !line.StartsWith("Preset") && !line.StartsWith("Each turn") && !line.StartsWith("The last"));
            Assert.AreEqual(3 + 3 + 3 + 2 * 4, distinct, "one sentence for every value of the seven engine toggles");
        }

        [Test]
        public void TheRulebookHasEverySectionTheGddAsksFor()
        {
            var ids = ThullaRulebook.Build(Table(new RulesSelection { GameId = GameId.Thulla }, null), new Presets()).Select(s => s.Id);
            CollectionAssert.AreEqual(new[] { "basics", ThullaRulebook.WhyNotSection, "auto-play", "glossary", "tips", "table" }, ids);
        }
    }
}
