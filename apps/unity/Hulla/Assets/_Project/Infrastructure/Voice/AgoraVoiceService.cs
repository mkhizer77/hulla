#if HULLA_AGORA
using System;
using System.Collections.Generic;
using System.Threading;
using Agora.Rtc;
using Hulla.Application.Voice;
using Hulla.Domain;
using UnityEngine;

namespace Hulla.Infrastructure.Voice
{
    /// <summary>
    /// The Agora adapter (ADR-0012), compiled only with the `HULLA_AGORA` define once the SDK is imported
    /// (docs/runbooks/voice-setup.md). One engine for the run; one channel per table; players join with
    /// their user id as the Agora account, so a speaker maps back to a seat. Speech-tier audio with echo
    /// cancellation and noise suppression on by the chat-room scenario (GDD 11.4). SDK callbacks arrive on
    /// its own thread and are posted to the main thread before anything else sees them.
    /// </summary>
    public sealed class AgoraVoiceService : IVoiceService, IDisposable
    {
        /// <summary>Volume reports four times a second; a remote level above this counts as speech.</summary>
        private const int ReportMs = 250;
        private const int SpeechLevel = 30;
        private const float JoinTimeoutSeconds = 10f;

        private readonly SynchronizationContext _main;
        private readonly Dictionary<uint, string> _accounts = new Dictionary<uint, string>();
        private readonly Dictionary<string, bool> _muted = new Dictionary<string, bool>();
        private readonly HashSet<string> _remoteSpeaking = new HashSet<string>();
        private IRtcEngine? _engine;
        private string? _appId;
        private string? _account;
        private bool _localSpeaking;
        private bool _transmitting;
        private AwaitableCompletionSource<bool>? _joining;

        public AgoraVoiceService()
        {
            _main = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        public bool Supported => true;

        public event Action<string, bool>? Speaking;

        public event Action? Dropped;

        public event Action? TokenExpiring;

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
            var engine = EngineFor(ticket.AppId);
            if (engine == null)
            {
                return Result<bool>.Fail("voice.unavailable", "engine");
            }

            _account = ticket.Account;
            _transmitting = false;
            var options = new ChannelMediaOptions();
            options.channelProfile.SetValue(CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_LIVE_BROADCASTING);
            options.clientRoleType.SetValue(publish ? CLIENT_ROLE_TYPE.CLIENT_ROLE_BROADCASTER : CLIENT_ROLE_TYPE.CLIENT_ROLE_AUDIENCE);
            options.publishMicrophoneTrack.SetValue(publish);
            options.autoSubscribeAudio.SetValue(true);
            options.autoSubscribeVideo.SetValue(false);
            engine.MuteLocalAudioStream(true);
            _joining = new AwaitableCompletionSource<bool>();
            var code = engine.JoinChannelWithUserAccount(ticket.Token, ticket.Channel, ticket.Account, options);
            if (code != 0)
            {
                _joining = null;
                return Result<bool>.Fail("voice.unavailable", "join_" + code);
            }

            var joined = _joining;
            _ = TimeOut(joined, cancellation);
            var ok = await joined.Awaitable;
            _joining = null;
            if (!ok)
            {
                engine.LeaveChannel();
                return Result<bool>.Fail("voice.unavailable", "join");
            }

            foreach (var muted in _muted)
            {
                ApplyMute(muted.Key, muted.Value);
            }

            return Result<bool>.Ok(true);
        }

        public async Awaitable LeaveAsync()
        {
            _engine?.LeaveChannel();
            _accounts.Clear();
            _transmitting = false;
            SetLocalSpeaking(false);
            foreach (var account in new List<string>(_remoteSpeaking))
            {
                _remoteSpeaking.Remove(account);
                Speaking?.Invoke(account, false);
            }

            _account = null;
            await Awaitable.NextFrameAsync();
        }

        public void RenewToken(string token) => _engine?.RenewToken(token);

        public void SetTransmit(bool on)
        {
            _transmitting = on;
            _engine?.MuteLocalAudioStream(!on);
            if (!on)
            {
                SetLocalSpeaking(false);
            }
        }

        public void SetPeerMuted(string account, bool muted)
        {
            _muted[account] = muted;
            ApplyMute(account, muted);
        }

        public void Dispose()
        {
            if (_engine == null)
            {
                return;
            }

            _engine.InitEventHandler(null);
            _engine.LeaveChannel();
            _engine.Dispose();
            _engine = null;
        }

