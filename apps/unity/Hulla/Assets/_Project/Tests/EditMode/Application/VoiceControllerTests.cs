using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Table;
using Hulla.Application.Voice;
using Hulla.Domain;
using Hulla.Infrastructure.Nakama;
using Hulla.Protocol;
using NUnit.Framework;
using UnityEngine;

namespace Hulla.Application.Tests
{
    /// <summary>Voice at a table (GDD 11.4, 20.14, E25, OQ-044) against a scripted provider and token RPC.</summary>
    public sealed class VoiceControllerTests
    {
        private FakeGateway _gateway = null!;
        private TableClient _client = null!;
        private ScriptedVoice _voice = null!;
        private ScriptedTokens _tokens = null!;
        private HashSet<string> _muted = null!;
        private VoiceController _controller = null!;
        private long _seq;

        [SetUp]
        public void SetUp()
        {
            _gateway = new FakeGateway();
            _client = new TableClient(_gateway, new Services.TableTicket("m1", "ABC123", "t_ABC123"), Messages.Me, 100);
            _voice = new ScriptedVoice();
            _tokens = new ScriptedTokens();
            _muted = new HashSet<string>();
            _seq = 1;
            Deliver();
            _controller = new VoiceController(_voice, _tokens, _client, id => id != null && _muted.Contains(id), enabled: true);
        }

        [TearDown]
        public void TearDown() => _controller.Dispose();

        [Test]
        public void VoiceHidesWithoutAProviderTheServerSwitchOrOnATableWithVoiceOff()
        {
            Assert.AreEqual(VoiceState.Idle, _controller.State);
            Assert.AreEqual(VoiceState.Hidden, new VoiceController(new ScriptedVoice { Supported = false }, _tokens, _client, _ => false, true).State);
            Assert.AreEqual(VoiceState.Hidden, new VoiceController(_voice, _tokens, _client, _ => false, enabled: false).State);
            Deliver(voice: VoiceDefault.Off);
            Assert.AreEqual(VoiceState.Hidden, _controller.State);
        }

        [Test]
        public void PrivateRoomsDefaultToOpenMicAndOtherTablesToPushToTalk()
        {
            Assert.AreEqual(VoiceMode.OpenMic, _controller.Mode);
            Deliver(mode: TableMode.Public);
            Assert.AreEqual(VoiceMode.PushToTalk, _controller.Mode);
            Deliver(mode: TableMode.Public, voice: VoiceDefault.OpenMic);
            Assert.AreEqual(VoiceMode.OpenMic, _controller.Mode);
        }

        [Test]
        public void MyMicAloneIsSharedButOpensNoChannel()
        {
            _controller.ToggleMic();
            Assert.IsTrue(_controller.MicOn);
            Assert.IsTrue(LastMic(), "the seat's mic goes to the server");
            Assert.AreEqual(VoiceState.Waiting, _controller.State);
            Assert.AreEqual(0, _tokens.Asked, "one mic costs no minutes (GDD 20.14)");
        }

        [Test]
        public void ASecondMicOpensTheChannelAndOpenMicSends()
        {
            _controller.ToggleMic();
            Deliver(mics: new[] { 0, 1 });
            Assert.AreEqual(VoiceState.Live, _controller.State);
            Assert.AreEqual("t_ABC123", _voice.Channel);
            Assert.IsTrue(_voice.Published, "a seat may speak");
            Assert.IsTrue(_voice.Transmitting);
        }

        [Test]
        public void AListenerJoinsWhenTwoOthersHaveTheirMicsOn()
        {
            Deliver(mics: new[] { 1 });
            Assert.AreEqual(VoiceState.Idle, _controller.State);
            Deliver(mics: new[] { 1, 2 });
            Assert.AreEqual(VoiceState.Live, _controller.State);
            Assert.IsFalse(_voice.Transmitting, "my mic is off");
        }

        [Test]
        public void PushToTalkSendsOnlyWhileHeld()
        {
            Deliver(mode: TableMode.Public);
            _controller.ToggleMic();
            Deliver(mode: TableMode.Public, mics: new[] { 0, 3 });
            Assert.AreEqual(VoiceState.Live, _controller.State);
            Assert.IsFalse(_voice.Transmitting);
            _controller.SetTalking(true);
            Assert.IsTrue(_voice.Transmitting);
            _controller.SetTalking(false);
            Assert.IsFalse(_voice.Transmitting);
        }

