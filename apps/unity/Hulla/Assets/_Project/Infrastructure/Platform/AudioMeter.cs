using UnityEngine;

namespace Hulla.Infrastructure.Platform
{
    /// <summary>
    /// Development builds only: logs when the app's own mixed output starts and stops sounding, so a phone
    /// turned down can be told apart from an app that plays nothing (<c>adb logcat -s Unity</c>, or the
    /// Mac's Player.log). Table sounds and voices both pass through Unity's mix.
    /// </summary>
    public sealed class AudioMeter : MonoBehaviour
    {
        private const float Threshold = 0.002f;
        private const float QuietSeconds = 1f;
        private readonly float[] _samples = new float[1024];
        private bool _sounding;
        private float _peak;
        private float _quietFor;

        public static void Attach()
        {
            if (!Debug.isDebugBuild || FindAnyObjectByType<AudioMeter>() != null)
            {
                return;
            }

            var host = new GameObject("HullaAudioMeter");
            DontDestroyOnLoad(host);
            host.AddComponent<AudioMeter>();
        }

        private void Update()
        {
            AudioListener.GetOutputData(_samples, 0);
            var peak = 0f;
            foreach (var sample in _samples)
            {
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            if (peak > Threshold)
            {
                _quietFor = 0f;
                _peak = Mathf.Max(_peak, peak);
                if (!_sounding)
                {
                    _sounding = true;
                    Debug.Log($"audio: sounding (peak {peak:0.000}, listener volume {AudioListener.volume:0.00}, paused {AudioListener.pause})");
                }

                return;
            }

            if (_sounding && (_quietFor += Time.unscaledDeltaTime) >= QuietSeconds)
            {
                Debug.Log($"audio: quiet (loudest {_peak:0.000})");
                _sounding = false;
                _peak = 0f;
            }
        }
    }
}
