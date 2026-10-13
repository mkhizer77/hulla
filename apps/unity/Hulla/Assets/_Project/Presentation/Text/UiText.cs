using Hulla.Application.Social;

namespace Hulla.Presentation.Text
{
    /// <summary>
    /// English UI text for the first playable, kept in one place so the localisation pass (#65) can
    /// move it into string tables by key without touching the views.
    /// </summary>
    public static class UiText
    {
        public const string AppTitle = "Hulla";
        public const string Tagline = "Thulla with friends";
        public const string NameLabel = "Your name";
        public const string NameTooShort = "A name needs at least 2 characters.";
        public const string CreateTable = "Create a table";
        public const string CodeLabel = "Room code";
        public const string JoinTable = "Join";
        public const string Connecting = "Connecting…";
        public const string RoomCode = "Room code";
        public const string Ready = "Ready";
        public const string NotReady = "Not ready";
        public const string Start = "Start round";
        public const string Leave = "Leave";
        public const string Host = "host";
        public const string Waiting = "Waiting for players";
        public const string Empty = "Open seat";
        public const string YourTurn = "Your turn";
        public const string TakeHand = "Take hand";
        public const string Escaped = "Out";
        public const string Away = "Away";

        /// <summary>A seat whose player left or lost connection; the server plays it for the rest of the round.</summary>
        public const string SeatAwayAutoPlay = "Away · auto-play";

        /// <summary>A present player whose turns time out, so the server plays for them (R45).</summary>
        public const string SeatAutoPlay = "Auto-play";

        /// <summary>Whose turn it is when it is not the player's own.</summary>
        public const string TurnOf = "{0}'s turn";
        public const string RejoinTable = "Rejoin your table ({0})";
        public const string Thulla = "THULLA!";
        public const string PickedUp = "{0} picks up";
        public const string Bhabhi = "{0} is the Bhabhi";
        public const string LoserIs = "{0} is the {1}";
        public const string AfterRounds = "After {0} rounds";
        public const string PointsLine = "{0}: {1} pts · {2}× {3}";
        public const string Highlights = "Highlights";
        public const string MostThullas = "Most thullas: {0}, {1}";
        public const string MostPickedUp = "Most cards picked up: {0}, {1}";
        public const string MakeFriends = "Played well together?";
        public const string AddName = "Add {0}";
        public const string RematchVotes = "{0} of {1} want another";
        public const string EscapeOrder = "Escaped in order";
        public const string PlayAgain = "Play again";
        public const string AppName = "Hulla";
        public const string ShareCardGame = "Thulla";
        public const string ShareResult = "Share";
        public const string ShareAs = "Share the result as";
        public const string SharePost = "Post";
        public const string ShareStory = "Story";
        public const string ShareResultTitle = "Thulla on Hulla";

        /// <summary>{0} loser's name, {1} the table's loser title, {2} room code.</summary>
        public const string ShareResultWithCode = "{0} is the {1}! Join our table on Hulla with code {2}";

        /// <summary>{0} loser's name, {1} the table's loser title.</summary>
        public const string ShareResultText = "{0} is the {1}! Play Thulla with us on Hulla.";

        public const string ShareFooterCode = "Join with code {0} on Hulla";
        public const string ShareFooter = "Play Thulla on Hulla";

        /// <summary>{0} name, {1} cards picked up.</summary>
        public const string BigThulla = "{0} picked up {1} cards, the biggest thulla of the night. Share it?";

        public const string NotNow = "Not now";
        public const string Waiting4Rematch = "Waiting for the others…";
        public const string You = "You";
        public const string Reconnecting = "Reconnecting…";
        public const string Chat = "Chat";
        public const string Send = "Send";
        public const string ChatTooFast = "Slow down a little: one message every 2 seconds.";
        public const string ChatGuest = "Guests can chat at friends' tables. Link an account to chat at public ones.";
        public const string ChatFiltered = "That message has words this table doesn't allow.";
        public const string Mute = "Mute";
        public const string Report = "Report";
        public const string ReportWhy = "What happened with {0}?";
        public const string ReportThanks = "Thanks. We'll look into it; the table's recent chat is attached.";
        public const string Blocked = "Blocked. You won't hear from or be matched with {0}.";