        [Test]
        public void TwoQuietMinutesLeaveTheChannelUntilAMicChanges()
        {
            Deliver(mics: new[] { 1, 2 });
            _controller.Tick(60f);
            _voice.Speak("user-1", true);
            _controller.Tick(100f);
            Assert.AreEqual(VoiceState.Live, _controller.State, "speech resets the quiet clock");
            _voice.Speak("user-1", false);
            _controller.Tick(VoiceController.SilenceSeconds + 1f);
            Assert.AreEqual(VoiceState.Paused, _controller.State);
            Assert.IsNull(_voice.Channel);
            Deliver(mics: new[] { 1, 2 });
            Assert.AreEqual(VoiceState.Paused, _controller.State, "nothing changed");
            Deliver(mics: new[] { 1, 2, 3 });
            Assert.AreEqual(VoiceState.Live, _controller.State);
            Assert.AreEqual(2, _voice.Joins);
        }

        [Test]
        public void E25_AFailedChannelNeverBlocksPlayAndATapRetries()
        {
            _tokens.Next = Result<VoiceTicket>.Fail("voice.unavailable");
            _controller.ToggleMic();
            Deliver(mics: new[] { 0, 1 });
            Assert.AreEqual(VoiceState.Failed, _controller.State);
            _tokens.Next = null;
            _controller.ToggleMic();
            Assert.AreEqual(VoiceState.Live, _controller.State);
            Assert.IsTrue(_controller.MicOn, "the retry kept the mic on");

            _voice.Drop();
            Assert.AreEqual(VoiceState.Failed, _controller.State);
            Assert.IsNull(_voice.Channel);
        }

        [Test]
        public void ASuspendedPlayerNeverJoins()
        {
            _tokens.Next = Result<VoiceTicket>.Fail("voice.suspended");
            Deliver(mics: new[] { 1, 2 });
            Assert.AreEqual(VoiceState.Suspended, _controller.State);
            Deliver(mics: new[] { 1, 2, 3 });
            Assert.AreEqual(1, _tokens.Asked);
        }

        [Test]
        public void MutedPlayersAreSilentOnJoinAndWhenTheListChanges()
        {
            _muted.Add("user-1");
            Deliver(mics: new[] { 1, 2 });
            Assert.IsTrue(_voice.Muted["user-1"]);
            Assert.IsFalse(_voice.Muted["user-2"]);
            _muted.Add("user-2");
            _controller.MutesChanged();
            Assert.IsTrue(_voice.Muted["user-2"]);
        }

        [Test]
        public void AMicTheServerForgotIsSentAgain()
        {
            _controller.ToggleMic();
            var sent = _gateway.Sent.FindAll(s => s.Type == MessageTypes.SetMic).Count;
            Deliver();
            _controller.Tick(1f);
            Assert.AreEqual(sent, _gateway.Sent.FindAll(s => s.Type == MessageTypes.SetMic).Count, "waits for the echo");
            _controller.Tick(3f);
            Assert.AreEqual(sent + 1, _gateway.Sent.FindAll(s => s.Type == MessageTypes.SetMic).Count);
            Assert.IsTrue(LastMic());
        }

        [Test]
        public void ARefusedMicrophoneLeavesTheMicOffAndSaysSo()
        {
            _voice.AllowMic = false;
            _controller.ToggleMic();
            Assert.IsFalse(_controller.MicOn);
            Assert.AreEqual(VoiceState.NoPermission, _controller.State);
            Assert.IsFalse(_gateway.Sent.Exists(s => s.Type == MessageTypes.SetMic));
        }

        [Test]
        public void ATokenNearItsEndIsRenewedAndLeavingTheTableLeavesTheChannel()
        {
            Deliver(mics: new[] { 1, 2 });
            _voice.ExpireSoon();
            Assert.AreEqual(2, _tokens.Asked);
            Assert.AreEqual("token-2", _voice.Token);
            _controller.Dispose();
            Assert.IsNull(_voice.Channel);
        }

