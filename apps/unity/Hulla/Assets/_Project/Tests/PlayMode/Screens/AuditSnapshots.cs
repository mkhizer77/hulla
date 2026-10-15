#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Hulla.Application.Services;
using Hulla.Application.Settings;
using Hulla.Application.Table;
using Hulla.Games.Thulla;
using Hulla.Protocol;
using System.IO;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Share;
using Hulla.Presentation.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// Every main screen in English and the default theme, with the bottom navigation where the app shows
    /// it, as PNGs for a design review; explicit: <c>-testFilter Hulla.Screens.Tests.AuditSnapshots</c>.
    /// HULLA_SNAPSHOT_SKIN (club, haveli, dhaba, mehndi, adda) picks another theme.
    /// </summary>
    public sealed class AuditSnapshots
    {
        private static readonly string[] WithNav = { "home", "friends", "profile", "shop", "pass", "leaderboard", "ranked", "missions", "achievements", "inventory" };

        [UnityTest, Explicit]
        public IEnumerator EveryScreen()
        {
            Strings.Use(Strings.Fallback);
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var dir = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_DIR") ?? "Temp";
            Directory.CreateDirectory(dir);
            var skin = SettingsModel.DefaultSkin;
            var named = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_SKIN");
            if (named != null && System.Enum.TryParse(named, true, out TableSkin chosen))
            {
                skin = chosen;
            }

            foreach (var screen in ScreenCases.All())
            {
                var task = CardRenderer.RenderAsync(settings, CardRatio.Story, root =>
                {
                    ScreenCases.Skin(root, skin);
                    var instance = screen.Screen!.Instantiate();
                    instance.style.flexGrow = 1;
                    root.Add(instance);
                    screen.Bind(instance);
                    var face = instance.Q(className: "screen");
                    if (face != null)
                    {
                        Hulla.Presentation.Theme.AmbientLayer.Decorate(face, skin, false);
                    }
                    if (System.Array.IndexOf(WithNav, screen.Name) >= 0)
                    {
                        var bar = new NavBar(UiText.NavLabel);
                        bar.Select(screen.Name == "home" ? NavTab.Home : screen.Name == "friends" ? NavTab.Friends : NavTab.Inventory);
                        root.Add(bar);
                    }
                });
                var awaiter = task.GetAwaiter();
                while (!awaiter.IsCompleted)
                {
                    yield return null;
                }

                File.WriteAllBytes(Path.Combine(dir, "audit-" + screen.Name + ".png"), awaiter.GetResult());
            }
        }

        /// <summary>The table, mid-round, in every theme, with a table cosmetic equipped and the hand full.</summary>
        [UnityTest, Explicit]
        public IEnumerator TableInEveryTheme()
        {
            Strings.Use(Strings.Fallback);
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");
            var dir = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_DIR") ?? "Temp";
            Directory.CreateDirectory(dir);
            foreach (TableSkin skin in System.Enum.GetValues(typeof(TableSkin)))
            {
                foreach (var myTurn in new[] { true, false })
                {
                    var task = CardRenderer.RenderAsync(settings, CardRatio.Story, root =>
                    {
                        ScreenCases.Skin(root, skin);
                        var instance = catalog.Table!.Instantiate();
                        instance.style.flexGrow = 1;
                        root.Add(instance);
                        var gateway = new Fakes.Gateway();
                        var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
                        new ThullaTableView(instance, client, new Fakes.Chat(), () => 0, () => { });
                        var state = Fakes.InRound(5);
                        state.Seats[0].Cosmetics = new Cosmetics { Table = "table_charpai" };
                        state.Round!.Seats[0].Hand = new List<string> { "3S", "8S", "TH", "QH", "AH", "3C", "4C", "5C", "JC", "2D", "6D", "9D", "KD" };
                        state.Round.Seats[0].HandCount = 13;
                        state.Round.TurnSeat = myTurn ? 0 : 2;
                        state.Round.DiscardCount = 12;
                        gateway.Deliver(MessageTypes.TableState, 5, state);
                        gateway.Deliver(MessageTypes.TurnStart, 5, new TurnStart { RoundId = "r", Seat = myTurn ? 0 : 2, Legal = myTurn ? new List<string> { "3S", "8S" } : new List<string>(), DeadlineTs = 1 });
                        var face = instance.Q(className: "screen") ?? instance.Q(className: "table");
                        if (face != null)
                        {
                            Hulla.Presentation.Theme.AmbientLayer.Decorate(face, skin, false);
                        }
                    });
                    var awaiter = task.GetAwaiter();
                    while (!awaiter.IsCompleted)
                    {
                        yield return null;
                    }

                    File.WriteAllBytes(Path.Combine(dir, "theme-" + skin.ToString().ToLowerInvariant() + (myTurn ? "-mine" : "-wait") + ".png"), awaiter.GetResult());
                }
            }
        }
    }
}
#endif
