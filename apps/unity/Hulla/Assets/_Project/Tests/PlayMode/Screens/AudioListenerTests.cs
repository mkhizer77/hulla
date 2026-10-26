using System.Collections;
using Hulla.Presentation.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hulla.Screens.Tests
{
    /// <summary>The scenes carry no listener, so the app makes exactly one; without it nothing plays.</summary>
    public sealed class AudioListenerTests
    {
        [UnityTest]
        public IEnumerator TheAppHasExactlyOneAudioListener()
        {
            SoundPlayer.EnsureListener();
            SoundPlayer.EnsureListener();
            yield return null;
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);
        }
    }
}