        /// <summary>The reasons of GDD 10.11, in the server's order (services/reports REPORT_REASONS).</summary>
        public static readonly (string Id, string Label)[] ReportReasons =
        {
            ("cheating", "Cheating or teaming up"),
            ("abusive_text", "Abusive messages"),
            ("abusive_voice", "Abusive voice"),
            ("inappropriate_profile", "Inappropriate name or picture"),
            ("spam", "Spam"),
            ("underage", "Seems under age"),
        };
        public const string Unmute = "Unmute";
        public const string MutedNote = "You won't see {0}'s messages or emotes.";

        public static string ChatProblem(string code) => code switch
        {
            "network.rate_limited" => ChatTooFast,
            "auth.not_linked" => ChatGuest,
            "content.filtered" => ChatFiltered,
            "social.muted" => "You're muted for a while after reports from other players.",
            _ => ChatRefused,
        };

        public const string ChatRefused = "Not sent. Messages can't have links or phone numbers, up to 140 characters.";
        public const string QuickPlay = "Quick Play";
        public const string Settings = "Settings";
        public const string SettingsAccount = "Account and data";
        public const string SettingsPrivacy = "Privacy";
        public const string AnalyticsLabel = "Share anonymous usage data";
        public const string AnalyticsAsk = "Help us improve Hulla? We'd count anonymous things like round length and taps, never your chat, cards or name.";
        public const string AnalyticsYes = "Yes, share";
        public const string AnalyticsNo = "No thanks";
        public const string ExportData = "Export my data";
        public const string ExportShared = "Your data is ready to share.";
        public const string ExportCopied = "Your data was copied.";
        public const string DeleteAccount = "Delete my account";
        public const string DeleteConfirm = "Delete your account? You have 14 days to change your mind.";
        public const string DeleteYes = "Delete";
        public const string DeleteNo = "Keep it";
        public const string DeletePending = "Your account will be deleted on {0}. Cancel any time before then.";
        public const string CancelDeletion = "Cancel deletion";
        public const string DeletionCancelled = "Deletion cancelled. Your account stays.";
        public const string Done = "Done";
        public const string SettingsPlay = "Playing";
        public const string SettingsSeeing = "Seeing and moving";
        public const string OneTapPlay = "One-tap play";
        public const string HandSortLabel = "Sort my hand";
        public const string SortBySuit = "By suit";
        public const string SortByRank = "By rank";
        public const string HapticsLabel = "Vibration";
        public const string PlayNow = "Play now";
        public const string CreateRoom = "Create a room";
        public const string JoinByCode = "Join by code";
        public const string Retry = "Retry";
        public const string Loading = "Loading…";
        public const string ChangeRules = "Change rules";
        public const string FairDealCheck = "Fair deal · see the shuffle";
        public const string FairDealExplained = "Before the deal, the server committed to {0}. The seed was {1}. Anyone can rebuild the shuffle from these two, so the deal could not have been changed after the fact.";
        public const string CopySeed = "Copy the seed";
        public const string StartWith = "Start · {0} of {1} seated";
        public const string NoMissionsYet = "New missions arrive at midnight.";
        public const string NoAchievementsYet = "Play a round to start.";
        public const string NothingThisWeek = "Nothing here this week.";
        public const string ShowMyCode = "Show my code";
        public const string InviteYourGroup = "Invite your group";
        public const string PartyEmpty = "No friends online. Share your code.";
        public const string Watching1 = "Watching: {0}";

        /// <summary>The bottom navigation (GDD 14.1).</summary>
        public static string NavLabel(Navigation.NavTab tab) => tab switch
        {
            Navigation.NavTab.Home => "Home",
            Navigation.NavTab.Friends => Friends,
            Navigation.NavTab.Shop => Shop,
            Navigation.NavTab.Pass => "Pass",
            _ => Profile,
        };

        /// <summary>Glyphs stand in for the icon set (#23).</summary>
        public static string NavGlyph(Navigation.NavTab tab) => tab switch
        {
            Navigation.NavTab.Home => "⌂",
            Navigation.NavTab.Friends => "☺",
            Navigation.NavTab.Shop => "◈",
            Navigation.NavTab.Pass => "★",
            _ => "●",
        };
        public const string SettingsSound = "Sound";
        public const string SfxLabel = "Sound effects";

        /// <summary>The stamp for a pile of five or more (GDD 14.6).</summary>
        public const string ThullaBig = "OYE HOYE!";

        /// <summary>The short flag on the seat that gave the thulla (GDD 14.5).</summary>
        public const string ThullaFlag = "THULLA";

