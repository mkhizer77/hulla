using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.Social;
using Hulla.Application.ViewModels;
using Hulla.Infrastructure.Content;
using Hulla.Infrastructure.Nakama;
using Hulla.Infrastructure.Settings;
using Hulla.Application.Settings;

namespace Hulla.Composition
{
    /// <summary>
    /// Lives for the whole run: configuration, the server connection, sign-in, the table gateway and
    /// the home screen's view model (ARCHITECTURE 9.2). Builds the session scope once signed in.
    /// </summary>
    public sealed class AppScope : IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly NakamaConnection _connection;
        private readonly NakamaSessionService _sessionService;
        private readonly NakamaTableGateway _tables;
        private readonly NakamaMatchmaker _matchmaker;
        private readonly NakamaTableChat _tableChat;
        private SessionScope? _session;
        private PushPrompt? _pushPrompt;

        public AppScope(AppConfig config)
        {
            Config = config;
            Settings = new SettingsModel(Store);
            _connection = NakamaConnection.Create(config.Scheme, config.Host, config.Port, config.ServerKey);
            _sessionService = new NakamaSessionService(_connection);
            _tables = new NakamaTableGateway(_connection);
            Home = new HomeViewModel(_sessionService, _tables);
            _matchmaker = new NakamaMatchmaker(_connection);
            _tableChat = new NakamaTableChat(_connection);
            var social = new NakamaSocialService(_connection);
            Social = social;
            HomeFriends = new HomeFriendsViewModel(social);
            Mutes = new Hulla.Application.Social.MuteList(social);
            Account = new NakamaAccountService(_connection);
            Progress = new ProgressViewModel(new NakamaProgressService(_connection));
            DailyReward = new DailyRewardViewModel(new NakamaDailyRewardService(_connection));
            Leaderboard = new LeaderboardViewModel(new NakamaLeaderboardService(_connection));
            Ranked = new RankedViewModel(new NakamaRankedService(_connection));
            Phone = new PhoneViewModel(new NakamaPhoneService(_connection, _sessionService));
            Achievements = new AchievementsViewModel(new NakamaAchievementsService(_connection));
            Shop = new ShopViewModel(new NakamaShopService(_connection));
            NotificationsService = new NakamaNotificationsService(_connection);
            NotificationSettings = new NotificationSettingsViewModel(NotificationsService);
            Pass = new PassViewModel(new NakamaPassService(_connection));
            Missions = new MissionsViewModel(new NakamaMissionsService(_connection));
            Friends = new FriendsViewModel(social);
            Profile = new ProfileViewModel(social);
            QuickPlay = new QuickPlayViewModel(_matchmaker);
        }

        public AppConfig Config { get; private set; }

        /// <summary>Invite links from a cold or warm start (GDD 10.5).</summary>
        public DeepLinkRouter Links { get; } = new DeepLinkRouter();

        public CancellationToken Lifetime => _lifetime.Token;

        public ISessionService Sessions => _sessionService;

        public ITableGateway Tables => _tables;

        public HomeViewModel Home { get; }

        public IChatCatalog Chat { get; } = new StreamingChatCatalog();

        public IPresetCatalog Presets { get; } = new StreamingPresetCatalog();

        public QuickPlayViewModel QuickPlay { get; }

        public ITableChat TableChat => _tableChat;

        public FriendsViewModel Friends { get; }

        public Hulla.Application.Social.ISocialService Social { get; }

        public HomeFriendsViewModel HomeFriends { get; }

        public Hulla.Application.Social.MuteList Mutes { get; }

        public IAccountService Account { get; }

        public ProgressViewModel Progress { get; }

        public DailyRewardViewModel DailyReward { get; }

        public LeaderboardViewModel Leaderboard { get; }

        public RankedViewModel Ranked { get; }

        public PhoneViewModel Phone { get; }

        public AchievementsViewModel Achievements { get; }

        public ShopViewModel Shop { get; }

        public INotificationsService NotificationsService { get; }

        public NotificationSettingsViewModel NotificationSettings { get; }

        public PassViewModel Pass { get; }

        public MissionsViewModel Missions { get; }

        public Hulla.Application.Analytics.AnalyticsClient Analytics { get; } = new Hulla.Application.Analytics.AnalyticsClient(new Hulla.Infrastructure.Analytics.LogAnalyticsSink());

        public ProfileViewModel Profile { get; }

        public IShareSheet ShareSheet { get; } = new Hulla.Infrastructure.Platform.NativeShareSheet();

        public IPushDevice PushDevice { get; } = Hulla.Infrastructure.Platform.PushDevices.Create(Hulla.Presentation.Text.PushText.ChannelName);

        public PushPrompt PushPrompt => _pushPrompt ??= new PushPrompt(Store);

        public SettingsModel Settings { get; }

        /// <summary>Small saved flags (hints seen, rounds played) alongside the settings.</summary>
        public ISettingsStore Store { get; } = new PlayerPrefsSettingsStore();

        public SessionScope? Session => _session;

        public void UseServer(AppConfig config)
        {
            if (config.HostAndPort == Config.HostAndPort)
            {
                return;
            }

            EndSession();
            Config = config;
            _connection.Retarget(config.Scheme, config.Host, config.Port, config.ServerKey);
            _sessionService.Forget();
        }

        public SessionScope BeginSession(PlayerSession player)
        {
            if (_session != null && _session.Player.UserId == player.UserId)
            {
                return _session;
            }

            _session?.Dispose();
            _session = new SessionScope(this, player);
            return _session;
        }

        public void EndSession()
        {
            _session?.Dispose();
            _session = null;
        }

        public void Dispose()
        {
            EndSession();
            QuickPlay.Dispose();
            _matchmaker.Dispose();
            _tableChat.Dispose();
            _tables.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
    }
}
