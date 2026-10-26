using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hulla.Presentation.Audio
{
    /// <summary>
    /// Plays the table's sounds (GDD 16.2) from a small pool of sources on a persistent object, so
    /// overlapping sounds keep their own pitch. The volume comes from the SFX setting; 0 skips the
    /// call. Sounds duck while someone else is talking on voice (GDD 11.4). Clips are built once from
    /// <see cref="SoundKit"/>.
    /// </summary>
    public sealed class SoundPlayer : IDisposable
    {
        private const int Voices = 4;

        /// <summary>Table sounds play at this share of their level under speech.</summary>
        public const float DuckLevel = 0.3f;
        private readonly Dictionary<Sound, AudioClip> _clips = new Dictionary<Sound, AudioClip>();
        private readonly List<AudioSource> _sources = new List<AudioSource>();
        private GameObject? _host;
        private int _next;

        /// <summary>0 to 1; 0 is off.</summary>
        public float Volume { get; set; } = 0.7f;

        /// <summary>Someone else is talking on voice.</summary>
        public bool Ducked { get; set; }

        public void Play(Sound sound, float pitch = 1f)
        {
            if (Volume <= 0f)
            {
                return;
            }

            var source = Source();
            source.pitch = pitch;
            source.PlayOneShot(Clip(sound), Ducked ? Volume * DuckLevel : Volume);
        }

        /// <summary>
        /// The app's one audio listener: the scenes carry no camera with one, and without a listener
        /// Unity plays nothing (table sounds and voice alike).
        /// </summary>
        public static void EnsureListener()
        {
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() != null)
            {
                return;
            }

            var host = new GameObject("HullaListener");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<AudioListener>();
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
