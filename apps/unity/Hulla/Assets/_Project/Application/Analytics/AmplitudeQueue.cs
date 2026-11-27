using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Hulla.Protocol;

namespace Hulla.Application.Analytics
{
    /// <summary>What Amplitude is told about the device with every event; nothing that names the player.</summary>
    public sealed class AmplitudeDevice
    {
        public AmplitudeDevice(string deviceId, string platform, string osName, string osVersion, string deviceModel, string appVersion, string language, string country)
        {
            DeviceId = deviceId;
            Platform = platform;
            OsName = osName;
            OsVersion = osVersion;
            DeviceModel = deviceModel;
            AppVersion = appVersion;
            Language = language;
            Country = country;
        }

        /// <summary>A random id made on this device after consent; not the advertising or hardware id.</summary>
        public string DeviceId { get; }

        public string Platform { get; }

        public string OsName { get; }

        public string OsVersion { get; }

        public string DeviceModel { get; }

        public string AppVersion { get; }

        public string Language { get; }

        /// <summary>The region set on the device. No IP address is sent, so Amplitude does no lookup of its own.</summary>
        public string Country { get; }
    }

    /// <summary>
    /// Events on their way to Amplitude's HTTP V2 API (docs/design/analytics-events.md): each is written
    /// once as a line of JSON with its time, session and a unique insert id, held until a request that
    /// carried it comes back 200, and sent again after a failure (the insert id stops duplicates).
    /// No network or disk here: the sink posts <see cref="NextRequest"/> and reports the status.
    /// </summary>
    public sealed class AmplitudeQueue
    {
        /// <summary>The free plan takes ten events a request.</summary>
        public const int MaxPerRequest = 10;

        /// <summary>Held at most, offline or refused; the oldest go first.</summary>
        public const int MaxHeld = 500;

        /// <summary>A gap this long between events starts a new session, as Amplitude's own SDKs do.</summary>
        public const long SessionGapMs = 5 * 60 * 1000;

        public const long ThrottledWaitMs = 30_000;
        public const long FirstRetryMs = 5_000;
        public const long LongestRetryMs = 300_000;

        private readonly string _apiKey;
        private readonly Func<long> _nowMs;
        private readonly Func<string> _newId;
        private readonly List<string> _held = new List<string>();
        private readonly AmplitudeDevice _device;
        private string? _userId;
        private long _sessionId;
        private long _lastEventMs;
        private long _waitUntilMs;
        private int _failures;
        private int _perRequest = MaxPerRequest;
        private int _inFlight;

        public AmplitudeQueue(string apiKey, AmplitudeDevice device, Func<long> nowMs, Func<string> newId)
        {
            _apiKey = apiKey;
            _device = device;
            _nowMs = nowMs;
            _newId = newId;
        }

        /// <summary>The events not yet accepted, oldest first, one JSON object a line: what the sink keeps on disk.</summary>
        public IReadOnlyList<string> Held => _held;

        public bool Sending => _inFlight > 0;

        /// <summary>Events kept from the last run go out first.</summary>
        public void Restore(IEnumerable<string> lines)
        {
            var at = 0;
            foreach (var line in lines)
            {
                var text = line.Trim();
                if (text.Length > 2 && text[0] == '{' && text[text.Length - 1] == '}')
                {
                    _held.Insert(at++, text);
                }
            }

            Trim();
        }

        public void Enqueue(AnalyticsEvent analyticsEvent)
        {
            var json = Begin(analyticsEvent.Name);
            json.Append(",\"event_properties\":");
            AppendObject(json, analyticsEvent.Parameters);
            json.Append('}');
            Hold(json.ToString());
        }

        /// <summary>
        /// The account for the events from now on, and the properties that changed, as one $identify
        /// event. With nothing to set nothing is sent: the id already goes with every event.
        /// </summary>
        public void Identify(string? userId, IReadOnlyDictionary<string, object> properties)
        {
            _userId = userId != null && userId.Length >= 5 ? userId : null;
            if (properties.Count == 0)
            {
                return;
            }

            var json = Begin("$identify");
            json.Append(",\"user_properties\":{\"$set\":");
            AppendObject(json, properties);
            json.Append("}}");
            Hold(json.ToString());
        }

        /// <summary>The body of the next request, or null when nothing is held, one is on its way, or a retry is waiting.</summary>
        public string? NextRequest()
        {
            if (_held.Count == 0 || _inFlight > 0 || _nowMs() < _waitUntilMs)
            {
                return null;
            }

            _inFlight = Math.Min(_perRequest, _held.Count);
            var body = new StringBuilder();
            body.Append("{\"api_key\":");
            AppendString(body, _apiKey);
            body.Append(",\"events\":[");
            for (var i = 0; i < _inFlight; i++)
            {
                if (i > 0)
                {
                    body.Append(',');
                }

                body.Append(_held[i]);
            }

            body.Append("]}");
            return body.ToString();
        }