        [Test]
        public void TheSpeakingSetFollowsTheProvider()
        {
            Deliver(mics: new[] { 1, 2 });
            _voice.Speak("user-2", true);
            Assert.IsTrue(_controller.IsSpeaking("user-2"));
            _voice.Speak("user-2", false);
            Assert.IsFalse(_controller.IsSpeaking("user-2"));
        }

        [Test]
        public void TheTokenReplyNeedsEveryField()
        {
            var ok = NakamaVoiceTokens.Parse(@"{""ok"":true,""provider"":""livekit"",""url"":""wss://voice.test"",""channel"":""t_X"",""account"":""u1"",""token"":""a.b.c"",""expiresTs"":5}");
            Assert.IsTrue(ok.IsOk);
            Assert.AreEqual("u1", ok.Value.Account);
            Assert.AreEqual("wss://voice.test", ok.Value.Url);
            Assert.AreEqual("voice.suspended", NakamaVoiceTokens.Parse(@"{""ok"":false,""error"":{""code"":""voice.suspended""}}").Error.Code);
            Assert.IsFalse(NakamaVoiceTokens.Parse(@"{""ok"":true,""channel"":""t_X""}").IsOk);
        }

        private bool LastMic() => ((SetMic)_gateway.Sent.FindLast(s => s.Type == MessageTypes.SetMic).Payload).Mic;

        private void Deliver(int[]? mics = null, TableMode mode = TableMode.Private, VoiceDefault? voice = null)
        {
            var message = Messages.TableState(_seq++);
            var state = (TableState)message.Payload;
            state.Mode = mode;
            if (voice != null)
            {
                state.Rules = new RulesSelection { GameId = GameId.Thulla, Settings = new Protocol.Settings { VoiceDefault = voice } };
            }

            foreach (var seat in state.Seats)
            {
                seat.Mic = mics != null && Array.IndexOf(mics, (int)seat.SeatSeat) >= 0;
            }

            _gateway.Deliver(message);
        }

        private static Awaitable<T> Done<T>(T value)
        {
            var source = new AwaitableCompletionSource<T>();
            source.SetResult(value);
            return source.Awaitable;
        }

        private sealed class ScriptedTokens : IVoiceTokens
        {
            public int Asked { get; private set; }

            public Result<VoiceTicket>? Next { get; set; }

            public Awaitable<Result<VoiceTicket>> TokenAsync(string matchId, CancellationToken cancellation)
            {
                Asked++;
                return Done(Next ?? Result<VoiceTicket>.Ok(new VoiceTicket("wss://voice.test", "t_ABC123", Messages.Me, "token-" + Asked, 0)));
            }
        }

        private sealed class ScriptedVoice : IVoiceService
        {
            public bool Supported { get; set; } = true;

            public bool AllowMic { get; set; } = true;

            public string? Channel { get; private set; }

            public string? Token { get; private set; }

            public bool Published { get; private set; }

            public bool Transmitting { get; private set; }

            public int Joins { get; private set; }

            public Dictionary<string, bool> Muted { get; } = new Dictionary<string, bool>();

            public event Action<string, bool>? Speaking;

            public event Action? Dropped;

            public event Action? TokenExpiring;

            public Awaitable<bool> AskMicrophoneAsync() => Done(AllowMic);

            public Awaitable<Result<bool>> JoinAsync(VoiceTicket ticket, bool publish, CancellationToken cancellation)
            {
                Joins++;
                Channel = ticket.Channel;
                Token = ticket.Token;
                Published = publish;
                return Done(Result<bool>.Ok(true));
            }

            public Awaitable LeaveAsync()
            {
                Channel = null;
                Transmitting = false;
                var source = new AwaitableCompletionSource();
                source.SetResult();
                return source.Awaitable;
            }

            public void RenewToken(string token) => Token = token;

            public void SetTransmit(bool on) => Transmitting = on;

            public void SetPeerMuted(string account, bool muted) => Muted[account] = muted;

            public void Speak(string account, bool speaking) => Speaking?.Invoke(account, speaking);

            public void Drop()
            {
                Channel = null;
                Dropped?.Invoke();
            }

            public void ExpireSoon() => TokenExpiring?.Invoke();
        }
    }
}
