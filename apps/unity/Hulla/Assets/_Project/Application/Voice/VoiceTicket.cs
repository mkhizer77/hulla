namespace Hulla.Application.Voice
{
    /// <summary>What `voice_token` returns: the provider's app id, the table's channel and a token bound to this player.</summary>
    public sealed class VoiceTicket
    {
        public VoiceTicket(string appId, string channel, string account, string token, long expiresMs)
        {
            AppId = appId;
            Channel = channel;
            Account = account;
            Token = token;
            ExpiresMs = expiresMs;
        }

        public string AppId { get; }

        public string Channel { get; }

        /// <summary>The player's user id; other clients map a speaker back to a seat by it.</summary>
        public string Account { get; }

        public string Token { get; }

        public long ExpiresMs { get; }
    }
}
