using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Voice;
using Hulla.Domain;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;
using Object = UnityEngine.Object;
using RoomOptions = LiveKit.RoomOptions;

namespace Hulla.Infrastructure.Voice
{
    /// <summary>
    /// The LiveKit adapter (ADR-0024). One room per table, joined with the player's user id as the identity,
    /// so a speaker maps back to a seat. Room events arrive on the main thread.
    /// <para>
    /// Everything goes through Unity's audio, so levels are ordinary <see cref="AudioSource"/> volumes: each
    /// remote voice plays from its own hidden source (the player's level times the voice level), and a seat
    /// with its mic on sends Unity's microphone through the SDK's audio processing (echo cancellation
    /// against what Unity plays, table sounds included, plus noise suppression and gain control). The
    /// microphone opens only at the first send, so a listener never sees the permission prompt; a listener
    /// who turns a mic on is joined again by the controller. Holding mutes the published track.
    /// </para>
    /// A muted player, or everyone while the speaker is off, is not subscribed at all. LiveKit refreshes the
    /// token of a connected client, so <see cref="TokenExpiring"/> never fires.
    /// </summary>
    public sealed class LiveKitVoiceService : IVoiceService, IDisposable
    {
        private const float JoinTimeoutSeconds = 10f;

        /// <summary>A join that timed out may still connect; it is closed once it does, within this long.</summary>
        private const float LateConnectSeconds = 60f;

        private readonly Dictionary<string, bool> _muted = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _subscribed = new Dictionary<string, bool>();
        private readonly Dictionary<string, Listener> _listeners = new Dictionary<string, Listener>();
        private readonly Dictionary<string, float> _peerVolume = new Dictionary<string, float>();
        private readonly HashSet<string> _speaking = new HashSet<string>();
        private GameObject? _micHost;
        private MicrophoneSource? _mic;
        private LocalAudioTrack? _track;
        private float _outputVolume = 1f;
        private bool _listening = true;
        private Room? _room;
        private string? _account;
        private bool _recording;
        private bool _transmitting;

        public bool Supported => true;

        public event Action<string, bool>? Speaking;

        public event Action? Dropped;

#pragma warning disable CS0067 // LiveKit renews the token itself while connected.
        public event Action? TokenExpiring;
#pragma warning restore CS0067

        public async Awaitable<bool> AskMicrophoneAsync()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (MicrophoneAllowed())
            {
                return true;
            }

            var answer = new AwaitableCompletionSource<bool>();
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => answer.TrySetResult(true);
            callbacks.PermissionDenied += _ => answer.TrySetResult(false);
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
            return await answer.Awaitable;
#elif UNITY_IOS && !UNITY_EDITOR
            if (!MicrophoneAllowed())
            {
                await UnityEngine.Application.RequestUserAuthorization(UserAuthorization.Microphone);
            }

            return MicrophoneAllowed();
#else
            await Awaitable.NextFrameAsync();
            return true;
#endif
        }

        /// <param name="publish">This seat sends its microphone; false listens without touching it.</param>
        public async Awaitable<Result<bool>> JoinAsync(VoiceTicket ticket, bool publish, CancellationToken cancellation)
        {
            await LeaveAsync();
            var withMic = publish && MicrophoneAllowed() && Microphone.devices.Length > 0;
            var room = new Room();
            room.TrackPublished += OnTrackPublished;
            room.TrackSubscribed += OnTrackSubscribed;
            room.TrackUnsubscribed += OnTrackUnsubscribed;
            room.ActiveSpeakersChanged += OnSpeakers;
            room.ParticipantDisconnected += OnParticipantLeft;
            room.DisconnectedWithReason += OnDisconnected;
            _room = room;
            _account = ticket.Account;
            _transmitting = false;

            var options = new RoomOptions { AutoSubscribe = false, Dynacast = false, AdaptiveStream = false };
            var connect = room.Connect(ticket.Url, ticket.Token, options);
            if (!await Finished(connect, cancellation) || _room != room)
            {
                Drop(room);
                if (!connect.IsDone)
                {
                    _ = CloseWhenConnected(room, connect);
                }

                return Result<bool>.Fail("voice.unavailable", "connect");
            }

            foreach (var participant in room.RemoteParticipants.Values)
            {
                foreach (var publication in participant.Tracks.Values)
                {
                    Subscribe(publication, participant.Identity);
                }
            }

            if (withMic && !await PublishMicrophone(room, cancellation))
            {
                Drop(room);
                return Result<bool>.Fail("voice.unavailable", "publish");
            }

            return Result<bool>.Ok(true);
        }

