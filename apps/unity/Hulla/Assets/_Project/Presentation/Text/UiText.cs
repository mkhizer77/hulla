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
        public const string Server = "Server (development builds)";

        public static string Error(string code) => "Something went wrong (" + code + ")";
    }
}
