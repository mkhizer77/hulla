using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Hulla.Application.Analytics;
using Hulla.Infrastructure.Settings;
using Hulla.Protocol;
using UnityEngine;
using UnityEngine.Networking;

namespace Hulla.Infrastructure.Analytics
{
    /// <summary>
    /// Amplitude over its HTTP V2 API, no SDK (ADR-0025, docs/runbooks/amplitude-setup.md). Events wait in
    /// a file until Amplitude takes them, so a round played offline is counted on the next launch.
    /// The device id is a random one made here after consent and thrown away when consent is withdrawn.
    /// The account's id is not sent: the privacy policy says usage data carries none (docs/legal). A
    /// build that links the two must change that text first (<c>linkAccount</c>).
    /// Per-move events stay out (<see cref="AnalyticsEvents.HighVolume"/>).
    /// </summary>
    public sealed class AmplitudeSink : IAnalyticsSink
    {
        public const string EuEndpoint = "https://api.eu.amplitude.com/2/httpapi";
        public const string UsEndpoint = "https://api2.amplitude.com/2/httpapi";
        public const string DeviceIdKey = "hulla.analytics.device";

        private readonly string _apiKey;
        private readonly string _endpoint;
        private readonly string _file;
        private readonly bool _linkAccount;
        private AmplitudeQueue? _queue;
        private int _generation;
        private bool _pumping;

        public AmplitudeSink(string apiKey, bool eu, bool linkAccount = false)
        {
            _linkAccount = linkAccount;
            _apiKey = apiKey;
            _endpoint = eu ? EuEndpoint : UsEndpoint;
            _file = Path.Combine(UnityEngine.Application.persistentDataPath, DeviceProfile.Key("amplitude-events") + ".jsonl");
        }

        /// <summary>Events not yet accepted by Amplitude.</summary>
        public int Held => _queue?.Held.Count ?? 0;

        /// <summary>The HTTP status of the last request (0 before the first, or when it got no answer).</summary>
        public int LastStatus { get; private set; }

        public void Send(IReadOnlyList<AnalyticsEvent> batch)
        {
            var queue = Queue();
            foreach (var analyticsEvent in batch)
            {
                if (!AnalyticsEvents.HighVolume.Contains(analyticsEvent.Name))
                {
                    queue.Enqueue(analyticsEvent);
                }
            }

            Save(queue);
            Pump(queue);
        }

        public void Identify(string? userId, IReadOnlyDictionary<string, object> properties)
        {
            var queue = Queue();
            queue.Identify(_linkAccount ? userId : null, properties);
            Save(queue);
            Pump(queue);
        }

        public void Forget()
        {
            _generation++;
            _queue?.Clear();
            _queue = null;
            PlayerPrefs.DeleteKey(DeviceProfile.Key(DeviceIdKey));
            try
            {
                File.Delete(_file);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"amplitude: could not delete held events: {error.Message}");
            }
        }

        /// <summary>Made on the first event after consent, with the device id and whatever the last run could not send.</summary>
        private AmplitudeQueue Queue()
        {
            if (_queue != null)
            {
                return _queue;
            }

            _queue = new AmplitudeQueue(_apiKey, Device(), () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), () => Guid.NewGuid().ToString("N"));
            try
            {
                if (File.Exists(_file))
                {
                    _queue.Restore(File.ReadAllLines(_file));
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning($"amplitude: could not read held events: {error.Message}");
            }

            return _queue;
        }

        private static AmplitudeDevice Device()
        {
            var key = DeviceProfile.Key(DeviceIdKey);
            var id = PlayerPrefs.GetString(key, "");
            if (id.Length == 0)
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(key, id);
            }

            var platform = UnityEngine.Application.platform switch
            {
                RuntimePlatform.Android => "Android",
                RuntimePlatform.IPhonePlayer => "iOS",
                RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor => "macOS",
                RuntimePlatform.WebGLPlayer => "Web",
                _ => UnityEngine.Application.platform.ToString(),
            };
            var region = "";
            try
            {
                region = RegionInfo.CurrentRegion.TwoLetterISORegionName;
            }
            catch (ArgumentException)
            {
                // A device with no region set has none to report.
            }

            return new AmplitudeDevice(
                id,
                platform,
                platform,
                SystemInfo.operatingSystem,
                SystemInfo.deviceModel,
                UnityEngine.Application.version,
                UnityEngine.Application.systemLanguage.ToString(),
                region);
        }

        private void Save(AmplitudeQueue queue)
        {
            try
            {
                File.WriteAllLines(_file, queue.Held);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"amplitude: could not keep events: {error.Message}");
            }
        }

        /// <summary>One request at a time until nothing is held or a retry is waiting; the next batch starts it again.</summary>
        private async void Pump(AmplitudeQueue queue)
        {
            if (_pumping)
            {
                return;
            }

            _pumping = true;
            try
            {
                while (queue.NextRequest() is string body)
                {
                    var generation = _generation;
                    var status = await PostAsync(body);
                    if (generation != _generation)
                    {
                        // Consent was withdrawn while the request was in the air: that queue is gone.
                        return;
                    }

                    LastStatus = status;
                    queue.Completed(status);
                    Save(queue);
                }
            }
            finally
            {
                _pumping = false;
            }
        }

        /// <returns>The HTTP status, or 0 when the request never got an answer.</returns>
        private async Awaitable<int> PostAsync(string body)
        {
            try
            {
                using var request = new UnityWebRequest(_endpoint, UnityWebRequest.kHttpVerbPOST);
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 20;
                await request.SendWebRequest();
                var status = (int)request.responseCode;
                if (status != 200 && Debug.isDebugBuild)
                {
                    Debug.LogWarning($"amplitude: {status} {request.error}");
                }

                return status;
            }
            catch (Exception error)
            {
                Debug.LogWarning($"amplitude: {error.Message}");
                return 0;
            }
        }
    }
}
