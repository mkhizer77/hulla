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
        public const string EscapeOrder = "Escaped in order";
        public const string PlayAgain = "Play again";
        public const string Waiting4Rematch = "Waiting for the others…";
        public const string You = "You";
        public const string Reconnecting = "Reconnecting…";
        public const string Chat = "Chat";
        public const string QuickPlay = "Quick Play";
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

        public static string Error(string code) => "Something went wrong (" + code + ")";
    }
}