        /// <summary>The leader's crown (GDD 14.5), as a glyph until the icon set lands.</summary>
        public const string Crown = "♛";

        /// <summary>Opens the full quick-chat panel from the strip.</summary>
        public const string More = "…";

        /// <summary>The picked-up tray in the player sheet (GDD 14.7).</summary>
        public const string PickedUpTray = "{0} has picked up: {1}";
        public const string RulesRecapLabel = "Rules recap at the first round";
        public const string FourColourLabel = "Four-colour deck";
        public const string HighContrastLabel = "High contrast";
        public const string LargeCardsLabel = "Large cards";
        public const string TextSizeLabel = "Text size";
        public const string ReduceMotionLabel = "Reduce motion";
        public const string LeftHandedLabel = "Left-handed layout";
        public const string Playing = "Playing…";
        public const string Lead = "Leads";
        public const string Discard = "Discard {0}";
        public const string FairDeal = "Fair deal ✓";
        public const string MustFollow = "Must follow {0}";
        public const string LeadAceOfSpades = "Lead the A♠ to open";
        public const string NotYourTurn = "Wait for your turn";
        public const string NotPlayableNow = "Not playable now";
        public const string SeatsLabel = "Table size";
        public const string Cancel = "Cancel";
        public const string Searching = "Finding a table for {0}…";
        public const string SearchingWider = "Looking at every table size…";
        public const string SearchingStart = "Starting with three or more as soon as they're in…";
        public const string FoundTable = "Table found";
        public const string StartsIn = "Dealing in {0}s";
        public const string QuickPlayTable = "Quick Play table";
        public const string RecapTitle = "Standard rules";
        public const string Recap = "Follow the led suit if you can. If you can't, any card is a thulla: the trick stops and whoever played the highest card of the led suit picks up the pile. Empty your hand to escape. The last player holding cards is the Bhabhi.";
        public const string RecapHide = "Don't show again";
        public const string Close = "Close";

        /// <summary>Emote ids read as words until the emote art arrives (#34): "laugh_cry" → "Laugh cry".</summary>
        public static string EmoteLabel(string id)
        {
            var words = id.Replace('_', ' ');
            return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words.Substring(1);
        }
        public const string Server = "Server (development builds)";

        public const string Profile = "Profile";
        public const string Missions = "Missions";
        public const string Leaderboard = "Leaderboard";
        public const string Achievements = "Achievements";
        public const string Shop = "Shop";
        public const string InviteFriends = "Invite friends";
        public const string Invite = "Invite";
        public const string Invited = "Invited";
        public const string AtTable = "At the table";
        public const string NoFriendsToInvite = "No friends yet. Share the code, or add friends from Home.";

        /// <summary>{0} inviter's name, {1} room code.</summary>
        public const string InviteReceived = "{0} invites you to their table ({1}).";
        public const string InviteLinkWhileSeated = "Finish this table first to join {0}.";

        public const string Notifications = "Notifications";
        public const string NotifyInvites = "Table invites";
        public const string NotifyFriendRequests = "Friend requests";
        public const string NotifyFriendOnline = "A friend comes online";
        public const string NotifyDailyReward = "Daily reward ready";
        public const string NotifySeasonEnd = "Season ending soon";
        public const string NotifyQuietHours = "Quiet hours at night (friends' invites still come)";
        public const string NotifyNonFriendInvites = "Invites from players who aren't friends";

        /// <summary>Why an invite didn't go: a table_invite skip reason or an error code.</summary>
        public static string InviteProblem(string reason) => reason switch
        {
            "blocked" => "Can't invite",
            "muted" => "Not taking invites",
            "rate_limited" or "network.rate_limited" => "Too many invites; try later",
            "self" => "That's you",
            _ => "Didn't send",
        };
        public const string SeasonPass = "Season pass";
        public const string NoSeason = "No season is running right now. The pass opens with the next one.";

        /// <summary>{0} tier, {1} tier count, {2} XP into the tier, {3} XP per tier.</summary>
        public const string PassProgress = "Tier {0} of {1} · {2:N0}/{3:N0} XP";

        /// <summary>{0} tier count.</summary>
        public const string PassComplete = "All {0} tiers done";

        /// <summary>{0} time left, such as "3d 4h".</summary>
        public const string SeasonEndsIn = "ends in {0}";

        public const string DoubleXp = "double XP";
        public const string PremiumLocked = "The premium track opens with the premium pass.";
        public const string Locked = "Locked";

