#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hulla.Application.Services;
using Hulla.Application.Settings;
using Hulla.Application.Table;
using Hulla.Application.ViewModels;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Achievements;
using Hulla.Presentation.Screens.Friends;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Screens.Leaderboard;
using Hulla.Presentation.Screens.Lobby;
using Hulla.Presentation.Screens.Missions;
using Hulla.Presentation.Screens.Pass;
using Hulla.Presentation.Screens.Profile;
using Hulla.Presentation.Screens.QuickPlay;
using Hulla.Presentation.Screens.Ranked;
using Hulla.Presentation.Screens.Rulebook;
using Hulla.Presentation.Screens.Settings;
using Hulla.Presentation.Screens.Shop;
using Hulla.Presentation.Text;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// The pseudo-locale check (GDD 26.1, #65): every screen laid out on a 1080 × 1920 phone panel in
    /// en-XA (accented, 30% longer, bracketed) and ar-XB (mirrored) shows no clipped text, no text
    /// running off the screen and no raw key. A failure lists every problem; set HULLA_SNAPSHOT_DIR
    /// to also write each screen as a PNG.
    /// </summary>
    public sealed class PseudoLocaleTests
    {
        private const int Width = 1080;
        private const int Height = 1920;
        private static readonly Regex RawKey = new Regex(@"^[a-z][a-z0-9_]*(\.[A-Za-z0-9_]+)+$");

        private sealed class Case
        {
            public Case(string name, VisualTreeAsset? screen, System.Action<VisualElement> bind)
            {
                Name = name;
                Screen = screen;
                Bind = bind;
            }

            public string Name { get; }

            public VisualTreeAsset? Screen { get; }

            public System.Action<VisualElement> Bind { get; }
        }

        [TearDown]
        public void TearDown() => Strings.Use(Strings.Fallback);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AccentedLongerTextFitsEveryScreen() => Check("en-XA");

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator MirroredLayoutFitsEveryScreen() => Check("ar-XB");

        [UnityTest]
        public IEnumerator MirroringReversesRowsAndRightAlignsText()
        {
            Assert.IsTrue(Strings.Use("ar-XB"));
            var host = new GameObject("rtl");
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            yield return null;
            var root = document.rootVisualElement;
            root.EnableInClassList("rtl", Strings.RightToLeft);
            var screen = AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset").Leaderboard!.Instantiate();
            root.Add(screen);
            yield return null;
            yield return null;
            Assert.AreEqual(FlexDirection.RowReverse, screen.Q(className: "settings__top").resolvedStyle.flexDirection);
            Assert.AreEqual(TextAnchor.UpperRight, screen.Q<Label>("reset").resolvedStyle.unityTextAlign);
            root.EnableInClassList("rtl", false);
            yield return null;
            Assert.AreEqual(FlexDirection.Row, screen.Q(className: "settings__top").resolvedStyle.flexDirection);
            Object.Destroy(host);
        }

        [Test]
        public void TheMirroredLocaleIsRightToLeftAndTheAccentedOneIsLonger()
        {
            Assert.IsTrue(Strings.Use("ar-XB"));
            Assert.IsTrue(Strings.RightToLeft);
            Assert.IsTrue(Strings.Use("en-XA"));
            Assert.IsFalse(Strings.RightToLeft);
            var pseudo = UiText.PlayAgain;
            Strings.Use(Strings.Fallback);
            Assert.Greater(pseudo.Length, UiText.PlayAgain.Length * 1.25);
            StringAssert.StartsWith("[", pseudo);
        }

        private static IEnumerator Check(string locale)
        {
            Assert.IsTrue(Strings.Use(locale), "the build has the " + locale + " table");
            var problems = new List<string>();
            foreach (var screen in Cases())
            {
                var host = new GameObject("pseudo-" + screen.Name);
                var texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                var settings = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset"));
                settings.targetTexture = texture;
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                settings.scale = 1;
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                yield return null;
                var root = document.rootVisualElement;
                root.style.width = Width;
                root.style.height = Height;
                root.EnableInClassList("rtl", Strings.RightToLeft);
                Assert.IsNotNull(screen.Screen, screen.Name + " is in the catalog");
                var instance = screen.Screen!.Instantiate();
                instance.style.flexGrow = 1;
                root.Add(instance);
                screen.Bind(instance);
                for (var frame = 0; frame < 4; frame++)
                {
                    yield return null;
                }

                problems.AddRange(Problems(root, screen.Name));
                Snapshot(texture, locale + "-" + screen.Name);
                Object.Destroy(host);
                Object.Destroy(settings);
                texture.Release();
                Object.Destroy(texture);
            }

            Assert.IsEmpty(problems, locale + ":\n" + string.Join("\n", problems));
        }

        /// <summary>Text that is clipped, runs past the screen's sides or shows a raw key.</summary>
        private static IEnumerable<string> Problems(VisualElement root, string screen)
        {
            var bounds = root.worldBound;
            foreach (var element in root.Query<TextElement>().ToList())
            {
                var text = element.text;
                if (string.IsNullOrEmpty(text) || element.resolvedStyle.display == DisplayStyle.None ||
                    element.resolvedStyle.visibility == UnityEngine.UIElements.Visibility.Hidden || element.layout.width <= 0 || !Shown(element))
                {
                    continue;
                }

                var where = screen + " " + (string.IsNullOrEmpty(element.name) ? element.GetType().Name : "#" + element.name) + " \"" + text + "\"";
                if (RawKey.IsMatch(text))
                {
                    yield return where + ": a raw key";
                    continue;
                }

                if (element.resolvedStyle.textOverflow == TextOverflow.Ellipsis)
                {
                    continue;
                }

                var box = element.contentRect;
                var wraps = element.resolvedStyle.whiteSpace != WhiteSpace.NoWrap;
                var needed = wraps
                    ? element.MeasureTextSize(text, box.width, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined)
                    : element.MeasureTextSize(text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                if (!wraps && needed.x > box.width + 2)
                {
                    yield return where + $": needs {needed.x:F0}px, has {box.width:F0}px";
                }
                else if (wraps && needed.y > box.height + 2)
                {
                    yield return where + $": needs {needed.y:F0}px high, has {box.height:F0}px";
                }

                var world = element.worldBound;
                if (!InHorizontalScroller(element) && (world.xMin < bounds.xMin - 1 || world.xMax > bounds.xMax + 1))
                {
                    yield return where + ": runs off the screen";
                }
            }
        }

        /// <summary>Chips in a sideways scroller run past the edge by design.</summary>
        private static bool InHorizontalScroller(VisualElement element)
        {
            for (var e = element.parent; e != null; e = e.parent)
            {
                if (e is ScrollView scroll && scroll.mode != ScrollViewMode.Vertical)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Hidden ancestors are not on screen.</summary>
        private static bool Shown(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (e.resolvedStyle.display == DisplayStyle.None || e.resolvedStyle.opacity <= 0.01f)
                {
                    return false;
                }
            }

            return true;
        }

        private static void Snapshot(RenderTexture texture, string name)
        {
            var dir = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            pixels.Apply();
            RenderTexture.active = previous;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), pixels.EncodeToPNG());
            Object.Destroy(pixels);
        }

        private static IEnumerable<Case> Cases()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");
            yield return new Case("home", catalog.Home, r =>
            {
                var friends = new HomeFriendsViewModel(new Fakes.Social());
                new HomeView(r, new HomeViewModel(new Fakes.Session(), new Fakes.Gateway()) { LastName = "Khizer" }, null, (_, _) => { }, (_, _, _) => { }, (_, _, _) => { }, () => { }, (_, _) => { }, (_, _) => { }, friends, () => { }, TutorialText.HowToPlay);
                _ = friends.RefreshAsync(default);
                var daily = new DailyRewardViewModel(new Fakes.Daily());
                new DailyRewardCard(r.Q<VisualElement>("daily"), daily, () => { });
                _ = daily.RefreshAsync(0, default);
            });
            yield return new Case("lobby", catalog.Lobby, r =>
            {
                var gateway = new Fakes.Gateway();
                var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
                new LobbyView(r, client, () => 0, () => { }, _ => true, null, () => RulebookText.PresetStandard, () => { });
                var state = Fakes.InRound(3);
                state.Status = TableStatus.Open;
                state.Round = null;
                gateway.Deliver(MessageTypes.TableState, 3, state);
            });
            yield return new Case("table", catalog.Table, r =>
            {
                var gateway = new Fakes.Gateway();
                var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
                new ThullaTableView(r, client, new Fakes.Chat(), () => 0, () => { });
                gateway.Deliver(MessageTypes.TableState, 5, Fakes.InRound(5));
                gateway.Deliver(MessageTypes.TurnStart, 5, new TurnStart { RoundId = "r", Seat = 0, Legal = new List<string> { "AS", "KS" }, DeadlineTs = 1 });
            });
            yield return new Case("results", catalog.Table, r =>
            {
                var gateway = new Fakes.Gateway();
                var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
                new ThullaTableView(r, client, new Fakes.Chat(), () => 0, () => { });
                gateway.Deliver(MessageTypes.TableState, 5, Fakes.InRound(5));
                gateway.Deliver(MessageTypes.RoundEnd, 6, new RoundEnd { RoundId = "r", BhabhiSeat = 2, EscapeOrder = new List<long> { 0, 1, 3 }, Points = new List<long>(), Stats = new List<SeatStats>(), Fairness = new Fairness { Seed = "s", Commitment = "c", DeckOrder = new List<string>() } });
            });
            yield return new Case("quick-play", catalog.QuickPlay, r =>
            {
                var model = new QuickPlayViewModel(new Fakes.Matchmaker());
                new QuickPlayView(r, model, () => { });
                _ = model.StartAsync(5, default);
            });
            yield return new Case("settings", catalog.Settings, r =>
                new SettingsView(r, new SettingsModel(new MemoryStore()), () => { }, new SettingsView.AccountActions(_ => { }, _ => { }, _ => { }, () => null)));
            yield return new Case("rulebook", catalog.Rulebook, r =>
                new RulebookView(r, RulebookText.Title, ThullaRulebook.Build(new TableModel("me"), new NoPresets()), () => { }));
            yield return new Case("profile", catalog.Profile, r =>
            {
                var model = new ProfileViewModel(new Fakes.Social());
                new ProfileView(r, model, () => { }, (_, _, _) => { });
                _ = model.RefreshAsync(default);
            });
            yield return new Case("friends", catalog.Friends, r =>
            {
                var model = new FriendsViewModel(new Fakes.Social());
                new FriendsView(r, model, () => { }, new FriendsView.Actions(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }));
                _ = model.RefreshAsync(default);
            });
            yield return new Case("leaderboard", catalog.Leaderboard, r =>
            {
                var model = new LeaderboardViewModel(new Fakes.Boards());
                new LeaderboardView(r, model, () => 1_000_000_000_000, "me", () => { }, (_, _) => { }, () => { });
                _ = model.RefreshAsync(default);
            });
            yield return new Case("ranked", catalog.Ranked, r =>
            {
                var model = new RankedViewModel(new Fakes.Ranked());
                new RankedView(r, model, () => 1_000_000_000_000, "me", () => { }, _ => { });
                _ = model.RefreshAsync(default);
            });
            yield return new Case("achievements", catalog.Achievements, r =>
            {
                var model = new AchievementsViewModel(new Fakes.Achievements());
                new AchievementsView(r, model, () => { });
                _ = model.RefreshAsync(default);
            });
            yield return new Case("shop", catalog.Shop, r =>
            {
                var model = new ShopViewModel(new Fakes.Shop());
                new ShopView(r, model, () => { }, _ => { }, _ => { });
                _ = model.RefreshAsync(default);
            });
            yield return new Case("pass", catalog.Pass, r =>
            {
                var model = new PassViewModel(new Fakes.Pass());
                new PassView(r, model, () => 1_000_000_000_000, () => { }, (_, _) => { });
                _ = model.RefreshAsync(default);
            });
            yield return new Case("phone", catalog.Phone, r =>
            {
                var model = new PhoneViewModel(new Fakes.Phone());
                new Hulla.Presentation.Screens.Phone.PhoneView(r, model, () => { }, new Hulla.Presentation.Screens.Phone.PhoneView.PhoneActions(_ => { }, _ => { }, () => { }, () => { }, () => { }));
                _ = model.RefreshAsync(default);
            });
            yield return new Case("missions", catalog.Missions, r =>
            {
                var model = new MissionsViewModel(new Fakes.Missions());
                new MissionsView(r, model, () => { }, _ => { }, _ => { });
                _ = model.RefreshAsync(0, default);
            });
        }

        private sealed class MemoryStore : ISettingsStore
        {
            private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

            public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var v) ? v : fallback;

            public void SetInt(string key, int value) => _values[key] = value;

            public void Save()
            {
            }
        }

        private sealed class NoPresets : IPresetCatalog
        {
            public (ThullaRules Rules, Hulla.Protocol.Settings Settings)? Find(string presetId) => null;

            public Awaitable LoadAsync(System.Threading.CancellationToken cancellation)
            {
                var source = new AwaitableCompletionSource();
                source.SetResult();
                return source.Awaitable;
            }
        }
    }
}
#endif
