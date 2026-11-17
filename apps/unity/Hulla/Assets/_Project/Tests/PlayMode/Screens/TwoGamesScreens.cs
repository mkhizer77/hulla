#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hulla.Application.Progress;
using Hulla.Application.ViewModels;
using Hulla.Domain;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Share;
using Hulla.Presentation.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// Home with two games and a page per game (docs/design/two-games.md), on the real screen asset.
    /// Render is explicit and draws PNGs: <c>-testFilter Hulla.Screens.Tests.TwoGamesScreens</c>, folder in HULLA_SNAPSHOT_OUT.
    /// </summary>
    public sealed class TwoGamesScreens
    {
        private static ScreenCatalog Catalog => AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");

        private sealed class Calls
        {
            public string? Created;
            public string? QuickPlayed;
            public string? RungPlayed;
            public string? RungCreated;
        }

        private static HomeView NewHome(VisualElement root, Calls calls, bool rungLive = true)
        {
            var friends = new HomeFriendsViewModel(new Fakes.Social());
            var home = new HomeView(root, new HomeViewModel(new Fakes.Session(), new Fakes.Gateway()) { LastName = "Khizer" }, null,
                (name, _) => calls.Created = name, (_, _, _) => { }, (name, seats, _) => calls.QuickPlayed = name + ":" + seats, () => { }, (_, _) => { }, (_, _) => { }, friends, () => { }, TutorialText.HowToPlay);
            home.PlayRung = (name, _) => calls.RungPlayed = name;
            home.CreateRung = (name, set, _) => calls.RungCreated = name + ":" + set;
            home.MyNumbers = game => game == HomeView.Rung ? Strings.Format(UiText.YourRung, 12, 58) : Strings.Format(UiText.YourThulla, 142);
            home.RungLive = rungLive;
            return home;
        }

        /// <summary>Presses a button that is not on a panel: events do not dispatch there, so its click handlers are run directly.</summary>
        private static void Tap(Button button)
        {
            var invoke = typeof(Clickable).GetMethod("Invoke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(invoke, "Clickable.Invoke");
            using var click = NavigationSubmitEvent.GetPooled();
            invoke!.Invoke(button.clickable, new object[] { click });
        }

        private sealed class TwoGameMedals : IAchievementsService
        {
            public Awaitable<Result<(IReadOnlyList<AchievementInfo> Achievements, IReadOnlyList<string> UnlockedNow)>> AchievementsAsync(System.Threading.CancellationToken c) =>
                Fakes.Done(Result<(IReadOnlyList<AchievementInfo> Achievements, IReadOnlyList<string> UnlockedNow)>.Ok((new List<AchievementInfo>
                {
                    new AchievementInfo("first_thulla", "play", "thullas_given", 1, 1, 1_000, 5, null),
                    new AchievementInfo("rung_bazis_10", "play", "rung_bazis", 10, 4, null, 10, null),
                    new AchievementInfo("first_friend", "social", "friends", 1, 0, null, 5, null),
                }, new List<string>())));
        }

        [UnityTest]
        public IEnumerator TheTrophiesFilterShowsOneGamesMedalsAndTheSharedOnes()
        {
            GamePick.Enabled = true;
            GamePick.Reset();
            try
            {
                var root = Catalog.Achievements!.Instantiate();
                var model = new AchievementsViewModel(new TwoGameMedals());
                using var view = new Hulla.Presentation.Screens.Achievements.AchievementsView(root, model, () => { });
                var load = model.RefreshAsync(default).GetAwaiter();
                while (!load.IsCompleted) yield return null;
                Assert.IsNotNull(root.Q<Button>("game-all"), "the filter sits under the segments");
                Assert.AreEqual(3, root.Query(className: "medal-tile").ToList().Count);
                Tap(root.Q<Button>("game-rung"));
                Assert.AreEqual(GameFilter.Rung, GamePick.Chosen);
                Assert.AreEqual(2, root.Query(className: "medal-tile").ToList().Count, "Rung's medal and the one both games share");
                Assert.IsNull(root.Q(className: "medal--first_thulla"));
                Tap(root.Q<Button>("game-thulla"));
                Assert.IsNull(root.Q(className: "medal--rung_bazis_10"));
            }
            finally
            {
                GamePick.Enabled = false;
                GamePick.Reset();
            }
        }

        [Test]
        public void WithOneGameLiveTrophiesShowNoFilter()
        {
            GamePick.Enabled = false;
            var root = Catalog.Achievements!.Instantiate();
            using var view = new Hulla.Presentation.Screens.Achievements.AchievementsView(root, new AchievementsViewModel(new TwoGameMedals()), () => { });
            Assert.IsNull(root.Q<VisualElement>("game-pick"));
        }

        [Test]
        public void HomeShowsTwoGameCardsAndNoPlayButtonOfItsOwn()
        {
            var root = Catalog.Home!.Instantiate();
            NewHome(root, new Calls());
            var stage = root.Q<VisualElement>("stage");
            Assert.IsNotNull(stage.Q<Button>("card-thulla"));
            Assert.IsNotNull(stage.Q<Button>("card-rung"));
            Assert.IsNull(stage.Q<Button>("quick"), "Play now lives on a game's page");
            Assert.IsNull(stage.Q<Button>("create"), "Create Room lives on a game's page");
            Assert.IsNotNull(stage.Q<Button>("join-toggle"), "Join by Code works for either game, so it stays");
            Assert.AreEqual(UiText.PlayersFour, root.Q<Label>("game-rung-players").text);
        }

        [Test]
        public void ACardOpensItsGamesPageAndBackReturnsHome()
        {
            var root = Catalog.Home!.Instantiate();
            var home = NewHome(root, new Calls());
            var changes = "";
            home.PageChanged += open => changes += open ? "+" : "-";
            Tap(root.Q<Button>("card-thulla"));
            Assert.AreEqual(HomeView.Thulla, home.Page);
            Assert.IsTrue(root.Q<VisualElement>("game-page").ClassListContains("home__page--thulla"));
            StringAssert.Contains("142", root.Q<Label>("page-stats").text);
            Tap(root.Q<Button>("page-back"));
            Assert.IsNull(home.Page);
            Tap(root.Q<Button>("card-rung"));
            Assert.AreEqual(HomeView.Rung, home.Page);
            StringAssert.Contains("58", root.Q<Label>("page-stats").text);
            Assert.AreEqual("+-+", changes);
        }

        [Test]
        public void ThullasPagePlaysAndCreatesThullaAndRungsPagePlaysAndCreatesRung()
        {
            var root = Catalog.Home!.Instantiate();
            var calls = new Calls();
            var home = NewHome(root, calls);
            home.OpenPage(HomeView.Thulla);
            Tap(root.Q<Button>("quick"));
            Tap(root.Q<Button>("create"));
            Assert.AreEqual("Khizer:4", calls.QuickPlayed);
            Assert.AreEqual("Khizer", calls.Created);
            Assert.IsNull(calls.RungPlayed);
            home.ClosePage();
            home.OpenPage(HomeView.Rung);
            Tap(root.Q<Button>("set-hokm"));
            Assert.AreEqual("hokm", home.RungSet);
            Assert.AreEqual(Strings.Text("preset.hokm.description"), root.Q<Label>("rung-set-line").text);
            Tap(root.Q<Button>("quick"));
            Tap(root.Q<Button>("create"));
            Assert.AreEqual("Khizer", calls.RungPlayed);
            Assert.AreEqual("Khizer:hokm", calls.RungCreated);
        }

        [Test]
        public void TheCasualOrRankedSwitchIsLeftToTheStylesheetSoRungsPageCanHideIt()
        {
            var root = Catalog.Home!.Instantiate();
            var friends = new HomeFriendsViewModel(new Fakes.Social());
            _ = new HomeView(root, new HomeViewModel(new Fakes.Session(), new Fakes.Gateway()) { LastName = "Khizer" }, null,
                (_, _) => { }, (_, _, _) => { }, (_, _, _) => { }, () => { }, (_, _) => { }, (_, _) => { }, friends, () => { }, TutorialText.HowToPlay, openRanked: (_, _) => { });
            // An inline Flex would beat the Rung page's rule that hides the switch (Ranked is Thulla only).
            Assert.AreEqual(StyleKeyword.Null, root.Q<VisualElement>("mode").style.display.keyword);
        }

        [Test]
        public void SearchingForARungTableShowsNoSeatPicker()
        {
            var root = Catalog.QuickPlay!.Instantiate();
            var model = new Hulla.Application.ViewModels.QuickPlayViewModel(new Fakes.Matchmaker());
            _ = new Hulla.Presentation.Screens.QuickPlay.QuickPlayView(root, model, () => { });
            _ = model.StartRungAsync(default);
            Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("seats").style.display.value);
            Assert.AreEqual(DisplayStyle.None, root.Q<Label>("seats-label").style.display.value);
        }

        [Test]
        public void WithTheFlagOffRungsCardSaysComingSoonAndDoesNotOpen()
        {
            var root = Catalog.Home!.Instantiate();
            var home = NewHome(root, new Calls(), rungLive: false);
            Assert.AreEqual(UiText.ComingSoon, root.Q<Label>("game-rung-players").text);
            Tap(root.Q<Button>("card-rung"));
            Assert.IsNull(home.Page);
        }

        [UnityTest, Explicit]
        public IEnumerator Render()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var folder = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_OUT") ?? "Temp";
            foreach (var (name, page) in new[] { ("two-home", (string?)null), ("two-page-thulla", HomeView.Thulla), ("two-page-rung", HomeView.Rung) })
            {
                var task = CardRenderer.RenderAsync(settings, 1080, 2400, root =>
                {
                    ScreenCases.Skin(root, Hulla.Application.Settings.TableSkin.Basant);
                    var screen = Catalog.Home!.Instantiate();
                    screen.style.flexGrow = 1;
                    root.Add(screen);
                    if (screen.Q(className: "screen") is { } face)
                    {
                        Hulla.Presentation.Theme.AmbientLayer.Decorate(face, Hulla.Application.Settings.TableSkin.Basant, false);
                    }

                    var home = NewHome(screen, new Calls());
                    if (page == null)
                    {
                        var bar = new NavBar(UiText.NavLabel);
                        bar.Select(NavTab.Home);
                        root.Add(bar);
                    }
                    else
                    {
                        home.OpenPage(page);
                    }
                });
                var awaiter = task.GetAwaiter();
                while (!awaiter.IsCompleted) yield return null;
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), awaiter.GetResult());
            }

            // Trophies with the game filter on.
            GamePick.Enabled = true;
            GamePick.Reset();
            try
            {
                var model = new AchievementsViewModel(new TwoGameMedals());
                var medals = CardRenderer.RenderAsync(settings, 1080, 2400, root =>
                {
                    ScreenCases.Skin(root, Hulla.Application.Settings.TableSkin.Basant);
                    var screen = Catalog.Achievements!.Instantiate();
                    screen.style.flexGrow = 1;
                    root.Add(screen);
                    _ = new Hulla.Presentation.Screens.Achievements.AchievementsView(screen, model, () => { });
                    SegmentBar.Mount(screen, new[] { new SegmentBar.Entry("missions", UiText.Missions, () => { }), new SegmentBar.Entry("achievements", UiText.Achievements, () => { }), new SegmentBar.Entry("leaderboard", UiText.Leaderboard, () => { }) }, "achievements");
                    _ = model.RefreshAsync(default);
                });
                var waiting = medals.GetAwaiter();
                while (!waiting.IsCompleted) yield return null;
                File.WriteAllBytes(Path.Combine(folder, "two-trophies.png"), waiting.GetResult());
            }
            finally
            {
                GamePick.Enabled = false;
                GamePick.Reset();
            }

            Assert.Pass(folder);
        }
    }
}
#endif
