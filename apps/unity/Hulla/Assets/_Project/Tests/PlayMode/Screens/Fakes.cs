using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.Social;
using Hulla.Domain;
using Hulla.Protocol;
using UnityEngine;

namespace Hulla.Screens.Tests
{
    /// <summary>Stand-ins for the services the screens read, so each screen opens without a server.</summary>
    internal static class Fakes
    {
        public static Awaitable<T> Done<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }

        public sealed class Gateway : ITableGateway
        {
            public readonly List<(string Type, object Payload)> Sent = new List<(string Type, object Payload)>();

            public event Action<ServerMessage>? MessageReceived;

            public event Action? Disconnected
            {
                add { }
                remove { }
            }

            public void Deliver(string type, long seq, object payload) => MessageReceived?.Invoke(new ServerMessage(type, seq, payload));

            public Awaitable<Result<TableTicket>> CreateTableAsync(CancellationToken cancellation) => Done(Result<TableTicket>.Fail("fake"));

            public Awaitable<Result<TableTicket>> JoinByCodeAsync(string code, CancellationToken cancellation) => Done(Result<TableTicket>.Fail("fake"));

            public Awaitable<Result<bool>> EnterAsync(TableTicket ticket, CancellationToken cancellation) => Done(Result<bool>.Ok(true));

            public void Send(string type, object payload) => Sent.Add((type, payload));

            public Awaitable LeaveAsync()
            {
                var source = new AwaitableCompletionSource();
                source.SetResult();
                return source.Awaitable;
            }
        }

        public sealed class Session : ISessionService
        {
            public PlayerSession? Current => null;

            public event Action<string, string>? Notified
            {
                add { }
                remove { }
            }

            public Awaitable<Result<PlayerSession>> SignInAsync(string displayName, CancellationToken cancellation) => Done(Result<PlayerSession>.Fail("fake"));

            public Awaitable<Result<bool>> ReconnectAsync(CancellationToken cancellation) => Done(Result<bool>.Fail("fake"));
        }

        public sealed class Social : ISocialService
        {
            public Awaitable<Result<Profile>> MyProfileAsync(CancellationToken c) => Done(Result<Profile>.Ok(new Profile { DisplayName = "Khizer", Handle = "khizer_k", Bio = "", FriendCode = "K7Q2MXRP", Stats = new ProfileStats { Rounds = 3 } }));

            public Awaitable<Result<Profile>> UpdateProfileAsync(ProfileUpdate u, CancellationToken c) => MyProfileAsync(c);

            public Awaitable<Result<IReadOnlyList<FriendEntry>>> FriendsAsync(CancellationToken c) => Done(Result<IReadOnlyList<FriendEntry>>.Ok(new List<FriendEntry>
            {
                new FriendEntry("1", "Ayesha", "ayesha", true, FriendState.Friend, true),
                new FriendEntry("2", "Sana", "sana", false, FriendState.Received, false),
            }));

            public Awaitable<Result<IReadOnlyList<FriendEntry>>> RecentAsync(CancellationToken c) => Done(Result<IReadOnlyList<FriendEntry>>.Ok(new List<FriendEntry> { new FriendEntry("5", "Noor", "noor", false, null, false, 3) }));

            public Awaitable<Result<bool>> AddByCodeAsync(string f, CancellationToken c) => Done(Result<bool>.Ok(true));

            public Awaitable<Result<bool>> AddAsync(string u, CancellationToken c) => Done(Result<bool>.Ok(true));

            public Awaitable<Result<bool>> RemoveAsync(string u, CancellationToken c) => Done(Result<bool>.Ok(true));

            public Awaitable<Result<bool>> BlockAsync(string u, CancellationToken c, bool blocked = true) => Done(Result<bool>.Ok(true));

            public Awaitable<Result<bool>> ReportAsync(string u, string r, string? t, string? round, CancellationToken c) => Done(Result<bool>.Ok(true));

            public Awaitable<Result<bool>> SetFavouriteAsync(string u, bool p, CancellationToken c) => Done(Result<bool>.Ok(true));