        /// <summary>{0} the title's name.</summary>
        public const string TitleReward = "title \"{0}\"";
        public const string Spotlight = "This week";
        public const string Equip = "Equip";
        public const string Equipped = "Equipped";
        public const string Free = "Free";
        public const string PriceCoins = "{0:N0} coins";
        public const string PriceGems = "{0:N0} gems";

        /// <summary>{0} coins, {1} gems.</summary>
        public const string Balances = "{0:N0} coins · {1:N0} gems";

        /// <summary>{0} item name, {1} price.</summary>
        public const string BuyQuestion = "Buy {0} for {1}?";

        public const string Buy = "Buy";

        /// <summary>{0} unlocked, {1} total.</summary>
        public const string AchievementsCount = "{0} of {1} unlocked";

        public const string GemsOnly = "{0} gems";

        /// <summary>{0} gems, {1} the title's name.</summary>
        public const string GemsAndTitle = "{0} gems + the title \"{1}\"";

        /// <summary>{0} the achievement's name.</summary>
        public const string AchievementUnlocked = "Achievement unlocked: {0}";

        /// <summary>A title key ("thulla_baaz") as words ("Thulla baaz") until the string tables land (#65).</summary>
        public static string TitleName(string key)
        {
            var words = key.Replace('_', ' ');
            return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words.Substring(1);
        }
        public const string ThisWeek = "This week";
        public const string ThisMonth = "This month";
        public const string Everyone = "Everyone";
        public const string FriendsTab = "Friends";
        public const string BoardEmpty = "No points yet. Win rounds to get on the board.";

        /// <summary>{0} time left, such as "3d 4h".</summary>
        public const string ResetsIn = "Resets in {0}. Top 10 win gems.";

        public const string PointsShort = "{0:N0} pts";

        /// <summary>{0} the player's own name on a board.</summary>
        public const string YouAre = "{0} (you)";
        public const string MissionsToday = "Today";
        public const string MissionsWeek = "This week";
        public const string MissionProgress = "{0}/{1} · {2}";
        public const string RewardCoins = "{0} coins";
        public const string RewardGems = "{0} coins, {1} gems";
        public const string Claim = "Claim";

        /// <summary>{0} day of the 7-day calendar, {1} the amount.</summary>
        public const string DailyReady = "Daily reward, day {0}: {1}";

        public const string DailyClaimed = "Claimed {0}.";
        public const string DailyTomorrow = "Tomorrow: {0}.";
        public const string CoinsOnly = "{0:N0} coins";
        public const string CoinsAndGems = "{0:N0} coins + {1} gems";
        public const string Claimed = "Done";
        public const string Reroll = "Swap";

        /// <summary>A mission described from what it counts (server services/missions), until the string tables (#65).</summary>
        public static string MissionText(string metric, long target) => metric switch
        {
            "thullas_given" => $"Give {target} thullas",
            "escaped_first" => target == 1 ? "Escape first in a round" : $"Escape first in {target} rounds",
            "escapes" => $"Escape {target} times",
            "rounds_played" => $"Play {target} rounds",
            "rounds_with_friend" => $"Play {target} rounds with a friend",
            "clean_rounds" => $"Escape without picking up, {target} times",
            "low_pickup_rounds" => $"Pick up fewer than 5 cards in {target} rounds",
            "hands_taken" => $"Take a hand {target} times",
            "series_won" => target == 1 ? "Win a series" : $"Win {target} series",
            "voice_private" => $"Use voice in {target} private rounds",
            _ => $"{metric} × {target}",
        };
        public const string ProgressLine = "Level {0} · {1}/{2} XP · {3} coins";
        public const string StreakSuffix = " · day {0} streak";
        public const string RewardLine = "+{0} XP · +{1} coins";
        public const string RewardCapped = " (daily cap reached)";
        public const string LevelUp = "Level up! {0} → {1}";

        /// <summary>Starting names for a new player (GDD 17.2), changeable on Home.</summary>
        public static readonly string[] NamePlaces = { "Lahori", "Dilli", "Karachi", "Dhaka", "Pindi", "Mumbai", "Multani", "Jaipuri" };