        private IRtcEngine? EngineFor(string appId)
        {
            if (_engine != null && _appId == appId)
            {
                return _engine;
            }

            Dispose();
            var engine = RtcEngine.CreateAgoraRtcEngine();
            var context = new RtcEngineContext
            {
                appId = appId,
                channelProfile = CHANNEL_PROFILE_TYPE.CHANNEL_PROFILE_LIVE_BROADCASTING,
                audioScenario = AUDIO_SCENARIO_TYPE.AUDIO_SCENARIO_CHATROOM,
            };
            var code = engine.Initialize(context);
            if (code != 0)
            {
                Debug.LogWarning($"voice: the engine did not start ({code})");
                engine.Dispose();
                return null;
            }

            engine.InitEventHandler(new Handler(this));
            engine.EnableAudio();
            engine.SetAudioProfile(AUDIO_PROFILE_TYPE.AUDIO_PROFILE_SPEECH_STANDARD, AUDIO_SCENARIO_TYPE.AUDIO_SCENARIO_CHATROOM);
            engine.EnableAudioVolumeIndication(ReportMs, 3, true);
            engine.SetDefaultAudioRouteToSpeakerphone(true);
            _engine = engine;
            _appId = appId;
            return engine;
        }

        private async Awaitable TimeOut(AwaitableCompletionSource<bool> joining, CancellationToken cancellation)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(JoinTimeoutSeconds, cancellation);
            }
            catch (OperationCanceledException)
            {
            }

            joining.TrySetResult(false);
        }

        private void ApplyMute(string account, bool muted)
        {
            foreach (var entry in _accounts)
            {
                if (entry.Value == account)
                {
                    _engine?.MuteRemoteAudioStream(entry.Key, muted);
                }
            }
        }

        private void SetLocalSpeaking(bool speaking)
        {
            if (_localSpeaking == speaking || _account == null)
            {
                return;
            }

            _localSpeaking = speaking;
            Speaking?.Invoke(_account, speaking);
        }

        private void Post(Action action) => _main.Post(_ => action(), null);

        private void OnJoined() => _joining?.TrySetResult(true);

        private void OnUserInfo(uint uid, string account)
        {
            _accounts[uid] = account;
            if (_muted.TryGetValue(account, out var muted))
            {
                _engine?.MuteRemoteAudioStream(uid, muted);
            }
        }

        private void OnUserOffline(uint uid)
        {
            if (_accounts.TryGetValue(uid, out var account) && _remoteSpeaking.Remove(account))
            {
                Speaking?.Invoke(account, false);
            }

            _accounts.Remove(uid);
        }

        /// <summary>The local report carries uid 0 and the voice-activity flag; the remote one carries levels.</summary>
        private void OnVolumes(AudioVolumeInfo[] speakers)
        {
            if (speakers.Length == 1 && speakers[0].uid == 0)
            {
                SetLocalSpeaking(_transmitting && speakers[0].vad == 1);
                return;
            }

            var now = new HashSet<string>();
            foreach (var speaker in speakers)
            {
                if (speaker.uid != 0 && speaker.volume >= SpeechLevel && _accounts.TryGetValue(speaker.uid, out var account)
                    && !(_muted.TryGetValue(account, out var muted) && muted))
                {
                    now.Add(account);
                }
            }

            foreach (var account in new List<string>(_remoteSpeaking))
            {
                if (!now.Contains(account))
                {
                    _remoteSpeaking.Remove(account);
                    Speaking?.Invoke(account, false);
                }
            }

            foreach (var account in now)
            {
                if (_remoteSpeaking.Add(account))
                {
                    Speaking?.Invoke(account, true);
                }
            }
        }

        private void OnFailed()
        {
            if (_joining != null)
            {
                _joining.TrySetResult(false);
                return;
            }

            Dropped?.Invoke();
        }

        private sealed class Handler : IRtcEngineEventHandler
        {
            private readonly AgoraVoiceService _owner;

            public Handler(AgoraVoiceService owner)
            {
                _owner = owner;
            }

            public override void OnJoinChannelSuccess(RtcConnection connection, int elapsed) => _owner.Post(_owner.OnJoined);

            public override void OnUserInfoUpdated(uint uid, UserInfo info)
            {
                var account = info.userAccount;
                _owner.Post(() => _owner.OnUserInfo(uid, account));
            }

            public override void OnUserOffline(RtcConnection connection, uint remoteUid, USER_OFFLINE_REASON_TYPE reason) =>
                _owner.Post(() => _owner.OnUserOffline(remoteUid));

            public override void OnAudioVolumeIndication(RtcConnection connection, AudioVolumeInfo[] speakers, uint speakerNumber, int totalVolume)
            {
                var copy = (AudioVolumeInfo[])speakers.Clone();
                _owner.Post(() => _owner.OnVolumes(copy));
            }

            public override void OnTokenPrivilegeWillExpire(RtcConnection connection, string token) =>
                _owner.Post(() => _owner.TokenExpiring?.Invoke());

            public override void OnConnectionStateChanged(RtcConnection connection, CONNECTION_STATE_TYPE state, CONNECTION_CHANGED_REASON_TYPE reason)
            {
                if (state == CONNECTION_STATE_TYPE.CONNECTION_STATE_FAILED)
                {
                    _owner.Post(_owner.OnFailed);
                }
            }

            public override void OnError(int err, string msg) => Debug.LogWarning($"voice: error {err}");
        }
    }
}
#endif
