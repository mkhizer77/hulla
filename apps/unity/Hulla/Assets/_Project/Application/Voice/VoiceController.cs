using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Table;
using UnityEngine;

namespace Hulla.Application.Voice
{
    /// <summary>
    /// Voice for one table (GDD 11.4). Each seat shares its mic through the server; this client is in the
    /// channel only while two mics at the table are on and leaves after two minutes in which nobody spoke
    /// (GDD 20.14, OQ-044). A failure never blocks play (E25): the mic shows a retry instead. Muted players
    /// stay silent here, and the mute list lives on the server, so it survives a reconnect.
    /// </summary>
    public sealed class VoiceController : IDisposable
    {
        /// <summary>No speech for this long and the client leaves the channel.</summary>
        public const float SilenceSeconds = 120f;

        /// <summary>A mic the server has not echoed back is sent again after this long (a rejoin forgets it).</summary>
        private const float ResendSeconds = 3f;

        private readonly IVoiceService _voice;
        private readonly IVoiceTokens _tokens;
        private readonly TableClient _client;
        private readonly Func<string?, bool> _isMuted;
        private readonly bool _enabled;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly HashSet<string> _speaking = new HashSet<string>();
        private VoiceState _state;
        private bool _inChannel;
        private bool _joinedSeated;
        private bool _joinedWithMic;
        private bool _micOn;
        private bool _talking;
        private bool _suspended;
        private bool _noPermission;
        private float _quiet;
        private float _resendIn;
        private string? _heldSignature;
        private bool _disposed;

        /// <param name="isMuted">The player's mute list (GDD 11.6); a muted player is silent here too.</param>
        /// <param name="enabled">The server's `voice` switch (GDD 22.5).</param>
        public VoiceController(IVoiceService voice, IVoiceTokens tokens, TableClient client, Func<string?, bool> isMuted, bool enabled)
        {
            _voice = voice;
            _tokens = tokens;
            _client = client;
            _isMuted = isMuted;
            _enabled = enabled;
            _voice.Speaking += OnSpeaking;
            _voice.Dropped += OnDropped;
            _voice.TokenExpiring += OnTokenExpiring;
            _client.Model.Changed += Evaluate;
            _state = Compute();
        }

        public event Action? Changed;

        /// <summary>The channel opened; the mode is the analytics label.</summary>
        public event Action<VoiceMode>? Joined;

        public VoiceState State => _state;

        public bool MicOn => _micOn;

        /// <summary>Push-to-talk is held.</summary>
        public bool Talking => _talking;

        /// <summary>Spectators listen only (GDD 11.4).</summary>
        public bool CanSpeak => _client.Model.MySeat != null;

        /// <summary>The table's preset decides; without one, open mic in private rooms and push-to-talk elsewhere.</summary>
        public VoiceMode Mode => _client.Model.Selection?.Settings?.VoiceDefault switch
        {
            Protocol.VoiceDefault.Off => VoiceMode.Off,
            Protocol.VoiceDefault.OpenMic => VoiceMode.OpenMic,
            Protocol.VoiceDefault.PushToTalk => VoiceMode.PushToTalk,
            _ => _client.Model.IsPrivate ? VoiceMode.OpenMic : VoiceMode.PushToTalk,
        };

        /// <summary>The microphone is going out now.</summary>
        public bool Transmitting => _inChannel && _micOn && CanSpeak && (Mode == VoiceMode.OpenMic || _talking);

        public bool IsSpeaking(string? playerId) => playerId != null && _speaking.Contains(playerId);

