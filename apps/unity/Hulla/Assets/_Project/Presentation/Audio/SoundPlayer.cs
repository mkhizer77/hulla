using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hulla.Presentation.Audio
{
    /// <summary>
    /// Plays the table's sounds (GDD 16.2) from a small pool of sources on a persistent object, so
    /// overlapping sounds keep their own pitch. The volume comes from the SFX setting; 0 skips the
    /// call. Clips are built once from <see cref="SoundKit"/>.
    /// </summary>
    public sealed class SoundPlayer : IDisposable
    {
        private const int Voices = 4;
        private readonly Dictionary<Sound, AudioClip> _clips = new Dictionary<Sound, AudioClip>();
        private readonly List<AudioSource> _sources = new List<AudioSource>();
        private GameObject? _host;
        private int _next;

        /// <summary>0 to 1; 0 is off.</summary>
        public float Volume { get; set; } = 0.7f;

        public void Play(Sound sound, float pitch = 1f)
        {
            if (Volume <= 0f)
            {
                return;
            }

            var source = Source();
            source.pitch = pitch;
            source.PlayOneShot(Clip(sound), Volume);
        }

        public void Dispose()
        {
            foreach (var clip in _clips.Values)
            {
                UnityEngine.Object.Destroy(clip);
            }

            _clips.Clear();
            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host);
                _host = null;
            }
        }

        private AudioClip Clip(Sound sound)
        {
            if (!_clips.TryGetValue(sound, out var clip))
            {
                var samples = SoundKit.Samples(sound);
                clip = AudioClip.Create(sound.ToString(), samples.Length, 1, SoundKit.SampleRate, false);
                clip.SetData(samples, 0);
                _clips[sound] = clip;
            }

            return clip;
        }

        private AudioSource Source()
        {
            if (_host == null)
            {
                _host = new GameObject("HullaSound");
                UnityEngine.Object.DontDestroyOnLoad(_host);
                for (var i = 0; i < Voices; i++)
                {
                    var source = _host.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.spatialBlend = 0f;
                    _sources.Add(source);
                }
            }

            var chosen = _sources[_next];
            _next = (_next + 1) % _sources.Count;
            return chosen;
        }
    }
}
