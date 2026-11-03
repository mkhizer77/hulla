using System;
using System.Threading;
using Hulla.Application.Services;
using UnityEngine;

namespace Hulla.Application.ViewModels
{
    /// <summary>The phone number screen: add a number, confirm the code, remove it, or switch to its account.</summary>
    public sealed class PhoneViewModel
    {
        private readonly IPhoneService _service;
        private string? _token;

        public PhoneViewModel(IPhoneService service)
        {
            _service = service;
        }

        public enum Stage
        {
            Loading,
            NoNumber,
            CodeSent,
            Linked,
            /// <summary>The number belongs to another account; the player may switch to it.</summary>
            Offer,
            Switched,
        }

        public event Action? Changed;

        /// <summary>Raised once the switch to the number's account is done.</summary>
        public event Action<PlayerSession>? Switched;

        public Stage Current { get; private set; } = Stage.Loading;

        public bool Busy { get; private set; }

        public string? ErrorCode { get; private set; }

        public string Phone { get; private set; } = "";

        public string MaskedPhone { get; private set; } = "";

        public long ResendAfterTs { get; private set; }

        /// <summary>True right after a removal, so the screen can say so.</summary>
        public bool Removed { get; private set; }

        public async Awaitable RefreshAsync(CancellationToken cancellation)
        {
            Begin();
            var status = await _service.StatusAsync(cancellation);
            Finish(status.IsOk ? null : status.Error.Code);
            Current = status.IsOk && status.Value ? Stage.Linked : Stage.NoNumber;
            Changed?.Invoke();
        }

        /// <summary>Where the last code went (whatsapp or sms).</summary>
        public string Channel { get; private set; } = PhoneChannels.Sms;

        /// <summary>WhatsApp failed and the server sent the code by SMS instead.</summary>
        public bool Fallback { get; private set; }

        /// <summary>A code on WhatsApp may not arrive (no WhatsApp on the number): offer SMS when the server has it.</summary>
        public bool CanSendBySms { get; private set; }

        public Awaitable SendAsync(string phone, CancellationToken cancellation)
        {
            Phone = phone.Trim();
            return StartAsync(null, cancellation);
        }

        /// <summary>"Send by SMS instead": the same number, by SMS, within the same resend wait and limits.</summary>
        public Awaitable SendBySmsAsync(CancellationToken cancellation) => StartAsync(PhoneChannels.Sms, cancellation);

        private async Awaitable StartAsync(string? channel, CancellationToken cancellation)
        {
            Begin();
            Removed = false;
            var sent = await _service.StartAsync(Phone, channel, cancellation);
            Finish(sent.IsOk ? null : sent.Error.Code);
            if (sent.IsOk)
            {
                MaskedPhone = sent.Value.MaskedPhone;
                ResendAfterTs = sent.Value.ResendAfterTs;
                Channel = sent.Value.Channel == PhoneChannels.WhatsApp ? PhoneChannels.WhatsApp : PhoneChannels.Sms;
                Fallback = sent.Value.Fallback;
                CanSendBySms = Channel == PhoneChannels.WhatsApp && System.Linq.Enumerable.Contains(sent.Value.Channels, PhoneChannels.Sms);
                Current = Stage.CodeSent;
            }

            Changed?.Invoke();
        }

        public async Awaitable VerifyAsync(string code, CancellationToken cancellation)
        {
            Begin();
            var verified = await _service.VerifyAsync(Phone, code.Trim(), cancellation);
            Finish(verified.IsOk ? null : verified.Error.Code);
            if (verified.IsOk)
            {
                MaskedPhone = verified.Value.MaskedPhone;
                _token = verified.Value.Token;
                Current = verified.Value.Linked || _token == null ? Stage.Linked : Stage.Offer;
            }

            Changed?.Invoke();
        }

        public async Awaitable RemoveAsync(CancellationToken cancellation)
        {
            Begin();
            var removed = await _service.RemoveAsync(cancellation);
            Finish(removed.IsOk ? null : removed.Error.Code);
            if (removed.IsOk)
            {
                Current = Stage.NoNumber;
                Removed = true;
            }

            Changed?.Invoke();
        }

        public async Awaitable SwitchAsync(CancellationToken cancellation)
        {
            if (_token == null)
            {
                return;
            }

            Begin();
            var token = _token;
            _token = null;
            var switched = await _service.SwitchAsync(token, cancellation);
            Finish(switched.IsOk ? null : switched.Error.Code);
            Current = switched.IsOk ? Stage.Switched : Stage.NoNumber;
            Changed?.Invoke();
            if (switched.IsOk)
            {
                Switched?.Invoke(switched.Value);
            }
        }

        /// <summary>Back to entering a number (a different one, or after declining the switch).</summary>
        public void StartOver()
        {
            _token = null;
            ErrorCode = null;
            Current = Stage.NoNumber;
            Changed?.Invoke();
        }

        private void Begin()
        {
            Busy = true;
            ErrorCode = null;
            Changed?.Invoke();
        }

        private void Finish(string? error)
        {
            Busy = false;
            ErrorCode = error;
        }
    }
}
