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
    /// <summary>Every main screen bound to fakes, for the checks that lay each one out (pseudo-locales, accessibility).</summary>
    internal static class ScreenCases
    {
        public sealed class Case
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

        /// <summary>The classes the shell puts on its root for a table theme (ThemeClasses.Apply).</summary>
        public static void Skin(VisualElement root, Hulla.Application.Settings.TableSkin skin)
        {
            root.AddToClassList(Hulla.Presentation.Theme.ThemeClasses.Skinned);
            root.AddToClassList(Hulla.Presentation.Theme.ThemeClasses.ClassOf(skin));
        }

        public static IEnumerable<Case> All()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");
            yield return new Case("home", catalog.Home, r =>
            {
                var friends = new HomeFriendsViewModel(new Fakes.Social());
                var progress = new ProgressViewModel(new Fakes.Progress());
                new HomeView(r, new HomeViewModel(new Fakes.Session(), new Fakes.Gateway()) { LastName = "Khizer" }, null, (_, _) => { }, (_, _, _) => { }, (_, _, _) => { }, () => { }, (_, _) => { }, (_, _) => { }, friends, () => { }, TutorialText.HowToPlay, progress: progress);
                _ = progress.RefreshAsync(0, default);
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