        /// <summary>
        /// The mic button: turns my mic on (asking for the microphone the first time) or off. After a failure
        /// or a silence leave, the same tap brings voice back.
        /// </summary>
        public async void ToggleMic()
        {
            if (_state == VoiceState.Hidden || _state == VoiceState.Suspended || !CanSpeak)
            {
                return;
            }

            if (_micOn && (_state == VoiceState.Failed || _state == VoiceState.Paused))
            {
                // E25: the tap retries rather than turning the mic off.
                _heldSignature = null;
                Evaluate();
                return;
            }

            if (_micOn)
            {
                SetMic(false);
                return;
            }

            if (!await _voice.AskMicrophoneAsync())
            {
                _noPermission = true;
                Evaluate();
                return;
            }

            if (_disposed)
            {
                return;
            }

            _noPermission = false;
            SetMic(true);
        }

        /// <summary>Push-to-talk pressed or released.</summary>
        public void SetTalking(bool held)
        {
            if (_talking == held)
            {
                return;
            }

            _talking = held;
            ApplyTransmit();
            Changed?.Invoke();
        }

        /// <summary>The mute list changed: apply it to everyone at the table.</summary>
        public void MutesChanged()
        {
            if (_inChannel)
            {
                ApplyMutes();
            }
        }

        public void Tick(float deltaSeconds)
        {
            if (_disposed)
            {
                return;
            }

            if (_resendIn > 0f)
            {
                _resendIn -= deltaSeconds;
            }

            if (_inChannel && _state == VoiceState.Live)
            {
                _quiet = _speaking.Count > 0 ? 0f : _quiet + deltaSeconds;
                if (_quiet >= SilenceSeconds)
                {
                    _heldSignature = Signature();
                    _ = Leave();
                }
            }

            ResendMicIfLost();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _voice.Speaking -= OnSpeaking;
            _voice.Dropped -= OnDropped;
            _voice.TokenExpiring -= OnTokenExpiring;
            _client.Model.Changed -= Evaluate;
            if (_inChannel)
            {
                _inChannel = false;
                _ = _voice.LeaveAsync();
            }
        }

        private void SetMic(bool on)
        {
            _micOn = on;
            _heldSignature = null;
            _client.SetMic(on);
            _resendIn = ResendSeconds;
            Evaluate();
        }

        /// <summary>A rejoin starts the seat with its mic off; say it again.</summary>
        private void ResendMicIfLost()
        {
            var me = MySeatInfo();
            if (_resendIn > 0f || me == null || !me.Value.Connected || me.Value.Mic == _micOn)
            {
                return;
            }

            _client.SetMic(_micOn);
            _resendIn = ResendSeconds;
        }

        private void Evaluate()
        {
            if (_disposed)
            {
                return;
            }

            if (_inChannel && _joinedSeated != CanSpeak)
            {
                // Seated or unseated: the token's right to speak changed, so join again.
                _ = Leave();
                return;
            }

            if (_inChannel && CanSpeak && _micOn && !_joinedWithMic)
            {
                // Joined to listen, so the microphone was never opened: join again to send.
                _ = Leave();
                return;
            }

            var want = WantChannel();
            if (want && !_inChannel && _state != VoiceState.Joining)
            {
                _ = Join();
                return;
            }

            if (!want && _inChannel)
            {
                _ = Leave();
                return;
            }

            if (_inChannel)
            {
                ApplyMutes();
                ApplyTransmit();
            }

            SetState(Compute());
        }

        private bool WantChannel()
        {
            if (!Available() || _suspended)
            {
                return false;
            }

            if (_heldSignature != null)
            {
                if (_heldSignature == Signature())
                {
                    return false;
                }

                _heldSignature = null;
            }

            return MicsOn() >= 2;
        }

        private bool Available() => _enabled && _voice.Supported && Mode != VoiceMode.Off;

        private VoiceState Compute()
        {
            if (!Available())
            {
                return VoiceState.Hidden;
            }

            if (_suspended)
            {
                return VoiceState.Suspended;
            }

            if (_inChannel)
            {
                return VoiceState.Live;
            }

            if (_heldSignature != null)
            {
                return _state == VoiceState.Failed ? VoiceState.Failed : VoiceState.Paused;
            }

            if (_noPermission)
            {
                return VoiceState.NoPermission;
            }

            return _micOn ? VoiceState.Waiting : VoiceState.Idle;
        }

