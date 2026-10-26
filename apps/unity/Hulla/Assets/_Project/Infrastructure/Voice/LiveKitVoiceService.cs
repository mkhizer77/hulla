using System;
using System.Collections.Generic;
using System.Threading;
using Hulla.Application.Voice;
using Hulla.Domain;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;
using RoomOptions = LiveKit.RoomOptions;

namespace Hulla.Infrastructure.Voice
{
    /// <summary>
    /// The LiveKit adapter (ADR-0024). One room per table, joined with the player's user id as the identity,
    /// so a speaker maps back to a seat. The platform audio module does echo cancellation, noise suppression
    /// and gain control and plays remote audio by itself. The microphone records only once this player first
    /// sends, so listeners never light the recording indicator; holding the mic mutes the published track.
    /// A muted player is not subscribed at all. LiveKit refreshes the token of a connected client, so
    /// <see cref="TokenExpiring"/> never fires. Room events arrive on the main thread.
    /// </summary>
    public sealed class LiveKitVoiceService : IVoiceService, IDisposable
    {
        private const float JoinTimeoutSeconds = 10f;

        private readonly Dictionary<string, bool> _muted = new Dictionary<string, bool>();
        private readonly HashSet<string> _speaking = new HashSet<string>();
        private PlatformAudio? _audio;
        private PlatformAudioSource? _source;
        private LocalAudioTrack? _track;
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
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
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
            if (!UnityEngine.Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                await UnityEngine.Application.RequestUserAuthorization(UserAuthorization.Microphone);
            }

            return UnityEngine.Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
            await Awaitable.NextFrameAsync();
            return true;
#endif
        }

        public async Awaitable<Result<bool>> JoinAsync(VoiceTicket ticket, bool publish, CancellationToken cancellation)
        {
            await LeaveAsync();
            if (!EnsureAudio())
            {
                return Result<bool>.Fail("voice.unavailable", "audio");
            }

            var room = new Room();
            room.TrackPublished += OnTrackPublished;
            room.ActiveSpeakersChanged += OnSpeakers;
            room.ParticipantDisconnected += OnParticipantLeft;
            room.DisconnectedWithReason += OnDisconnected;
            _room = room;
            _account = ticket.Account;
            _transmitting = false;

            var options = new RoomOptions { AutoSubscribe = false, Dynacast = false, AdaptiveStream = false };
            if (!await Finished(room.Connect(ticket.Url, ticket.Token, options), cancellation) || _room != room)
            {
                Drop(room);
                return Result<bool>.Fail("voice.unavailable", "connect");
            }

            foreach (var participant in room.RemoteParticipants.Values)
            {
                foreach (var publication in participant.Tracks.Values)
                {
                    Subscribe(publication, participant.Identity);
                }
            }

            if (publish && !await PublishMicrophone(room, cancellation))
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
            if (on && !_recording && _audio != null && _track != null)
            {
                _recording = StartRecording(_audio);
            }

            (_track as ILocalTrack)?.SetMute(!on);
            if (!on && _account != null && _speaking.Remove(_account))
            {
                Speaking?.Invoke(_account, false);
            }
        }

        public void SetPeerMuted(string account, bool muted)
        {
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

        public void Dispose()
        {
            if (_room != null)
            {
                Drop(_room);
            }

            _audio?.Dispose();
            _audio = null;
        }

        /// <summary>The audio module is made once, before the first room (remote audio plays only through it).</summary>
        private bool EnsureAudio()
        {
            if (_audio != null)
            {
                return true;
            }

            try
            {
                _audio = new PlatformAudio();
                // It starts recording on creation; nothing records until this player first sends.
                _audio.StopRecording();
                _recording = false;
                return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"voice: no audio device ({error.Message})");
                _audio = null;
                return false;
            }
        }

        private async Awaitable<bool> PublishMicrophone(Room room, CancellationToken cancellation)
        {
            if (_audio == null)
            {
                return false;
            }

            var processing = new AudioProcessingOptions { EchoCancellation = true, NoiseSuppression = true, AutoGainControl = true };
            _source = new PlatformAudioSource(_audio, processing);
            _track = LocalAudioTrack.CreateAudioTrack("mic", _source, room);
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

        private static bool StartRecording(PlatformAudio audio)
        {
            try
            {
                // Permission was asked before joining, so this runs without yielding.
                var steps = audio.StartRecording();
                while (steps.MoveNext())
                {
                }

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

        private void Subscribe(RemoteTrackPublication publication, string account)
        {
            if (publication.Kind != TrackKind.KindAudio)
            {
                return;
            }

            publication.SetSubscribed(!(_muted.TryGetValue(account, out var muted) && muted));
        }

        private void Drop(Room room)
        {
            room.TrackPublished -= OnTrackPublished;
            room.ActiveSpeakersChanged -= OnSpeakers;
            room.ParticipantDisconnected -= OnParticipantLeft;
            room.DisconnectedWithReason -= OnDisconnected;
            room.Disconnect();
            if (_room == room)
            {
                _room = null;
                _track = null;
                _source?.Dispose();
                _source = null;
                if (_recording && _audio != null)
                {
                    _audio.StopRecording();
                    _recording = false;
                }
            }
        }

        private void OnTrackPublished(RemoteTrackPublication publication, RemoteParticipant participant) =>
            Subscribe(publication, participant.Identity);

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
    }
}
