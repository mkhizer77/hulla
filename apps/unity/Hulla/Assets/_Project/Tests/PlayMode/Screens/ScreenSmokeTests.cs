#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hulla.Application.Services;
using Hulla.Application.Settings;
using Hulla.Application.Table;
using Hulla.Application.ViewModels;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Friends;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Screens.Lobby;
using Hulla.Presentation.Screens.Profile;
using Hulla.Presentation.Screens.QuickPlay;
using Hulla.Presentation.Screens.Rulebook;
using Hulla.Presentation.Screens.Settings;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// Every screen opens from its document with fake services and shows what it is bound to (#78).
    /// They run in the editor's play mode; the device matrix run comes with Test Lab.
    /// </summary>
    public sealed class ScreenSmokeTests
    {
        private GameObject? _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.Destroy(_host);
            }
        }

        private static ScreenCatalog Catalog => AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");

        private IEnumerator Open(VisualTreeAsset? screen, System.Action<VisualElement> bind)
        {
            Assert.IsNotNull(screen, "the catalog has the screen");
            _host = new GameObject("screen");
            var document = _host.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            yield return null;
            var root = screen!.Instantiate();
            root.style.flexGrow = 1;
            document.rootVisualElement.Add(root);
            bind(root);
            for (var i = 0; i < 3; i++)
            {
                yield return null;
            }
        }

        private static void Shown(VisualElement element, string what) =>
            Assert.AreNotEqual(DisplayStyle.None, element.resolvedStyle.display, what + " is shown");

        [UnityTest]
        public IEnumerator HomeShowsActionsAndAFriendsTableToJoin()
        {
            VisualElement root = null!;
            var friends = new HomeFriendsViewModel(new Fakes.Social());
            yield return Open(Catalog.Home, r =>
            {
                root = r;
                new HomeView(r, new HomeViewModel(new Fakes.Session(), new Fakes.Gateway()) { LastName = "Khizer" }, null, (_, _) => { }, (_, _, _) => { }, (_, _, _) => { }, () => { }, (_, _) => { }, (_, _) => { }, friends, () => { }, "How to play");
                _ = friends.RefreshAsync(default);
            });
            Assert.AreEqual("Khizer", root.Q<TextField>("name").value);
            Assert.AreEqual("Quick Play", root.Q<Button>("quick").text);
            Shown(root.Q<Button>("hero"), "the hero");
            StringAssert.Contains("Ayesha", root.Q<Button>("hero").text);
            Assert.AreEqual(3, root.Q<VisualElement>("seats").childCount, "4, 5 and 6 seat chips");
        }

        [UnityTest]
        public IEnumerator LobbyShowsTheCodeSeatsAndHostControls()
        {
            VisualElement root = null!;
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            yield return Open(Catalog.Lobby, r =>
            {
                root = r;
                new LobbyView(r, client, () => 0, () => { }, _ => true, null, () => "Standard · 20 s turns · loser: Bhabhi", () => { });
                var state = Fakes.InRound(3);
                state.Status = TableStatus.Open;
                state.Round = null;
                gateway.Deliver(MessageTypes.TableState, 3, state);
            });
            Assert.AreEqual("K7Q2MX", root.Q<Label>("code").text);
            Assert.AreEqual(4, root.Q<VisualElement>("seats").childCount);
            Shown(root.Q<Button>("share"), "Share code");
            Shown(root.Q<Button>("start"), "Start for the host");
            Shown(root.Q<Button>("edit-rules"), "Edit rules for the host");
            StringAssert.Contains("Standard", root.Q<Label>("rules").text);
        }

        [UnityTest]
        public IEnumerator TableShowsTheHandLegalCardsAndAReasonForAnIllegalTap()
        {
            VisualElement root = null!;
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            ThullaTableView view = null!;
            yield return Open(Catalog.Table, r =>
            {
                root = r;
                view = new ThullaTableView(r, client, new Fakes.Chat(), () => 0, () => { });
                gateway.Deliver(MessageTypes.TableState, 5, Fakes.InRound(5));
                gateway.Deliver(MessageTypes.TurnStart, 5, new TurnStart { RoundId = "r", Seat = 0, Legal = new List<string> { "AS", "KS" }, DeadlineTs = 1 });
            });
            var cards = root.Q<VisualElement>("hand").Query<VisualElement>(className: "card").ToList();
            Assert.AreEqual(4, cards.Count, "the whole hand");
            Assert.AreEqual(2, cards.Count(c => c.ClassListContains("card--legal")), "R18: only the spades are lit");
            Assert.AreEqual(3, root.Q<VisualElement>("opponents").Query<VisualElement>(className: "seat").ToList().Count(b => b.resolvedStyle.display != DisplayStyle.None));
            StringAssert.Contains("K7Q2MX", root.Q<Label>("info").text);

            var refused = "";
            view.CardRefused += card => refused = card;
            var heart = cards.First(c => !c.ClassListContains("card--legal") && c.Q<Label>(className: "card__suit").text == "♥");
            using (var up = PointerUpEvent.GetPooled())
            {
                up.target = heart;
                heart.SendEvent(up);
            }

            yield return null;
            Assert.AreEqual("2H", refused, "an illegal tap is refused");
            StringAssert.Contains("Must follow", root.Q<Label>("hint").text);
        }

        [UnityTest]
        public IEnumerator ResultsOpenAtRoundEnd()
        {
            VisualElement root = null!;
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            yield return Open(Catalog.Table, r =>
            {
                root = r;
                new ThullaTableView(r, client, new Fakes.Chat(), () => 0, () => { });
                var state = Fakes.InRound(5);
                gateway.Deliver(MessageTypes.TableState, 5, state);
                gateway.Deliver(MessageTypes.RoundEnd, 6, new RoundEnd { RoundId = "r", BhabhiSeat = 2, EscapeOrder = new List<long> { 0, 1, 3 }, Points = new List<long>(), Stats = new List<SeatStats>(), Fairness = new Fairness { Seed = "s", Commitment = "c", DeckOrder = new List<string>() } });
            });
            Shown(root.Q<VisualElement>("results"), "the results overlay");
            Assert.AreEqual("Bilal is the Bhabhi", root.Q<Label>("result-title").text);
        }

        [UnityTest]
        public IEnumerator QuickPlayShowsTheSearch()
        {
            VisualElement root = null!;
            var model = new QuickPlayViewModel(new Fakes.Matchmaker());
            yield return Open(Catalog.QuickPlay, r =>
            {
                root = r;
                new QuickPlayView(r, model, () => { });
                _ = model.StartAsync(5, default);
            });
            Assert.AreEqual("Finding a table for 5…", root.Q<Label>("title").text);
            Assert.AreEqual("Cancel", root.Q<Button>("cancel").text);
        }

        [UnityTest]
        public IEnumerator SettingsListsEverySettingAndTheAccount()
        {
            VisualElement root = null!;
            yield return Open(Catalog.Settings, r =>
            {
                root = r;
                new SettingsView(r, new SettingsModel(new MemoryStore()), () => { }, new SettingsView.AccountActions(_ => { }, _ => { }, _ => { }, () => null));
            });
            Assert.AreEqual(9, root.Query<Toggle>().ToList().Count, "nine on/off settings, analytics consent among them");
            Assert.IsTrue(root.Query<Button>().ToList().Any(b => b.text == "Delete my account"));
        }

        [UnityTest]
        public IEnumerator RulebookHasEverySection()
        {
            VisualElement root = null!;
            yield return Open(Catalog.Rulebook, r =>
            {
                root = r;
                var model = new TableModel("me");
                new RulebookView(r, RulebookText.Title, ThullaRulebook.Build(model, new NoPresets()), () => { });
            });
            Assert.AreEqual(6, root.Q<VisualElement>("list").childCount);
        }

        [UnityTest]
        public IEnumerator ProfileShowsTheFriendCode()
        {
            VisualElement root = null!;
            var model = new ProfileViewModel(new Fakes.Social());
            yield return Open(Catalog.Profile, r =>
            {
                root = r;
                new ProfileView(r, model, () => { }, (_, _, _) => { });
                _ = model.RefreshAsync(default);
            });
            Assert.AreEqual("K7Q2MXRP", root.Q<Label>("code").text);
            Assert.AreEqual("khizer_k", root.Q<TextField>("handle").value);
        }

        [UnityTest]
        public IEnumerator FriendsShowsRequestsFriendsAndRecentPlayers()
        {
            VisualElement root = null!;
            var model = new FriendsViewModel(new Fakes.Social());
            yield return Open(Catalog.Friends, r =>
            {
                root = r;
                new FriendsView(r, model, () => { }, new FriendsView.Actions(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }));
                _ = model.RefreshAsync(default);
            });
            var headings = root.Q<VisualElement>("list").Query<Label>(className: "settings__heading").ToList().Select(l => l.text).ToList();
            CollectionAssert.IsSubsetOf(new[] { "Requests", "Friends", "Played with recently" }, headings);
        }

        [UnityTest]
        public IEnumerator MissionsShowProgressClaimAndSwap()
        {
            VisualElement root = null!;
            var model = new MissionsViewModel(new Fakes.Missions());
            yield return Open(Catalog.Missions, r =>
            {
                root = r;
                new Hulla.Presentation.Screens.Missions.MissionsView(r, model, () => { }, _ => { }, _ => { });
                _ = model.RefreshAsync(0, default);
            });
            var texts = root.Query<Label>().ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(texts, "Give 3 thullas");
            CollectionAssert.Contains(texts, "4/20 · 300 coins, 5 gems");
            var buttons = root.Query<Button>().ToList().Select(b => b.text).ToList();
            CollectionAssert.Contains(buttons, "Claim");
            CollectionAssert.Contains(buttons, "Swap");
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
