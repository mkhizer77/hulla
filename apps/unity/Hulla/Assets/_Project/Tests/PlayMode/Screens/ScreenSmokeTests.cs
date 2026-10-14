#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Hulla.Application.Progress;
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
using Hulla.Presentation.Screens.Ranked;
using Hulla.Presentation.Screens.Lobby;
using Hulla.Presentation.Screens.Profile;
using Hulla.Presentation.Screens.QuickPlay;
using Hulla.Presentation.Screens.Rulebook;
using Hulla.Presentation.Screens.Settings;
using Hulla.Presentation.Screens.Pass;
using Hulla.Presentation.Screens.Shop;
using Hulla.Presentation.Share;
using Hulla.Presentation.TableKit;
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
                var daily = new DailyRewardViewModel(new Fakes.Daily());
                new DailyRewardCard(r.Q<VisualElement>("daily"), daily, () => { });
                _ = daily.RefreshAsync(0, default);
            });
            Assert.AreEqual("Khizer", root.Q<TextField>("name").value);
            Assert.AreEqual("Play now", root.Q<Button>("quick").text);
            Assert.AreEqual("Khizer", root.Q<Label>("name-label").text, "the name sits in the header");
            Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("name-panel").style.display.value, "a known name is not asked again");
            Assert.AreEqual(1, root.Q<VisualElement>("party").childCount, "the one friend online sits in the party bar");
            Shown(root.Q<Button>("hero"), "the hero");
            StringAssert.Contains("Ayesha", root.Q<Button>("hero").text);
            Assert.AreEqual(3, root.Q<VisualElement>("seats").childCount, "4, 5 and 6 seat chips");
            Shown(root.Q<VisualElement>("daily"), "the daily reward");
            Assert.AreEqual("Daily reward, day 4: 80 coins + 2 gems", root.Q<Label>("daily-text").text);
        }

        [UnityTest]
        public IEnumerator HomeRestsPlayNowWhenTheServerSaysSo()
        {
            VisualElement root = null!;
            HomeView home = null!;
            yield return Open(Catalog.Home, r =>
            {
                root = r;
                home = new HomeView(r, new HomeViewModel(new Fakes.Session(), new Fakes.Gateway()) { LastName = "Khizer" }, null, (_, _) => { }, (_, _, _) => { }, (_, _, _) => { });
            });
            Assert.IsTrue(root.Q<Button>("quick").enabledSelf);
            home.SetQuickPlay(false, "Hulla is having a short break. Tables open again soon.");
            yield return null;
            Assert.IsFalse(root.Q<Button>("quick").enabledSelf);
            Assert.IsTrue(root.Q<Button>("create").enabledSelf, "rooms with friends still open");
            Assert.AreEqual("Hulla is having a short break. Tables open again soon.", root.Q<Label>("status").text);
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
        public IEnumerator MyPickUpHoldsTheCardsUntilTheyFlyIntoTheirSlots()
        {
            VisualElement root = null!;
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            ThullaTableView view = null!;
            var state = Fakes.InRound(5);
            state.Round!.Trick!.Plays.Add(new Play { Seat = 1, Card = "3S" });
            yield return Open(Catalog.Table, r =>
            {
                root = r;
                view = new ThullaTableView(r, client, new Fakes.Chat(), () => 0, () => { });
                gateway.Deliver(MessageTypes.TableState, 5, state);
            });
            var resolved = new TrickResolved { RoundId = "r", Cards = new List<string> { "7S", "3S" }, Escapes = new List<Escape>(), HandCounts = new List<long> { 6, 3, 4, 3 }, Picker = 0, NextLeader = 0, LedSuit = Suit.S };
            gateway.Deliver(MessageTypes.TrickResolved, 6, resolved);
            gateway.Deliver(MessageTypes.Pickup, 7, new Pickup { RoundId = "r", Seat = 0, Cards = new List<string> { "7S", "3S" } });
            yield return null;
            var cards = root.Q<VisualElement>("hand").Query<VisualElement>(className: "card").ToList();
            Assert.AreEqual(6, cards.Count, "the picked-up cards already have slots in the fan");
            Assert.AreEqual(2, cards.Count(c => c.ClassListContains("card--arriving")), "but stay out of sight until they fly in");

            view.Present(new ServerMessage(MessageTypes.TrickResolved, 6, resolved));
            view.Tick(0.5f);
            var started = Time.realtimeSinceStartup;
            while (root.Q<VisualElement>("hand").Query<VisualElement>(className: "card--arriving").ToList().Count > 0 && Time.realtimeSinceStartup - started < 2f)
            {
                view.Tick(Time.deltaTime);
                yield return null;
            }

            Assert.Less(Time.realtimeSinceStartup - started, 2f, "each card is revealed as it lands, well before the hold gives up");
            var landed = root.Q<VisualElement>("hand").Query<VisualElement>(className: "card").ToList().Where(c => ((CardElement)c).Card == "7S" || ((CardElement)c).Card == "3S").ToList();
            Assert.AreEqual(2, landed.Count(c => c.ClassListContains("card--new")), "the landed cards are lit");
        }

        [UnityTest]
        public IEnumerator ADiscardedTrickCountsOnTheStackAsItLands()
        {
            VisualElement root = null!;
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            ThullaTableView view = null!;
            var state = Fakes.InRound(5);
            state.Round!.Trick!.Plays.Add(new Play { Seat = 1, Card = "3S" });
            yield return Open(Catalog.Table, r =>
            {
                root = r;
                view = new ThullaTableView(r, client, new Fakes.Chat(), () => 0, () => { });
                gateway.Deliver(MessageTypes.TableState, 5, state);
            });
            var discard = root.Q<VisualElement>("discard");
            Assert.AreEqual(DisplayStyle.None, discard.resolvedStyle.display, "nothing has been discarded yet");

            var resolved = new TrickResolved { RoundId = "r", Cards = new List<string> { "7S", "3S" }, Escapes = new List<Escape>(), HandCounts = new List<long> { 4, 3, 4, 3 }, Picker = null, NextLeader = 3, LedSuit = Suit.S };
            gateway.Deliver(MessageTypes.TrickResolved, 6, resolved);
            yield return null;
            var count = discard.Q<Label>(className: "discard__count");
            Assert.AreNotEqual(DisplayStyle.None, discard.resolvedStyle.display, "the stack appears");
            Assert.AreEqual("0", count.text, "the two cards are still on the table");

            view.Present(new ServerMessage(MessageTypes.TrickResolved, 6, resolved));
            view.Tick(0.4f);
            var started = Time.realtimeSinceStartup;
            while (count.text != "2" && Time.realtimeSinceStartup - started < 2f)
            {
                view.Tick(Time.deltaTime);
                yield return null;
            }

            Assert.AreEqual("2", count.text, "both cards landed on the stack");
        }

        [UnityTest]
        public IEnumerator TheAskedPlayerSeesTheRequestAndCanKeepPlaying()
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
            });
            var asked = new HandAsked { RoundId = "r", Asker = 3, Target = 0, CardCount = 4, DeadlineTs = 20000 };
            gateway.Deliver(MessageTypes.HandAsked, 6, asked);
            view.Present(new ServerMessage(MessageTypes.HandAsked, 6, asked));
            yield return null;
            var sheet = root.Q<VisualElement>("player-sheet");
            Shown(sheet, "the request");
            Assert.AreEqual("Sana asks for your hand", root.Q<Label>("sheet-title").text);
            StringAssert.Contains("4 cards", root.Q<Label>("sheet-status").text);
            var buttons = root.Q<VisualElement>("sheet-actions").Query<Button>().ToList();
            CollectionAssert.AreEqual(new[] { "Hand over", "Keep playing" }, buttons.Select(b => b.text).ToList());
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = buttons[1];
                buttons[1].SendEvent(click);
            }

            yield return null;
            var sent = gateway.Sent.Find(s => s.Type == MessageTypes.AnswerHand);
            Assert.IsFalse(((AnswerHand)sent.Payload).Accept, "Keep playing refuses");
            Assert.AreEqual(DisplayStyle.None, sheet.resolvedStyle.display);
        }

        [UnityTest]
        public IEnumerator AnEscapedPlayerIsToldAndKeepsWatching()
        {
            VisualElement root = null!;
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            yield return Open(Catalog.Table, r =>
            {
                root = r;
                new ThullaTableView(r, client, new Fakes.Chat(), () => 0, () => { });
                gateway.Deliver(MessageTypes.TableState, 5, Fakes.InRound(5));
            });
            gateway.Deliver(MessageTypes.Escape, 6, new Escape { RoundId = "r", Seat = 0, Rank = 1 });
            yield return null;
            Shown(root.Q<VisualElement>("coach"), "the you're-out note");
            StringAssert.Contains("You're out", root.Q<Label>("coach-text").text);
            Assert.AreEqual("Out · watching", root.Q<VisualElement>("me").Q<Label>(className: "seat__state").text, "the own badge says so");
            Shown(root.Q<Button>("chat"), "chat stays open to an escaped player");
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
        public IEnumerator TheBiggestThullaHighlightsShareInsideTheResults()
        {
            VisualElement root = null!;
            ThullaTableView view = null!;
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            yield return Open(Catalog.Table, r =>
            {
                root = r;
                view = new ThullaTableView(r, client, new Fakes.Chat(), () => 0, () => { });
                view.UseShare(() => { });
                gateway.Deliver(MessageTypes.TableState, 5, Fakes.InRound(5));
                gateway.Deliver(MessageTypes.RoundEnd, 6, new RoundEnd { RoundId = "r", BhabhiSeat = 2, EscapeOrder = new List<long> { 0, 1, 3 }, Points = new List<long>(), Stats = new List<SeatStats>(), Fairness = new Fairness { Seed = "s", Commitment = "c", DeckOrder = new List<string>() } });
                view.OfferBigThulla("r");
            });
            var share = root.Q<Button>("result-share");
            Assert.AreEqual(UiText.ShareBigThulla, share.text);
            Assert.IsTrue(share.ClassListContains("button--secondary"));
            Shown(root.Q<Button>("play-again"), "Play again beside the offer");
            Assert.AreEqual(UiText.RematchHow, root.Q<Label>("result-status").text, "the results say a new round needs everyone");
        }

        [UnityTest]
        public IEnumerator ShareCardDrawsAFeedPostAndAStory()
        {
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            gateway.Deliver(MessageTypes.TableState, 5, Fakes.InRound(5));
            var stats = new List<SeatStats>();
            for (var i = 0; i < 4; i++)
            {
                stats.Add(new SeatStats { CardsPickedUp = i == 2 ? 14 : 3 });
            }

            gateway.Deliver(MessageTypes.RoundEnd, 6, new RoundEnd { RoundId = "r", BhabhiSeat = 2, EscapeOrder = new List<long> { 0, 1, 3 }, Points = new List<long>(), Stats = stats, Fairness = new Fairness { Seed = "s", Commitment = "c", DeckOrder = new List<string>() } });
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            foreach (var ratio in new[] { CardRatio.Feed, CardRatio.Story })
            {
                byte[]? png = null;
                yield return Render(settings, ratio, client.Model, bytes => png = bytes);
                Assert.IsNotNull(png);
                var image = new Texture2D(2, 2);
                Assert.IsTrue(image.LoadImage(png!));
                Assert.AreEqual(CardRenderer.Width, image.width);
                Assert.AreEqual(CardRenderer.HeightOf(ratio), image.height);
                var path = System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, "share-card-" + ratio + ".png");
                System.IO.File.WriteAllBytes(path, png!);
                Debug.Log("share card: " + path);
                Object.Destroy(image);
            }
        }

        private static IEnumerator Render(PanelSettings settings, CardRatio ratio, TableModel model, System.Action<byte[]> done)
        {
            var task = CardRenderer.RenderAsync(settings, ratio, root => ThullaShareCard.Build(root, model, seat => model.Seats[(int)seat].DisplayName ?? "?", "Join with code K7Q2MX on Hulla", ratio == CardRatio.Story));
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            done(awaiter.GetResult());
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
        public IEnumerator SettingsShowTheNotificationSwitchesOnceLoaded()
        {
            VisualElement root = null!;
            var notifications = new NotificationSettingsViewModel(new Fakes.Notifications());
            yield return Open(Catalog.Settings, r =>
            {
                root = r;
                new SettingsView(r, new SettingsModel(new MemoryStore()), () => { }, null, notifications, change => _ = notifications.ChangeAsync(change, default));
                _ = notifications.RefreshAsync(default);
            });
            Assert.AreEqual(16, root.Query<Toggle>().ToList().Count, "nine settings and seven notification switches");
            var online = root.Query<Toggle>().ToList().First(t => t.label == "A friend comes online");
            Assert.IsFalse(online.value, "loaded from the server");
        }

        [UnityTest]
        public IEnumerator LobbyInviteSheetListsFriendsOnlineFirst()
        {
            VisualElement root = null!;
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            LobbyView lobby = null!;
            yield return Open(Catalog.Lobby, r =>
            {
                root = r;
                lobby = new LobbyView(r, client, () => 0, () => { });
                var state = Fakes.InRound(5);
                state.Status = TableStatus.Open;
                state.Round = null;
                gateway.Deliver(MessageTypes.TableState, 5, state);
                lobby.UseInvites(() => Fakes.Done<IReadOnlyList<Hulla.Application.Social.FriendEntry>>(new List<Hulla.Application.Social.FriendEntry>
                {
                    new Hulla.Application.Social.FriendEntry("u9", "Zara", null, false, null, false),
                    new Hulla.Application.Social.FriendEntry("u8", "Omar", null, true, null, false),
                    new Hulla.Application.Social.FriendEntry("u1", "Ayesha", null, true, null, false),
                }), _ => Fakes.Done<string?>(null));
            });
            var invite = root.Q<Button>("invite");
            Shown(invite, "Invite friends in a private room");
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = invite;
                invite.SendEvent(click);
            }

            yield return null;
            var names = root.Q("invite-sheet").Query<Label>(className: "lobby-seat__name").ToList().Select(l => l.text).ToList();
            CollectionAssert.AreEqual(new[] { "Ayesha", "Omar", "Zara · Offline" }, names);
            var chips = root.Q("invite-sheet").Query<Button>(className: "chip").ToList();
            Assert.AreEqual("At the table", chips[0].text, "Ayesha is seated");
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
        public IEnumerator LeaderboardShowsRowsTheResetAndMyRank()
        {
            VisualElement root = null!;
            var model = new LeaderboardViewModel(new Fakes.Boards());
            yield return Open(Catalog.Leaderboard, r =>
            {
                root = r;
                new LeaderboardView(r, model, () => 1_000_000_000_000, "me", () => { }, (_, _) => { });
                _ = model.RefreshAsync(default);
            });
            Assert.AreEqual(2, root.Q<VisualElement>("list").childCount);
            Assert.AreEqual("Resets in 3d 0h. Top 10 win gems.", root.Q<Label>("reset").text);
            Shown(root.Q<VisualElement>("me"), "my rank off the page");
            StringAssert.Contains("khizer (you)", root.Q<VisualElement>("me").Q<Label>(className: "leaderboard__name").text);
        }

        [UnityTest]
        public IEnumerator RankedShowsTheLeagueTheSeasonTheLadderAndTheTop()
        {
            VisualElement root = null!;
            var model = new RankedViewModel(new Fakes.Ranked());
            RankedCard? played = null;
            yield return Open(Catalog.Ranked, r =>
            {
                root = r;
                new RankedView(r, model, () => 1_000_000_000_000, "me", () => { }, card => played = card);
                _ = model.RefreshAsync(default);
            });
            Assert.AreEqual("Gold", root.Q<Label>("league").text);
            Assert.AreEqual("Rating 1287 · #14 this season", root.Q<Label>("rating").text);
            Assert.AreEqual("Promotion protected for 2 more", root.Q<Label>("progress").text);
            Assert.AreEqual("Season 2 ends in 9d 0h", root.Q<Label>("season").text);
            Shown(root.Q<Label>("banner"), "the season-end banner");
            StringAssert.Contains("Silver", root.Q<Label>("banner").text);
            var ladder = root.Q<VisualElement>("ladder").Query<Label>(className: "ranked__step-name").ToList().Select(l => l.text).ToList();
            CollectionAssert.AreEqual(new[] { "Ustad", "Gold", "Bronze" }, ladder);
            StringAssert.Contains("card back", root.Q<VisualElement>("ladder").Query<Label>(className: "ranked__step-detail").First().text);
            Assert.AreEqual(2, root.Q<VisualElement>("top").childCount);
            using (var click = NavigationSubmitEvent.GetPooled())
            {
                click.target = root.Q<Button>("play");
                root.Q<Button>("play").SendEvent(click);
            }

            Assert.IsNotNull(played);
        }

        [UnityTest]
        public IEnumerator RankedPlacementShowsTheProgressInsteadOfALeague()
        {
            VisualElement root = null!;
            var model = new RankedViewModel(new Fakes.Ranked { Placed = false });
            yield return Open(Catalog.Ranked, r =>
            {
                root = r;
                new RankedView(r, model, () => 1_000_000_000_000, "me", () => { }, _ => { });
                _ = model.RefreshAsync(default);
            });
            Assert.AreEqual("Unranked", root.Q<Label>("league").text);
            Assert.AreEqual("Placement 3 of 10. Play 7 more to get your league.", root.Q<Label>("progress").text);
            Shown(root.Q<VisualElement>("bar"), "the placement bar");
        }

        [UnityTest]
        public IEnumerator PhoneTakesANumberThenACodeThenOffersTheSwitch()
        {
            VisualElement root = null!;
            var model = new PhoneViewModel(new Fakes.Phone());
            yield return Open(Catalog.Phone, r =>
            {
                root = r;
                new Hulla.Presentation.Screens.Phone.PhoneView(r, model, () => { }, new Hulla.Presentation.Screens.Phone.PhoneView.PhoneActions(
                    n => _ = model.SendAsync(n, default), c => _ = model.VerifyAsync(c, default), () => { }, () => _ = model.SwitchAsync(default), model.StartOver));
                _ = model.RefreshAsync(default);
            });
            Shown(root.Q<VisualElement>("number-step"), "the number field");
            Assert.AreEqual("Send code", root.Q<Button>("send").text);
            _ = model.SendAsync("+923001234567", default);
            yield return null;
            Shown(root.Q<VisualElement>("code-step"), "the code field");
            Assert.AreEqual("We sent a code to +92 ••• 67.", root.Q<Label>("sent").text);
            _ = model.VerifyAsync("234567", default);
            yield return null;
            Shown(root.Q<VisualElement>("offer-step"), "the switch offer");
            Assert.AreEqual(DisplayStyle.None, root.Q<VisualElement>("code-step").resolvedStyle.display);
        }

        [UnityTest]
        public IEnumerator AchievementsShowCategoriesProgressAndRewards()
        {
            VisualElement root = null!;
            var model = new AchievementsViewModel(new Fakes.Achievements());
            yield return Open(Catalog.Achievements, r =>
            {
                root = r;
                new AchievementsView(r, model, () => { });
                _ = model.RefreshAsync(default);
            });
            Assert.AreEqual("1 of 3 unlocked", root.Q<Label>("status").text);
            var labels = root.Q<VisualElement>("list").Query<Label>().ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(labels, "Play");
            CollectionAssert.Contains(labels, "Friends");
            CollectionAssert.Contains(labels, "37/1,000");
            CollectionAssert.Contains(labels, "Give 1000 thullas · 60 gems + the title \"Thulla baaz\"");
        }

        [UnityTest]
        public IEnumerator ShopShowsTheSpotlightPricesAndEquip()
        {
            VisualElement root = null!;
            var model = new ShopViewModel(new Fakes.Shop());
            yield return Open(Catalog.Shop, r =>
            {
                root = r;
                new ShopView(r, model, () => { }, _ => { }, _ => { });
                model.SetBalances(1200, 50);
                _ = model.RefreshAsync(default);
            });
            Assert.AreEqual("1,200 coins · 50 gems", root.Q<Label>("balances").text);
            var texts = root.Q<VisualElement>("list").Query<TextElement>().ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(texts, "This week");
            CollectionAssert.Contains(texts, "Marble");
            CollectionAssert.Contains(texts, "Equipped");
            CollectionAssert.Contains(texts, "1,000 coins");
            CollectionAssert.Contains(texts, "Equip");
        }

        [UnityTest]
        public IEnumerator SeasonPassShowsTiersClaimsAndTheLockedTrack()
        {
            VisualElement root = null!;
            var model = new PassViewModel(new Fakes.Pass());
            yield return Open(Catalog.Pass, r =>
            {
                root = r;
                new PassView(r, model, () => 1_000_000_000_000, () => { }, (_, _) => { });
                _ = model.RefreshAsync(default);
            });
            var texts = root.Q<VisualElement>("list").Query<TextElement>().ToList().Select(l => l.text).ToList();
            CollectionAssert.Contains(texts, "Basant 2026");
            CollectionAssert.Contains(texts, "Tier 2 of 4 · 300/1,000 XP · ends in 20d 0h");
            CollectionAssert.Contains(texts, "Done", "tier 1 free is claimed");
            CollectionAssert.Contains(texts, "Claim", "tier 2 free is claimable");
            CollectionAssert.Contains(texts, "Locked", "premium without the pass");
            CollectionAssert.Contains(texts, "Kites");
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
