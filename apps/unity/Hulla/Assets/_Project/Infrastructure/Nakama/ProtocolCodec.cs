using System;
using System.Collections.Generic;
using System.Text;
using Hulla.Application.Services;
using Hulla.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hulla.Infrastructure.Nakama
{
    /// <summary>Envelopes to and from the wire, with payloads read into the generated DTOs.</summary>
    public sealed class ProtocolCodec
    {
        private static readonly Dictionary<string, Type> PayloadTypes = new Dictionary<string, Type>
        {
            [MessageTypes.TableState] = typeof(TableState),
            [MessageTypes.RulesChanged] = typeof(RulesChanged),
            [MessageTypes.Presence] = typeof(Presence),
            [MessageTypes.HostChanged] = typeof(HostChanged),
            [MessageTypes.TableClosed] = typeof(TableClosed),
            [MessageTypes.Error] = typeof(Error),
            [MessageTypes.Snapshot] = typeof(Snapshot),
            [MessageTypes.Pong] = typeof(Pong),
            [MessageTypes.Chat] = typeof(Chat),
            [MessageTypes.QuickChatSent] = typeof(QuickChatSent),
            [MessageTypes.EmoteSent] = typeof(EmoteSent),
            [MessageTypes.RoundStart] = typeof(RoundStart),
            [MessageTypes.TurnStart] = typeof(TurnStart),
            [MessageTypes.MoveApplied] = typeof(MoveApplied),
            [MessageTypes.MoveRejected] = typeof(MoveRejected),
            [MessageTypes.TrickResolved] = typeof(TrickResolved),
            [MessageTypes.Pickup] = typeof(Pickup),
            [MessageTypes.Escape] = typeof(Escape),
            [MessageTypes.HandTaken] = typeof(HandTaken),
            [MessageTypes.PrivateDraw] = typeof(PrivateDraw),
            [MessageTypes.EndgameStarted] = typeof(EndgameStarted),
            [MessageTypes.Confirmed] = typeof(Confirmed),
            [MessageTypes.RoundEnd] = typeof(RoundEnd),
        };

        private readonly JsonSerializer _serializer = ProtocolJson.CreateSerializer();

        /// <summary>Reads one envelope; null when the type is unknown to this client or the JSON is malformed.</summary>
        public ServerMessage? Decode(byte[] data)
        {
            try
            {
                var envelope = JObject.Parse(Encoding.UTF8.GetString(data));
                return DecodeEnvelope(envelope);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public string Encode(string type, string tableId, long seq, long ts, object payload)
        {
            var envelope = new JObject
            {
                ["v"] = 1,
                ["type"] = type,
                ["tableId"] = tableId,
                ["seq"] = seq,
                ["ts"] = ts,
                ["payload"] = JObject.FromObject(payload, _serializer),
            };
            return envelope.ToString(Formatting.None);
        }

        private ServerMessage? DecodeEnvelope(JObject envelope)
        {
            var type = (string?)envelope["type"];
            var seq = (long?)envelope["seq"] ?? 0;
            var payload = envelope["payload"];
            if (type == null || payload == null || !PayloadTypes.TryGetValue(type, out var dto))
            {
                return null;
            }

            var value = payload.ToObject(dto, _serializer);
            if (value is Snapshot snapshot)
            {
                return new ServerMessage(type, seq, snapshot);
            }

            return value == null ? (ServerMessage?)null : new ServerMessage(type, seq, value);
        }
    }
}