        public async Awaitable LeaveAsync()
        {
            if (_room != null)
            {
                Drop(_room);
            }

            foreach (var account in new List<string>(_speaking))
            {
                _speaking.Remove(account);
                Speaking?.Invoke(account, false);
            }

            _account = null;
            _transmitting = false;
            await Awaitable.NextFrameAsync();
        }

        public void RenewToken(string token)
        {
        }

        public void SetTransmit(bool on)
        {
            _transmitting = on;
            if (on && !_recording && _mic != null && _track != null && MicrophoneAllowed())
            {
                _recording = StartRecording(_mic);
            }

            (_track as ILocalTrack)?.SetMute(!on);
            if (!on && _account != null && _speaking.Remove(_account))
            {
                Speaking?.Invoke(_account, false);
            }
        }

        public void SetPeerMuted(string account, bool muted)
        {
            if (_muted.TryGetValue(account, out var was) && was == muted)
            {
                return;
            }

            _muted[account] = muted;
            if (_room == null || !_room.RemoteParticipants.TryGetValue(account, out var participant))
            {
                return;
            }

            foreach (var publication in participant.Tracks.Values)
            {
                Subscribe(publication, account);
            }

            if (muted && _speaking.Remove(account))
            {
                Speaking?.Invoke(account, false);
            }
        }

        public void SetPeerVolume(string account, float volume)
        {
            _peerVolume[account] = Mathf.Clamp01(volume);
            ApplyVolumes();
        }

        public void SetOutputVolume(float volume)
        {
            _outputVolume = Mathf.Clamp01(volume);
            ApplyVolumes();
        }

        public void SetListening(bool on)
        {
            if (_listening == on)
            {
                return;
            }

            _listening = on;
            if (_room == null)
            {
                return;
            }

            foreach (var participant in _room.RemoteParticipants.Values)
            {
                foreach (var publication in participant.Tracks.Values)
                {
                    Subscribe(publication, participant.Identity);
                }
            }
        }

        public void Dispose()
        {
            if (_room != null)
            {
                Drop(_room);
            }

            if (_micHost != null)
            {
                Object.Destroy(_micHost);
                _micHost = null;
            }
        }

