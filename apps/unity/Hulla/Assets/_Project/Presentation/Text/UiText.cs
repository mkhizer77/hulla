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
        public const string Waiting4Rematch = "Waiting for the others…";
        public const string You = "You";
        public const string Reconnecting = "Reconnecting…";
        public const string Chat = "Chat";
        public const string Send = "Send";
        public const string ChatTooFast = "Slow down a little: one message every 2 seconds.";
        public const string ChatRefused = "Not sent. Messages can't have links or phone numbers, up to 140 characters.";
        public const string QuickPlay = "Quick Play";
        public const string Settings = "Settings";
        public const string Done = "Done";
        public const string SettingsPlay = "Playing";
        public const string SettingsSeeing = "Seeing and moving";
        public const string OneTapPlay = "One-tap play";
        public const string HandSortLabel = "Sort my hand";
        public const string SortBySuit = "By suit";
        public const string SortByRank = "By rank";
        public const string HapticsLabel = "Vibrate on my turn";
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
        public const string EditRules = "Edit rules";
        public const string PresetLabel = "Preset";
        public const string TurnTimerLabel = "Turn timer";
        public const string LoserTitleLabel = "The loser is called";
        public const string RulesSummary = "{0} · {1} · loser: {2}";
        public const string TurnSeconds = "{0} s turns";
        public const string NoTimer = "No timer";
        public const string RulesChangedBy = "{0} changed the rules";
        public const string JoinFriendTable = "Join {0}'s table ({1} open)";

        public static string Presence(PresenceState state) => state switch
        {
            PresenceState.Lobby => "in a lobby",
            PresenceState.Round => "playing",
            PresenceState.Away => "away",
            _ => "online",
        };
        public const string ShareCode = "Share code";
        public const string ShareTitle = "Invite to Hulla";

        /// <summary>The bilingual invite (GDD 10.5); the hulla.gg/j/ link joins once deep links exist (#43).</summary>
        public const string ShareText = "Thulla khelo! Join my table on Hulla with code {0}";
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
                case "social.self":
                    return "That's your own code.";
                case "social.friend_cap":
                    return "You have the most friends allowed (500).";
                case "social.favourite_cap":
                    return "Ten favourites at most.";
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
