#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hulla.Application.Services;
using Hulla.Application.Settings;
using Hulla.Application.Table;
using Hulla.Application.ViewModels;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Screens.Home;
using Hulla.Presentation.Screens.Lobby;
using Hulla.Presentation.Share;
using Hulla.Presentation.Theme;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// Draws Home, the lobby, the table at the thulla moment and the results to PNGs for eyeballing
    /// the look, once per table theme; explicit, so it only runs on request:
    /// <c>-testFilter Hulla.Screens.Tests.ScreenSnapshots</c>, files in HULLA_SNAPSHOT_DIR.
    /// </summary>
    public sealed class ScreenSnapshots
    {
        private static ScreenCatalog Catalog => AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");

        private static PanelSettings Settings => AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");

        private static string Dir => System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_DIR") ?? "Temp";

        public static IEnumerable<TableSkin> Skins => (TableSkin[])System.Enum.GetValues(typeof(TableSkin));

        private static IEnumerator Snap(string name, TableSkin skin, VisualTreeAsset? screen, System.Action<VisualElement> bind, bool nav = false) =>
            Snap(name, skin, screen, bind, nav, CardRenderer.Width, CardRenderer.HeightOf(CardRatio.Story), null);

        /// <summary>At a canvas size in panel units, with that size's class and, when given, safe-area padding.</summary>
        private static IEnumerator Snap(string name, TableSkin skin, VisualTreeAsset? screen, System.Action<VisualElement> bind, bool nav, int width, int height, Insets? insets)
        {
            name += "-" + skin.ToString().ToLowerInvariant();
            var task = CardRenderer.RenderAsync(Settings, width, height, root =>
            {
                if (insets != null)
                {
                    ScreenLayout.Apply(root, ScreenLayout.ClassOf(width), insets.Value);
                }

                root.AddToClassList(ThemeClasses.Skinned);
                root.AddToClassList(ThemeClasses.ClassOf(skin));
                var instance = screen!.Instantiate();
                instance.style.flexGrow = 1;
                root.Add(instance);
                bind(instance);
                var layer = instance.Q(className: "screen");
                AmbientLayer.Decorate(layer, skin, reduceMotion: true);
                (layer?.Q(AmbientLayer.ScreenName) as AmbientLayer)?.Advance(1.3f);
                (layer?.Q(AmbientLayer.TableName) as AmbientLayer)?.Advance(1.3f);
                if (nav)
                {
                    var bar = new NavBar(Presentation.Text.UiText.NavLabel);
                    bar.Select(NavTab.Home);
                    bar.SetBadge(NavTab.Friends, 2);
                    root.Add(bar);
                }
            });
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted)
            {
                yield return null;
            }

            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), awaiter.GetResult());
        }

        [UnityTest, Explicit]
        public IEnumerator Home([ValueSource(nameof(Skins))] TableSkin skin)
        {
            var friends = new HomeFriendsViewModel(new Fakes.Social());
            yield return Snap("home", skin, Catalog.Home, r =>
            {
                new HomeView(r, new HomeViewModel(new Fakes.Session(), new Fakes.Gateway()) { LastName = "Khizer" }, null, (_, _) => { }, (_, _, _) => { }, (_, _, _) => { }, () => { }, (_, _) => { }, (_, _) => { }, friends, () => { }, "How to play");
                _ = friends.RefreshAsync(default);
                var daily = new DailyRewardViewModel(new Fakes.Daily());
                new DailyRewardCard(r.Q<VisualElement>("daily"), daily, () => { });
                _ = daily.RefreshAsync(0, default);
            }, nav: true);
        }

        /// <summary>A phone, a 4:3 tablet in portrait and in landscape, with the brief's largest insets for each.</summary>
        public static IEnumerable<(int Width, int Height, Insets Insets)> Sizes => new[]
        {
            (1080, 1920, new Insets(150, 0, 100, 0)),
            (1440, 1920, new Insets(70, 0, 60, 0)),
            (1920, 1440, new Insets(70, 130, 60, 0)),
        };

        [UnityTest, Explicit]
        public IEnumerator AtEachSize()
        {
            foreach (var (width, height, insets) in Sizes)
            {
                var suffix = "-" + width + "x" + height;
                yield return Snap("size-home" + suffix, TableSkin.Club, Catalog.Home, r =>
                {
                    new HomeView(r, new HomeViewModel(new Fakes.Session(), new Fakes.Gateway()) { LastName = "Khizer" }, null, (_, _) => { }, (_, _, _) => { }, (_, _, _) => { }, () => { }, (_, _) => { }, (_, _) => { }, new HomeFriendsViewModel(new Fakes.Social()), () => { }, "How to play");
                }, true, width, height, insets);

                var gateway = new Fakes.Gateway();
                var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
                yield return Snap("size-table" + suffix, TableSkin.Club, Catalog.Table, r =>
                {
                    new ThullaTableView(r, client, new Fakes.Chat(), () => 5000, () => { });
                    var state = Fakes.InRound(5);
                    state.Round!.Seats[0].Hand = new List<string> { "AS", "KS", "9S", "QH", "8H", "2H", "JD", "9D", "5C", "3C", "TC" };
                    state.Round.Seats[0].HandCount = 11;
                    gateway.Deliver(MessageTypes.TableState, 5, state);
                    gateway.Deliver(MessageTypes.TurnStart, 5, new TurnStart { RoundId = "r", Seat = 0, Legal = new List<string> { "QH", "8H", "2H" }, DeadlineTs = 15000 });
                }, false, width, height, insets);
            }
        }

        [UnityTest, Explicit]
        public IEnumerator Lobby([ValueSource(nameof(Skins))] TableSkin skin)
        {
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            yield return Snap("lobby", skin, Catalog.Lobby, r =>
            {
                new LobbyView(r, client, () => 0, () => { }, _ => true, null, () => "Standard · 20 s turns · loser: Bhabhi", () => { });
                var state = Fakes.InRound(3);
                state.Status = TableStatus.Open;
                state.Round = null;
                state.Seats[3] = new Seat { SeatSeat = 3, Connected = false };
                state.Seats[1].Ready = true;
                gateway.Deliver(MessageTypes.TableState, 3, state);
            });
        }

        [UnityTest, Explicit]
        public IEnumerator TableAtTheThulla([ValueSource(nameof(Skins))] TableSkin skin)
        {
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            yield return Snap("table", skin, Catalog.Table, r =>
            {
                var view = new ThullaTableView(r, client, new Fakes.Chat(), () => 5000, () => { });
                var state = Fakes.InRound(5);
                state.Round!.Seats[0].Hand = new List<string> { "AS", "KS", "9S", "QH", "8H", "2H", "JD", "9D", "5C", "3C", "TC" };
                state.Round.Seats[0].HandCount = 11;
                state.Round.Trick.Plays = new List<Play> { new Play { Seat = 2, Card = "KH" }, new Play { Seat = 3, Card = "6H" } };
                state.Round.TurnSeat = 1;
                gateway.Deliver(MessageTypes.TableState, 5, state);
                gateway.Deliver(MessageTypes.TurnStart, 5, new TurnStart { RoundId = "r", Seat = 0, Legal = new List<string> { "QH", "8H", "2H" }, DeadlineTs = 12000 });
                var thulla = new ServerMessage(MessageTypes.MoveApplied, 6, new MoveApplied { RoundId = "r", Seat = 1, Move = new Move { Type = TypeEnum.PlayCard, Card = "7C" }, TrickIndex = 2, Thulla = true });
                gateway.Deliver(MessageTypes.MoveApplied, 6, thulla.Payload);
                view.Present(thulla);
                view.Tick(0.3f);
            });
        }

        [UnityTest, Explicit]
        public IEnumerator TableOnMyTurn([ValueSource(nameof(Skins))] TableSkin skin)
        {
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            yield return Snap("table-turn", skin, Catalog.Table, r =>
            {
                new ThullaTableView(r, client, new Fakes.Chat(), () => 5000, () => { });
                var state = Fakes.InRound(5);
                state.Round!.Seats[0].Hand = new List<string> { "AS", "KS", "9S", "QH", "8H", "2H", "JD", "9D", "5C", "3C", "TC" };
                state.Round.Seats[0].HandCount = 11;
                state.Round.Trick.Plays = new List<Play> { new Play { Seat = 2, Card = "KH" }, new Play { Seat = 3, Card = "6H" } };
                gateway.Deliver(MessageTypes.TableState, 5, state);
                gateway.Deliver(MessageTypes.TurnStart, 5, new TurnStart { RoundId = "r", Seat = 0, Legal = new List<string> { "QH", "8H", "2H" }, DeadlineTs = 15000 });
            });
        }

        [UnityTest, Explicit]
        public IEnumerator TableOfSix([ValueSource(nameof(Skins))] TableSkin skin)
        {
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            yield return Snap("table-six", skin, Catalog.Table, r =>
            {
                new ThullaTableView(r, client, new Fakes.Chat(), () => 5000, () => { });
                var state = Fakes.InRound(5, 6);
                state.Round!.Seats[0].Hand = new List<string> { "AS", "9S", "QH", "8H", "JD", "5C", "TC" };
                state.Round.Seats[0].HandCount = 7;
                state.Round.Trick.Plays = new List<Play> { new Play { Seat = 3, Card = "KH" }, new Play { Seat = 4, Card = "6H" }, new Play { Seat = 5, Card = "AH" } };
                state.Round.TurnSeat = 0;
                gateway.Deliver(MessageTypes.TableState, 5, state);
            });
        }

        [UnityTest, Explicit]
        public IEnumerator SettingsWithThemes([ValueSource(nameof(Skins))] TableSkin skin)
        {
            yield return Snap("settings", skin, Catalog.Settings, r =>
            {
                var settings = new SettingsModel(new Store()) { Skin = skin };
                new Presentation.Screens.Settings.SettingsView(r, settings, () => { });
            });
        }

        private sealed class Store : ISettingsStore
        {
            private readonly Dictionary<string, int> _values = new Dictionary<string, int>();

            public int GetInt(string key, int fallback) => _values.TryGetValue(key, out var v) ? v : fallback;

            public void SetInt(string key, int value) => _values[key] = value;

            public void Save()
            {
            }
        }

        [UnityTest, Explicit]
        public IEnumerator Results([ValueSource(nameof(Skins))] TableSkin skin)
        {
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            yield return Snap("results", skin, Catalog.Table, r =>
            {
                var view = new ThullaTableView(r, client, new Fakes.Chat(), () => 1000, () => { });
                view.UseShare(() => { });
                view.UseFairness(_ => { });
                view.UseChangeRules(() => { });
                view.SetFriendActions(_ => true, _ => { });
                gateway.Deliver(MessageTypes.TableState, 5, Fakes.InRound(5));
                var stats = new List<SeatStats>();
                for (var i = 0; i < 4; i++)
                {
                    stats.Add(new SeatStats { CardsPickedUp = i == 2 ? 14 : 3, ThullasGiven = i == 1 ? 4 : 1 });
                }

                gateway.Deliver(MessageTypes.RoundEnd, 6, new RoundEnd { RoundId = "r", BhabhiSeat = 2, EscapeOrder = new List<long> { 3, 0, 1 }, Points = new List<long>(), Stats = stats, Fairness = new Fairness { Seed = "s", Commitment = "c", DeckOrder = new List<string>() } });
                view.ShowReward("+120 XP · +40 coins");
                view.OfferBigThulla("r");
            });
        }

        [UnityTest, Explicit]
        public IEnumerator Dialog([ValueSource(nameof(Skins))] TableSkin skin)
        {
            yield return Snap("dialog", skin, Catalog.Home, r => r.Add(ShellView.Dialog(
                "Ayesha wants to add you as a friend after your game together on Hulla. Add them?",
                Presentation.Text.UiText.AddFriend,
                Presentation.Text.UiText.NotNow,
                () => { },
                () => { })));
        }
    }
}
#endif
