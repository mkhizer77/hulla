namespace Hulla.Application.Services
{
    /// <summary>What the table RPCs (or the matchmaker) return: the match to join, the room code and the table id envelopes carry.</summary>
    public sealed class TableTicket
    {
        public TableTicket(string matchId, string code, string tableId, string? resumeToken = null, bool asSpectator = false)
        {
            MatchId = matchId;
            Code = code;
            TableId = tableId;
            ResumeToken = resumeToken;
            AsSpectator = asSpectator;
        }

        /// <summary>Proves this client held its seat, so a rejoin replaces a session the server still thinks is live (GDD 10.2).</summary>
        public string? ResumeToken { get; }

        /// <summary>Join to watch rather than to sit (GDD 7.7).</summary>
        public bool AsSpectator { get; }

        public TableTicket WithResume(string? resumeToken) => new TableTicket(MatchId, Code, TableId, resumeToken, AsSpectator);

        public TableTicket Watching() => new TableTicket(MatchId, Code, TableId, ResumeToken, true);

        /// <summary>A matchmade table: the code and table id arrive with the first table_state.</summary>
        public static TableTicket Matchmade(string matchId) => new TableTicket(matchId, "", "");

        public bool IsMatchmade => Code.Length == 0;

        public string MatchId { get; }

        public string Code { get; }

        public string TableId { get; }
    }
}