        /// <summary>
        /// The answer to the last request. 200 lets the events go; 400 drops them (they would be refused
        /// again); 413 halves the request; 429 waits half a minute; anything else, a dead network
        /// (status 0) included, waits longer each time.
        /// </summary>
        public void Completed(int status)
        {
            var sent = Math.Min(_inFlight, _held.Count);
            _inFlight = 0;
            switch (status)
            {
                case 200:
                    _held.RemoveRange(0, sent);
                    _failures = 0;
                    _perRequest = MaxPerRequest;
                    break;
                case 400:
                    _held.RemoveRange(0, sent);
                    break;
                case 413:
                    if (sent <= 1)
                    {
                        _held.RemoveRange(0, sent);
                    }
                    else
                    {
                        _perRequest = Math.Max(1, sent / 2);
                    }

                    break;
                case 429:
                    _waitUntilMs = _nowMs() + ThrottledWaitMs;
                    break;
                default:
                    _waitUntilMs = _nowMs() + Math.Min(LongestRetryMs, FirstRetryMs << Math.Min(_failures, 6));
                    _failures++;
                    break;
            }
        }

        /// <summary>Consent withdrawn: nothing held, no account, a fresh session next time.</summary>
        public void Clear()
        {
            _held.Clear();
            _inFlight = 0;
            _userId = null;
            _sessionId = 0;
            _lastEventMs = 0;
            _waitUntilMs = 0;
            _failures = 0;
        }

        private StringBuilder Begin(string eventType)
        {
            var now = _nowMs();
            if (_sessionId == 0 || now - _lastEventMs > SessionGapMs)
            {
                _sessionId = now;
            }

            _lastEventMs = now;
            var json = new StringBuilder(256);
            json.Append("{\"event_type\":");
            AppendString(json, eventType);
            json.Append(",\"time\":").Append(now.ToString(CultureInfo.InvariantCulture));
            json.Append(",\"session_id\":").Append(_sessionId.ToString(CultureInfo.InvariantCulture));
            Field(json, "insert_id", _newId());
            Field(json, "device_id", _device.DeviceId);
            if (_userId != null)
            {
                Field(json, "user_id", _userId);
            }

            Field(json, "platform", _device.Platform);
            Field(json, "os_name", _device.OsName);
            Field(json, "os_version", _device.OsVersion);
            Field(json, "device_model", _device.DeviceModel);
            Field(json, "app_version", _device.AppVersion);
            Field(json, "language", _device.Language);
            Field(json, "country", _device.Country);
            return json;
        }

        private void Hold(string line)
        {
            _held.Add(line);
            Trim();
        }

        private void Trim()
        {
            // Never the ones a request is carrying: Completed removes those by position.
            while (_held.Count > MaxHeld && _held.Count > _inFlight)
            {
                _held.RemoveAt(_inFlight);
            }
        }

        private static void Field(StringBuilder json, string key, string value)
        {
            if (value.Length == 0)
            {
                return;
            }

            json.Append(",\"").Append(key).Append("\":");
            AppendString(json, value);
        }

        private static void AppendObject(StringBuilder json, IReadOnlyDictionary<string, object> values)
        {
            json.Append('{');
            var first = true;
            foreach (var pair in values)
            {
                var value = Scalar(pair.Value);
                if (value == null)
                {
                    continue;
                }

                if (!first)
                {
                    json.Append(',');
                }

                first = false;
                AppendString(json, pair.Key);
                json.Append(':').Append(value);
            }

            json.Append('}');
        }

        /// <summary>A string, flag or number as JSON; anything else (and a number JSON cannot hold) is left out.</summary>
        private static string? Scalar(object value)
        {
            switch (value)
            {
                case string text:
                    var quoted = new StringBuilder(text.Length + 2);
                    AppendString(quoted, text.Length > 1024 ? text.Substring(0, 1024) : text);
                    return quoted.ToString();
                case bool flag:
                    return flag ? "true" : "false";
                case int or long or short or byte:
                    return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                case float or double:
                    var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return double.IsNaN(number) || double.IsInfinity(number) ? null : number.ToString("0.###", CultureInfo.InvariantCulture);
                default:
                    return null;
            }
        }

        private static void AppendString(StringBuilder json, string text)
        {
            json.Append('"');
            foreach (var c in text)
            {
                switch (c)
                {
                    case '"':
                        json.Append("\\\"");
                        break;
                    case '\\':
                        json.Append("\\\\");
                        break;
                    case '\n':
                        json.Append("\\n");
                        break;
                    case '\r':
                        json.Append("\\r");
                        break;
                    case '\t':
                        json.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            json.Append(c);
                        }

                        break;
                }
            }

            json.Append('"');
        }
    }
}
