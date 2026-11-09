using System;
using Hulla.Application.Voice;
using Hulla.Presentation.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hulla.Presentation.Voice
{
    /// <summary>
    /// The mic button's gestures, the same at the table and in the lobby (docs/design/voice-ux.md). With
    /// open mic a tap turns the mic on or off; on a push-to-talk table the button only does hold-to-talk,
    /// and the first hold also turns the mic on. A tap after a failure retries either way.
    /// </summary>
    public sealed class MicControl : IDisposable
    {
        /// <summary>How long the line about the microphone shows after the first tap.</summary>
        public const float WhySeconds = 4f;

        /// <summary>How long "Hold to talk" shows over the mic once voice is offered by holding.</summary>
        public const float TipSeconds = 6f;

        private readonly Button _button;
        private readonly VoiceController _voice;
        private readonly Action _changed;
        private readonly Label _tip;
        private readonly float _shownAt;
        private bool _held;
        private float _askedAt = float.NegativeInfinity;
        private bool _renderedAsking;
        private bool _renderedTip;

        /// <param name="tipClass">The class of the small label over the mic ("Hold to talk", "Talking").</param>
        public MicControl(Button button, VoiceController voice, Action changed, string tipClass)
        {
            _button = button;
            _voice = voice;
            _changed = changed;
            _shownAt = Time.unscaledTime;
            _tip = new Label { pickingMode = PickingMode.Ignore };
            _tip.AddToClassList(tipClass);
            _tip.style.display = DisplayStyle.None;
            _button.Add(_tip);
            _button.clicked += OnClicked;
            _button.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            _button.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            _button.RegisterCallback<PointerLeaveEvent>(OnLeave);
            _button.RegisterCallback<PointerCancelEvent>(OnCancel);
        }

        /// <summary>Push-to-talk: the button is held, not tapped.</summary>
        public bool HoldMode => _voice.Mode == VoiceMode.PushToTalk;

        /// <summary>The mic was just asked for: the pill explains what it is used for.</summary>
        public bool Asking
        {
            get
            {
                // Voice is often live at once now, so the line shows for its time unless something went wrong.
                var state = _voice.State;
                return Time.unscaledTime - _askedAt < WhySeconds && !_voice.MicRefused
                    && state != VoiceState.Failed && state != VoiceState.Suspended && state != VoiceState.NoPermission;
            }
        }

        /// <summary>The tip or the why line is due to change: the view should render again.</summary>
        public bool Stale => Asking != _renderedAsking || TipWanted != _renderedTip;

        private bool TipWanted =>
            HoldMode && _voice.CanSpeak && _voice.State != VoiceState.Hidden && _voice.State != VoiceState.Suspended
            && (_voice.Talking || Time.unscaledTime - _shownAt < TipSeconds);

        /// <summary>Updates the tip over the mic; returns whether the why line is showing.</summary>
        public bool Render()
        {
            _renderedTip = TipWanted;
            _tip.text = _voice.Talking ? UiText.Talking : UiText.HoldToTalk;
            _tip.style.display = _renderedTip ? DisplayStyle.Flex : DisplayStyle.None;
            _renderedAsking = Asking;
            return _renderedAsking;
        }

        /// <summary>The voice pill's line for this state; empty when there is nothing to say.</summary>
        public static string Line(VoiceController voice, bool asking)
        {
            var state = voice.State;
            if (state == VoiceState.Hidden)
            {
                return "";
            }

            if (asking)
            {
                return UiText.VoiceWhy;
            }

            if (voice.MicRefused && state != VoiceState.Suspended)
            {
                return UiText.VoiceNoPermission;
            }

            return state switch
            {
                VoiceState.Waiting => UiText.VoiceWaiting,
                VoiceState.Joining => UiText.VoiceJoining,
                VoiceState.Failed => UiText.VoiceFailed,
                VoiceState.Suspended => UiText.VoiceSuspended,
                VoiceState.NoPermission => UiText.VoiceNoPermission,
                VoiceState.Live when !voice.CanSpeak => UiText.VoiceListening,
                _ => "",
            };
        }

        /// <summary>The button's word: what a hold does on push-to-talk tables, else whether the mic is on.</summary>
        public string Label()
        {
            if (_voice.MicOn && _voice.State == VoiceState.Failed)
            {
                return UiText.VoiceRetry;
            }

            if (HoldMode && _voice.CanSpeak)
            {
                return _voice.Talking ? UiText.Talking : UiText.HoldToTalk;
            }

            return _voice.MicOn ? UiText.MicOn : UiText.MicOff;
        }

        public void Dispose()
        {
            Release();
            _button.clicked -= OnClicked;
            _button.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            _button.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            _button.UnregisterCallback<PointerLeaveEvent>(OnLeave);
            _button.UnregisterCallback<PointerCancelEvent>(OnCancel);
            _tip.RemoveFromHierarchy();
        }

        private void OnDown(PointerDownEvent _)
        {
            if (!HoldMode || !_voice.CanSpeak || _voice.State == VoiceState.Failed)
            {
                return;
            }

            _held = true;
            if (!_voice.MicOn)
            {
                _askedAt = Time.unscaledTime;
            }

            _voice.HoldToTalk(true);
            _changed();
        }

        private void OnUp(PointerUpEvent _) => Release();

        private void OnLeave(PointerLeaveEvent _) => Release();

        private void OnCancel(PointerCancelEvent _) => Release();

        private void Release()
        {
            if (!_held)
            {
                return;
            }

            _held = false;
            _voice.HoldToTalk(false);
            _changed();
        }

        private void OnClicked()
        {
            // On push-to-talk tables the hold does the work; a tap only retries a failed channel.
            if (HoldMode && !(_voice.MicOn && _voice.State == VoiceState.Failed))
            {
                return;
            }

            if (!_voice.MicOn)
            {
                _askedAt = Time.unscaledTime;
            }

            _voice.ToggleMic();
            _changed();
        }
    }
}