            public Awaitable<Result<IReadOnlyList<FriendPresence>>> PresenceAsync(CancellationToken c) => Done(Result<IReadOnlyList<FriendPresence>>.Ok(new List<FriendPresence> { new FriendPresence("1", "Ayesha", PresenceState.Lobby, true, "K7Q2MX", 2, true) }));

            public Awaitable<Result<bool>> SetAwayAsync(bool a, CancellationToken c) => Done(Result<bool>.Ok(true));

            public Awaitable<Result<IReadOnlyList<string>>> MutesAsync(CancellationToken c) => Done(Result<IReadOnlyList<string>>.Ok(new List<string>()));

            public Awaitable<Result<IReadOnlyList<string>>> SetMuteAsync(string u, bool m, CancellationToken c) => Done(Result<IReadOnlyList<string>>.Ok(new List<string>()));
        }

        public sealed class Matchmaker : IMatchmaker
        {
            public event Action<TableTicket>? Matched
            {
                add { }
                remove { }
            }

            public Awaitable<Result<bool>> SearchAsync(int seats, bool anySize, CancellationToken cancellation) => Done(Result<bool>.Ok(true));

            public Awaitable CancelAsync()
            {
                var source = new AwaitableCompletionSource();
                source.SetResult();
                return source.Awaitable;
            }
        }

        public sealed class Daily : Hulla.Application.Progress.IDailyRewardService
        {
            public Awaitable<Result<Hulla.Application.Progress.DailyRewardStatus>> StatusAsync(int tz, CancellationToken c) =>
                Done(Result<Hulla.Application.Progress.DailyRewardStatus>.Ok(new Hulla.Application.Progress.DailyRewardStatus(true, new Hulla.Application.Progress.DailyRewardDay(4, 80, 2), new Hulla.Application.Progress.DailyRewardDay(5, 90, 0))));

            public Awaitable<Result<Hulla.Application.Progress.DailyRewardStatus>> ClaimAsync(CancellationToken c) =>
                Done(Result<Hulla.Application.Progress.DailyRewardStatus>.Ok(new Hulla.Application.Progress.DailyRewardStatus(false, new Hulla.Application.Progress.DailyRewardDay(4, 80, 2), new Hulla.Application.Progress.DailyRewardDay(5, 90, 0))));
        }

        public sealed class Boards : Hulla.Application.Progress.ILeaderboardService
        {
            public Awaitable<Result<Hulla.Application.Progress.LeaderboardPage>> BoardAsync(Hulla.Application.Progress.LeaderboardPeriod period, bool friends, CancellationToken c) =>
                Done(Result<Hulla.Application.Progress.LeaderboardPage>.Ok(new Hulla.Application.Progress.LeaderboardPage(
                    new List<Hulla.Application.Progress.LeaderboardRow>
                    {
                        new Hulla.Application.Progress.LeaderboardRow(1, "u1", "ayesha", 120, 9),
                        new Hulla.Application.Progress.LeaderboardRow(2, "u2", "bilal", 90, 7),
                    },
                    new Hulla.Application.Progress.LeaderboardRow(57, "me", "khizer", 12, 2),
                    1_000_000_000_000 + 3L * 86_400_000)));
        }

        public sealed class Achievements : Hulla.Application.Progress.IAchievementsService
        {
            public Awaitable<Result<(IReadOnlyList<Hulla.Application.Progress.AchievementInfo> Achievements, IReadOnlyList<string> UnlockedNow)>> AchievementsAsync(CancellationToken c) =>
                Done(Result<(IReadOnlyList<Hulla.Application.Progress.AchievementInfo> Achievements, IReadOnlyList<string> UnlockedNow)>.Ok((new List<Hulla.Application.Progress.AchievementInfo>
                {
                    new Hulla.Application.Progress.AchievementInfo("first_thulla", "play", "thullas_given", 1, 1, 1_000, 5, null),
                    new Hulla.Application.Progress.AchievementInfo("thullas_1000", "play", "thullas_given", 1000, 37, null, 60, "thulla_baaz"),
                    new Hulla.Application.Progress.AchievementInfo("first_friend", "social", "friends", 1, 0, null, 5, null),
                }, new List<string>())));
        }

