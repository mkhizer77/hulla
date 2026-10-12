namespace Hulla.Application.Services
{
    /// <summary>What the table RPCs (or the matchmaker) return: the match to join, the room code and the table id envelopes carry.</summary>
    public sealed class TableTicket
    {
        public TableTicket(string matchId, string code, string tableId)
        {
            MatchId = matchId;
            Code = code;
            TableId = tableId;
        }

        /// <summary>A matchmade table: the code and table id arrive with the first table_state.</summary>
        public static TableTicket Matchmade(string matchId) => new TableTicket(matchId, "", "");

        public bool IsMatchmade => Code.Length == 0;

        public string MatchId { get; }

        public string Code { get; }

        public string TableId { get; }
    }
}
