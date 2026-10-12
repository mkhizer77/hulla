namespace Hulla.Application.Services
{
    /// <summary>One decoded server message: the wire type, the table seq it carries and its generated DTO.</summary>
    public readonly struct ServerMessage
    {
        public ServerMessage(string type, long seq, object payload)
        {
            Type = type;
            Seq = seq;
            Payload = payload;
        }

        public string Type { get; }

        public long Seq { get; }

        public object Payload { get; }
    }
}
