namespace Hulla.Application.Voice
{
    /// <summary>What `voice_token` returns: the voice server, the table's room and a token bound to this player.</summary>
    public sealed class VoiceTicket
    {
        public VoiceTicket(string url, string channel, string account, string token, long expiresMs)
        {
            Url = url;
            Channel = channel;
            Account = account;
            Token = token;
            ExpiresMs = expiresMs;
        }

        /// <summary>The voice server to connect to (`wss://` on a box).</summary>
        public string Url { get; }

        public string Channel { get; }

        /// <summary>The player's user id; other clients map a speaker back to a seat by it.</summary>
        public string Account { get; }

        public string Token { get; }

        public long ExpiresMs { get; }
    }
}
