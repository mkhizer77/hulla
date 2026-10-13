#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Games.Thulla;
using Hulla.Presentation.Navigation;
using Hulla.Presentation.Share;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hulla.Screens.Tests
{
    /// <summary>
    /// Draws a five-seat table mid-round to a PNG for eyeballing layout changes; explicit, so it only
    /// runs on request: <c>-testFilter Hulla.Screens.Tests.TableSnapshot</c>, path in HULLA_SNAPSHOT_OUT.
    /// </summary>
    public sealed class TableSnapshot
    {
        [UnityTest, Explicit]
        public IEnumerator RenderTable()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ScreenCatalog>("Assets/Settings/UI/ScreenCatalog.asset");
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Settings/UI/PanelSettings.asset");
            var gateway = new Fakes.Gateway();
            var client = new TableClient(gateway, new TableTicket("m", "K7Q2MX", "t_K7Q2MX"), "me", 1);
            var names = new[] { "Karachi Tiger 81", "Ali", "Sara", "Noor", "Bilal" };
            var seats = new List<Seat>();
            var views = new List<SeatView>();
            for (var i = 0; i < 5; i++)
            {
                seats.Add(new Seat { SeatSeat = i, PlayerId = i == 0 ? "me" : "u" + i, DisplayName = names[i], Connected = i != 3, AutoPlay = i == 3, Ready = true });
                views.Add(new SeatView { HandCount = 9 + i, Hand = i == 0 ? new List<string> { "AS", "KS", "9S", "4S", "QH", "8H", "2H", "JD", "9D", "5C", "3C" } : null });
            }

            var state = new TableState
            {
                Code = "K7Q2MX", Mode = TableMode.Private, Status = TableStatus.InRound, HostSeat = 0, LastSeq = 5,
                Seats = seats, Spectators = new List<Spectator>(), Rules = new RulesSelection { GameId = GameId.Thulla },
                Round = new View { Phase = Phase.Play, SeatCount = 5, Seats = views, EscapeOrder = new List<long>(), LeaderSeat = 2, DiscardCount = 11,
                    Trick = new Trick { Plays = new List<Play> { new Play { Seat = 2, Card = "KH" }, new Play { Seat = 3, Card = "6H" }, new Play { Seat = 4, Card = "TH" } } }, TurnSeat = 0 },
            };
            // HULLA_SNAPSHOT_SIZE as WIDTHxHEIGHT in panel units; 1080 wide is every phone since the UI matches width.
            var size = (System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_SIZE") ?? "1080x2400").Split('x');
            var task = CardRenderer.RenderAsync(settings, int.Parse(size[0]), int.Parse(size[1]), root =>
            {
                var screen = catalog.Table!.Instantiate();
                screen.style.flexGrow = 1;
                root.Add(screen);
                new ThullaTableView(screen, client, new Fakes.Chat(), () => 0, () => { });
                gateway.Deliver(MessageTypes.TableState, 5, state);
                gateway.Deliver(MessageTypes.TurnStart, 6, new TurnStart { RoundId = "r", Seat = 0, Legal = new List<string> { "QH", "8H", "2H" }, DeadlineTs = 20000 });
            });
            var awaiter = task.GetAwaiter();
            while (!awaiter.IsCompleted) yield return null;
            var path = System.Environment.GetEnvironmentVariable("HULLA_SNAPSHOT_OUT") ?? "Temp/table.png";
            File.WriteAllBytes(path, awaiter.GetResult());
            Assert.Pass(path);
        }
    }
}
#endif
