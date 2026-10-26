using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Application.Table;
using Hulla.Application.Voice;

namespace Hulla.Composition
{
    /// <summary>One table: the table client and its voice.</summary>
    public sealed class TableScope : IDisposable
    {
        private readonly CancellationTokenSource _lifetime;

        public TableScope(SessionScope session, TableTicket ticket)
        {
            Session = session;
            Ticket = ticket;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Lifetime);
            Client = new TableClient(session.App.Tables, ticket, session.Player.UserId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var app = session.App;
            Voice = new VoiceController(app.Voice, app.VoiceTokens, Client, app.Mutes.IsMuted, app.RemoteConfig.Voice);
            Voice.Joined += OnVoiceJoined;
            app.Mutes.Changed += Voice.MutesChanged;
        }

        public VoiceController Voice { get; }

        public SessionScope Session { get; }

        public TableTicket Ticket { get; }

        public TableClient Client { get; }

        public CancellationToken Lifetime => _lifetime.Token;

        public void Dispose()
        {
            Session.App.Mutes.Changed -= Voice.MutesChanged;
            Voice.Joined -= OnVoiceJoined;
            Voice.Dispose();
            Client.Dispose();
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private void OnVoiceJoined(VoiceMode mode) =>
            Session.App.Analytics.Track(Protocol.AnalyticsEvents.VoiceJoin(mode == VoiceMode.PushToTalk ? "pushToTalk" : "openMic"));
    }
}