        /// <summary>Mics on at the table: the others as the server reports them, mine as I set it.</summary>
        private int MicsOn()
        {
            var count = _micOn && CanSpeak ? 1 : 0;
            foreach (var seat in _client.Model.Seats)
            {
                if (seat.Seat != _client.Model.MySeat && seat.Occupied && seat.Connected && seat.Mic)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Who has a mic on; a silence leave or a failure waits until this changes.</summary>
        private string Signature()
        {
            var ids = new List<string>();
            foreach (var seat in _client.Model.Seats)
            {
                if (seat.Seat != _client.Model.MySeat && seat.Occupied && seat.Connected && seat.Mic)
                {
                    ids.Add(seat.PlayerId!);
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return (_micOn ? "1|" : "0|") + string.Join(",", ids);
        }

        private async Awaitable Join()
        {
            SetState(VoiceState.Joining);
            var cancellation = _lifetime.Token;
            var seated = CanSpeak;
            try
            {
                var ticket = await _tokens.TokenAsync(_client.Ticket.MatchId, cancellation);
                if (_disposed)
                {
                    return;
                }

                if (!ticket.IsOk)
                {
                    _suspended = ticket.Error.Code == "voice.suspended";
                    Fail();
                    return;
                }

                // Only a seat with its mic on opens the microphone; everyone else listens.
                var withMic = seated && _micOn;
                var joined = await _voice.JoinAsync(ticket.Value, withMic, cancellation);
                _joinedWithMic = withMic;
                if (_disposed)
                {
                    if (joined.IsOk)
                    {
                        _ = _voice.LeaveAsync();
                    }

                    return;
                }

                if (!joined.IsOk)
                {
                    Fail();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _inChannel = true;
            _joinedSeated = seated;
            _quiet = 0f;
            SetState(VoiceState.Live);
            Joined?.Invoke(Mode);
            Evaluate();
        }

        private async Awaitable Leave()
        {
            _inChannel = false;
            _speaking.Clear();
            SetState(Compute());
            await _voice.LeaveAsync();
            Evaluate();
        }

        private void Fail()
        {
            _heldSignature = Signature();
            _state = VoiceState.Failed;
            SetState(Compute());
        }

        private void OnDropped()
        {
            if (!_inChannel)
            {
                return;
            }

            _inChannel = false;
            _speaking.Clear();
            Fail();
        }

        private async void OnTokenExpiring()
        {
            if (_disposed || !_inChannel)
            {
                return;
            }

            try
            {
                var ticket = await _tokens.TokenAsync(_client.Ticket.MatchId, _lifetime.Token);
                if (ticket.IsOk && _inChannel)
                {
                    _voice.RenewToken(ticket.Value.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OnSpeaking(string account, bool speaking)
        {
            var changed = speaking ? _speaking.Add(account) : _speaking.Remove(account);
            if (speaking)
            {
                _quiet = 0f;
            }

            if (changed)
            {
                Changed?.Invoke();
            }
        }

        private void ApplyTransmit() => _voice.SetTransmit(Transmitting);

        private void ApplyMutes()
        {
            foreach (var seat in _client.Model.Seats)
            {
                if (seat.Occupied && seat.Seat != _client.Model.MySeat)
                {
                    _voice.SetPeerMuted(seat.PlayerId!, _isMuted(seat.PlayerId));
                }
            }
        }

        private SeatInfo? MySeatInfo()
        {
            var mine = _client.Model.MySeat;
            if (mine == null || mine.Value >= _client.Model.Seats.Count)
            {
                return null;
            }

            return _client.Model.Seats[(int)mine.Value];
        }

        private void SetState(VoiceState state)
        {
            _state = state;
            Changed?.Invoke();
        }
    }
}