        private static bool MicrophoneAllowed()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#elif UNITY_IOS && !UNITY_EDITOR
            return UnityEngine.Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
            return true;
#endif
        }

        private async Awaitable<bool> PublishMicrophone(Room room, CancellationToken cancellation)
        {
            if (_micHost == null)
            {
                _micHost = new GameObject("voice-mic") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(_micHost);
            }

            var processing = new AudioProcessingOptions { EchoCancellation = true, NoiseSuppression = true, AutoGainControl = true };
            _mic = new MicrophoneSource(Microphone.devices[0], _micHost, processing);
            _track = LocalAudioTrack.CreateAudioTrack("mic", _mic, room);
            ((ILocalTrack)_track).SetMute(true);
            var options = new TrackPublishOptions { Source = TrackSource.SourceMicrophone, Dtx = true, Red = true };
            if (!await Finished(room.LocalParticipant.PublishTrack(_track, options), cancellation) || _room != room)
            {
                return false;
            }

            // The mic may have been switched on while the track was on its way.
            SetTransmit(_transmitting);
            return true;
        }

        private static bool StartRecording(MicrophoneSource mic)
        {
            try
            {
                // Called only with the permission granted, so this never prompts.
                mic.Start();
                return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"voice: the microphone did not start ({error.Message})");
                return false;
            }
        }

        private static async Awaitable<bool> Finished(LiveKit.Internal.Threading.YieldInstruction instruction, CancellationToken cancellation)
        {
            var deadline = Time.realtimeSinceStartup + JoinTimeoutSeconds;
            while (!instruction.IsDone)
            {
                if (cancellation.IsCancellationRequested || Time.realtimeSinceStartup > deadline)
                {
                    return false;
                }

                await Awaitable.NextFrameAsync();
            }

            return !instruction.IsError;
        }

        /// <summary>A disconnect is ignored while a connect is pending, so a late connect is closed when it lands.</summary>
        private static async Awaitable CloseWhenConnected(Room room, LiveKit.Internal.Threading.YieldInstruction connect)
        {
            var deadline = Time.realtimeSinceStartup + LateConnectSeconds;
            while (!connect.IsDone && Time.realtimeSinceStartup < deadline)
            {
                await Awaitable.NextFrameAsync();
            }

            room.Disconnect();
        }

        private void Subscribe(TrackPublication publication, string account)
        {
            if (publication.Kind != TrackKind.KindAudio || publication is not RemoteTrackPublication remote)
            {
                return;
            }

            var want = _listening && !(_muted.TryGetValue(account, out var muted) && muted);
            if (_subscribed.TryGetValue(remote.Sid, out var current) && current == want)
            {
                return;
            }

            _subscribed[remote.Sid] = want;
            remote.SetSubscribed(want);
        }

        private void Drop(Room room)
        {
            room.TrackPublished -= OnTrackPublished;
            room.TrackSubscribed -= OnTrackSubscribed;
            room.TrackUnsubscribed -= OnTrackUnsubscribed;
            room.ActiveSpeakersChanged -= OnSpeakers;
            room.ParticipantDisconnected -= OnParticipantLeft;
            room.DisconnectedWithReason -= OnDisconnected;
            room.Disconnect();
            if (_room != room)
            {
                return;
            }

            _room = null;
            _track = null;
            if (_mic != null)
            {
                if (_recording)
                {
                    _mic.Stop();
                }

                _mic.Dispose();
                _mic = null;
            }

            _recording = false;
            foreach (var listener in _listeners.Values)
            {
                listener.Dispose();
            }

            _listeners.Clear();
            _subscribed.Clear();
        }

        private void OnTrackPublished(RemoteTrackPublication publication, RemoteParticipant participant) =>
            Subscribe(publication, participant.Identity);

        /// <summary>Each voice plays through its own Unity audio source at the player's level.</summary>
        private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            if (track is not RemoteAudioTrack audio || _listeners.ContainsKey(publication.Sid))
            {
                return;
            }

            try
            {
                var listener = new Listener(audio, participant.Identity);
                _listeners[publication.Sid] = listener;
                listener.Volume = LevelOf(participant.Identity);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"voice: a voice could not play ({error.Message})");
            }
        }

        private void OnTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            if (_listeners.Remove(publication.Sid, out var listener))
            {
                listener.Dispose();
            }
        }

        private void OnParticipantLeft(Participant participant)
        {
            if (_speaking.Remove(participant.Identity))
            {
                Speaking?.Invoke(participant.Identity, false);
            }
        }

        private void OnSpeakers(List<Participant> speakers)
        {
            var now = new HashSet<string>();
            foreach (var speaker in speakers)
            {
                // The list can name someone who has already left.
                if (speaker == null)
                {
                    continue;
                }

                var account = speaker.Identity;
                var mine = account == _account;
                if ((mine && _transmitting) || (!mine && !(_muted.TryGetValue(account, out var muted) && muted)))
                {
                    now.Add(account);
                }
            }

            foreach (var account in new List<string>(_speaking))
            {
                if (!now.Contains(account))
                {
                    _speaking.Remove(account);
                    Speaking?.Invoke(account, false);
                }
            }

            foreach (var account in now)
            {
                if (_speaking.Add(account))
                {
                    Speaking?.Invoke(account, true);
                }
            }
        }

        private float LevelOf(string account) =>
            _outputVolume * (_peerVolume.TryGetValue(account, out var level) ? level : 1f);

        private void ApplyVolumes()
        {
            foreach (var listener in _listeners.Values)
            {
                listener.Volume = LevelOf(listener.Account);
            }
        }

        /// <summary>The SDK reconnects by itself; a disconnect we did not ask for means it gave up.</summary>
        private void OnDisconnected(Room room, DisconnectReason reason)
        {
            if (reason == DisconnectReason.ClientInitiated || room != _room)
            {
                return;
            }

            Drop(room);
            Dropped?.Invoke();
        }

        /// <summary>One remote voice played through a hidden Unity audio source.</summary>
        private sealed class Listener : IDisposable
        {
            private readonly GameObject _host;
            private readonly AudioSource _source;
            private readonly AudioStream _stream;

            public Listener(RemoteAudioTrack track, string account)
            {
                Account = account;
                _host = new GameObject("voice") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(_host);
                _source = _host.AddComponent<AudioSource>();
                _source.spatialBlend = 0f;
                _stream = new AudioStream(track, _source);
            }

            public string Account { get; }

            public float Volume
            {
                set => _source.volume = value;
            }

            public void Dispose()
            {
                _stream.Dispose();
                Object.Destroy(_host);
            }
        }
    }
}
