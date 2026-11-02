#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    public sealed class Stage03AudioRevisionPlayModeTests
    {
        private const string MusicPath = "Assets/_SashimiBoy/Audio/Music/Stage_03_Flounder/flounder1.wav";

        [UnityTest]
        public IEnumerator ApprovedFullClip_LoadsAndDecodesNonSilentSamplesAcrossItsDuration()
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
            Assert.That(clip, Is.Not.Null);
            Assert.That(AssetDatabase.AssetPathToGUID(MusicPath), Is.EqualTo("c4fa323b3fd58a44dbbb5f5f6b64d734"));
            Assert.That(clip.channels, Is.EqualTo(2));
            Assert.That(clip.frequency, Is.EqualTo(48000));
            Assert.That(clip.samples, Is.EqualTo(11084160));
            Assert.That((double)clip.length, Is.EqualTo(230.92d).Within(.001d));
            AudioDataLoadState initialState = clip.loadState;
            bool requestedLoad = false;
            try
            {
                Assert.That(initialState, Is.Not.EqualTo(AudioDataLoadState.Failed));
                if (initialState == AudioDataLoadState.Unloaded)
                {
                    requestedLoad = true;
                    Assert.That(clip.LoadAudioData(), Is.True, "The real imported clip must decode.");
                }
                double deadline = Time.realtimeSinceStartupAsDouble + 20d;
                while (clip.loadState != AudioDataLoadState.Loaded &&
                    clip.loadState != AudioDataLoadState.Failed && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;
                Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
                const int windowFrames = 2048;
                var samples = new float[windowFrames * clip.channels];
                int[] offsets = { 0, clip.samples / 4, clip.samples / 2, clip.samples * 3 / 4, clip.samples - windowFrames };
                float peak = 0f;
                foreach (int offset in offsets)
                {
                    Assert.That(clip.GetData(samples, offset), Is.True, "PCM read failed at frame " + offset);
                    foreach (float value in samples)
                    {
                        Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False);
                        peak = Mathf.Max(peak, Mathf.Abs(value));
                    }
                    yield return null;
                }
                Assert.That(peak, Is.GreaterThan(.000001f), "The approved clip must contain actual decoded audio.");
                // No speaker playback or rhythm chart is involved. Lyrics and sync remain Owner checks.
            }
            finally
            {
                if (requestedLoad && clip != null) clip.UnloadAudioData();
            }
        }
    }
}
#endif