        public static readonly string[] NameAnimals = { "Lion", "Tiger", "Falcon", "Panther", "Cheetah", "Eagle", "Leopard", "Peacock" };
        public const string EditRules = "Edit rules";
        public const string PresetLabel = "Preset";
        public const string TurnTimerLabel = "Turn timer";
        public const string LoserTitleLabel = "The loser is called";
        public const string RulesSummary = "{0} · {1} · loser: {2}";
        public const string TurnSeconds = "{0} s turns";
        public const string NoTimer = "No timer";
        public const string RulesChangedBy = "{0} changed the rules";
        public const string JoinFriendTable = "Join {0}'s table ({1} open)";
        public const string WatchFriendTable = "Watch {0}'s game";
        public const string Watching = "Watching";
        public const string WatchingLobby = "You're watching. The host can give you a seat.";
        public const string Watchers = "Watching";
        public const string SeatThem = "Seat";
        public const string SeatsNext = "next round";

        public static string Presence(PresenceState state) => state switch
        {
            PresenceState.Lobby => "in a lobby",
            PresenceState.Round => "playing",
            PresenceState.Away => "away",
            _ => "online",
        };
        public const string ShareCode = "Share code";
        public const string ShareTitle = "Invite to Hulla";

        /// <summary>The bilingual invite (GDD 10.5) with the code and the link that opens the app at the table.</summary>
        public const string ShareText = "Thulla khelo! Join my table on Hulla with code {0}: {1}";
        public const string Friends = "Friends";
        public const string Back = "Back";
        public const string Save = "Save";
        public const string Saved = "Saved";
        public const string Copy = "Copy";
        public const string Copied = "Copied";
        public const string DisplayNameLabel = "Name";
        public const string HandleLabel = "Handle (letters, numbers, _)";
        public const string BioLabel = "About you (60 characters)";
        public const string FriendCodeLabel = "Your friend code";
        public const string StatsLine = "{0} rounds · {1} escapes · {2} times Bhabhi · {3} thullas given";
        public const string AddByCode = "Add a friend by code";
        public const string Add = "Add";
        public const string RequestSent = "Request sent";
        public const string Requests = "Requests";
        public const string SentRequests = "Waiting for them";
        public const string FriendsList = "Friends";
        public const string Recent = "Played with recently";
        public const string BlockedList = "Blocked";
        public const string Accept = "Accept";
        public const string Decline = "Decline";
        public const string CancelRequest = "Cancel";
        public const string Remove = "Remove";
        public const string Block = "Block";
        public const string Unblock = "Unblock";
        public const string AddFriend = "Add friend";
        public const string Favourite = "★";
        public const string NotFavourite = "☆";
        public const string Online = "Online";
        public const string Offline = "Offline";
        public const string RoundsTogether = "{0} rounds together";
        public const string NoFriendsYet = "No friends yet. Share your code or add theirs.";

        /// <summary>Plain words for the server's error codes; anything else falls back to the code.</summary>
        public static string Error(string code)
        {
            switch (code)
            {
                case "profile.not_found":
                    return "No player has that code.";
                case "table.not_found":
                case "table.invalid_code":
                    return "No table has that code. Check it with the host.";
                case "table.full":
                    return "That table is full.";
                case "table.locked":
                    return "That table isn't taking players right now.";
                case "table.banned":
                    return "The host removed you from that table.";
                case "table.blocked":
                    return "You can't join that table.";
                case "auth.banned":
                    return "This account can't play right now. Contact support if you think that's wrong.";
                case "auth.device_limit":
                    return "You're at this table on another device. Close it there first.";
                case "economy.insufficient_funds":
                    return "Not enough coins or gems for that.";
                case "economy.item_owned":
                    return "You already own that.";
                case "economy.item_unknown":
                    return "That item isn't in the shop any more.";
                case "auth.update_required":
                case "table.unsupported_version":
                    return "Please update Hulla to keep playing.";
                case "social.self":
                    return "That's your own code.";
                case "social.friend_cap":
                    return "You have the most friends allowed (500).";
                case "social.favourite_cap":
                    return "Ten favourites at most.";
                case "social.blocked":
                    return "You can't add this player.";
                case "social.not_friends":
                    return "Only friends can be favourites.";
                case "profile.handle_taken":
                    return "That handle is taken.";
                case "profile.invalid_handle":
                    return "Handles are 3 to 16 letters, numbers or _.";
                case "profile.invalid_name":
                    return "That name can't be used.";
                case "profile.name_change_locked":
                    return "You changed your name recently; try again later.";
                case "session.unavailable":
                case "social.unavailable":
                case "table.unavailable":
                    return "Can't reach the server. Check the connection and try again.";
                default:
                    return "Something went wrong (" + code + ")";
            }
        }
    }
}
