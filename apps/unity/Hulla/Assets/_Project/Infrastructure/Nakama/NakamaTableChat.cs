using System;
using System.Threading;
using Hulla.Application.Services;
using Hulla.Domain;
using Nakama;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>The table's room channel, named after the match id, as the server's chat test joins it.</summary>
    public sealed class NakamaTableChat : ITableChat, IDisposable
    {
        private readonly NakamaConnection _connection;
        private ISocket? _socket;
        private IChannel? _channel;

        public NakamaTableChat(NakamaConnection connection)
        {
            _connection = connection;
        }

        public event Action<TextLine>? Received;

        public async Awaitable<Result<bool>> JoinAsync(string matchId, CancellationToken cancellation)
        {
            var socket = _connection.Socket;
            if (socket == null)
            {
                return Result<bool>.Fail("session.not_signed_in");
            }

            Detach();
            _socket = socket;
            socket.ReceivedChannelMessage += OnMessage;
            try
            {
                _channel = await socket.JoinChatAsync(matchId, ChannelType.Room, false, false);
                return Result<bool>.Ok(true);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"table chat join failed: {error.Message}");
                return Result<bool>.Fail("chat.unavailable", error.GetType().Name);
            }
        }

        public async Awaitable<Result<bool>> SendAsync(string text, CancellationToken cancellation)
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > ITableChat.MaxLength)
            {
                return Result<bool>.Fail("table.invalid_payload");
            }

            if (_socket == null || _channel == null)
            {
                return Result<bool>.Fail("chat.unavailable");
            }

            try
            {
                await _socket.WriteChatMessageAsync(_channel, new JObject { ["text"] = trimmed }.ToString(Newtonsoft.Json.Formatting.None));
                return Result<bool>.Ok(true);
            }
            catch (Exception error)
            {
                // The before-message hook refuses with its code as the message (server hooks/before_channel_message).
                foreach (var code in new[] { "network.rate_limited", "auth.not_linked", "content.filtered" })
                {
                    if (error.Message.Contains(code))
                    {
                        return Result<bool>.Fail(code);
                    }
                }

                return Result<bool>.Fail("chat.refused", error.Message);
            }
        }

        public async Awaitable LeaveAsync()
        {
            var socket = _socket;
            var channel = _channel;
            Detach();
            if (socket != null && channel != null && socket.IsConnected)
            {
                try
                {
                    await socket.LeaveChatAsync(channel);
                }
                catch (Exception error)
                {
                    Debug.LogWarning($"table chat leave failed: {error.Message}");
                }
            }
        }

        public void Dispose() => Detach();

        private void OnMessage(IApiChannelMessage message)
        {
            if (_channel == null || message.ChannelId != _channel.Id)
            {
                return;
            }

            string? text;
            try
            {
                text = (string?)JObject.Parse(message.Content)["text"];
            }
            catch (Newtonsoft.Json.JsonException)
            {
                text = null;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                Received?.Invoke(new TextLine(message.SenderId, message.Username, text!));
            }
        }

        private void Detach()
        {
            if (_socket != null)
            {
                _socket.ReceivedChannelMessage -= OnMessage;
            }

            _socket = null;
            _channel = null;
        }
    }
}