        public sealed class Shop : Hulla.Application.Economy.IShopService
        {
            public Awaitable<Result<Hulla.Application.Economy.ShopCatalog>> CatalogAsync(CancellationToken c) =>
                Done(Result<Hulla.Application.Economy.ShopCatalog>.Ok(new Hulla.Application.Economy.ShopCatalog(new List<Hulla.Application.Economy.ShopItem>
                {
                    new Hulla.Application.Economy.ShopItem("back_classic_blue", "cardBack", "common", 0, 0, true, true, null),
                    new Hulla.Application.Economy.ShopItem("back_truck_art", "cardBack", "rare", 1000, 0, false, false, null),
                    new Hulla.Application.Economy.ShopItem("back_mughal", "cardBack", "epic", 0, 400, true, false, null),
                    new Hulla.Application.Economy.ShopItem("table_marble", "table", "rare", 4000, 0, false, false, null),
                }, new List<string> { "table_marble" })));

            public Awaitable<Result<(long Coins, long Gems)>> PurchaseAsync(string itemId, CancellationToken c) => Done(Result<(long Coins, long Gems)>.Ok((0L, 0L)));

            public Awaitable<Result<bool>> EquipAsync(string itemId, CancellationToken c) => Done(Result<bool>.Ok(true));
        }

        public sealed class Missions : Hulla.Application.Progress.IMissionsService
        {
            public Awaitable<Result<(IReadOnlyList<Hulla.Application.Progress.MissionInfo> Missions, bool RerollAvailable)>> MissionsAsync(int tz, CancellationToken c) =>
                Done(Result<(IReadOnlyList<Hulla.Application.Progress.MissionInfo> Missions, bool RerollAvailable)>.Ok((new List<Hulla.Application.Progress.MissionInfo>
                {
                    new Hulla.Application.Progress.MissionInfo("d1", "thullas_given", 3, 3, false, true, 60, 0, true),
                    new Hulla.Application.Progress.MissionInfo("d2", "escapes", 2, 0, false, false, 60, 0, true),
                    new Hulla.Application.Progress.MissionInfo("w1", "rounds_played", 20, 4, false, false, 300, 5, false),
                }, true)));

            public Awaitable<Result<bool>> ClaimAsync(string id, CancellationToken c) => Done(Result<bool>.Ok(true));

            public Awaitable<Result<bool>> RerollAsync(string id, CancellationToken c) => Done(Result<bool>.Ok(true));
        }

        public sealed class Chat : IChatCatalog
        {
            public IReadOnlyList<ChatPhrase> Phrases { get; } = new[] { new ChatPhrase("T01", "THULLA!", "Thulla!", "thulla") };

            public IReadOnlyList<string> Emotes { get; } = new[] { "chai" };

            public string? TextFor(string phraseId) => phraseId == "T01" ? "THULLA!" : null;

            public Awaitable LoadAsync(CancellationToken cancellation)
            {
                var source = new AwaitableCompletionSource();
                source.SetResult();
                return source.Awaitable;
            }
        }

        /// <summary>A four-seat table in round with the player at seat 0 holding a few cards.</summary>
        public static TableState InRound(long seq)
        {
            var names = new[] { "Khizer", "Ayesha", "Bilal", "Sana" };
            var seats = new List<Seat>();
            var views = new List<SeatView>();
            for (var i = 0; i < 4; i++)
            {
                seats.Add(new Seat { SeatSeat = i, PlayerId = i == 0 ? "me" : "u" + i, DisplayName = names[i], Connected = true, Ready = true });
                views.Add(new SeatView { HandCount = 4, Hand = i == 0 ? new List<string> { "AS", "KS", "2H", "9D" } : null });
            }

            return new TableState
            {
                Code = "K7Q2MX",
                Mode = TableMode.Private,
                Status = TableStatus.InRound,
                HostSeat = 0,
                LastSeq = seq,
                Seats = seats,
                Spectators = new List<Spectator>(),
                Rules = new RulesSelection { GameId = GameId.Thulla },
                Round = new View { Phase = Phase.Play, SeatCount = 4, Seats = views, EscapeOrder = new List<long>(), Trick = new Trick { Plays = new List<Play> { new Play { Seat = 3, Card = "7S" } } }, TurnSeat = 0 },
            };
        }
    }
}
