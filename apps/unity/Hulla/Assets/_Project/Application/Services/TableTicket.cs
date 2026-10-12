namespace Hulla.Application.Services
{
    /// <summary>What the table RPCs return: the match to join, the room code and the table id envelopes carry.</summary>
    public sealed class TableTicket
    {
        public TableTicket(string matchId, string code, string tableId)
        {
            MatchId = matchId;
            Code = code;
            TableId = tableId;
        }

        public string MatchId { get; }

        public string Code { get; }

        public string TableId { get; }
    }
}
