using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    public sealed class AudioClockPlayModeTests
    {
        [UnityTest]
        public IEnumerator RuntimeClip_ExercisesFullTransportAndReplay()
        {
            GameObject gameObject = new GameObject("AudioClockPlayModeTests");
            AudioClip clip = null;
            try
            {
                gameObject.AddComponent<AudioListener>();
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.mute = true;
                clip = AudioClip.Create(
                    "AudioClockPlayModeClip",
                    4800,
                    1,
                    48000,
                    false);
                source.clip = clip;

                Component clock = RuntimeReflection.AddComponent(
                    gameObject,
                    "SashimiBoy.AudioClock");
                RuntimeReflection.SetField(clock, "audioSource", source);
                RuntimeReflection.SetField(clock, "scheduledLeadTime", 0.02d);

                Assert.That(Command(clock, "Play"), Is.True);
                Assert.That(StateName(clock), Is.EqualTo("Scheduled"));
                Assert.That(SongTimeMs(clock), Is.LessThanOrEqualTo(0d));
                double scheduledTime = SongTimeMs(clock);
                Assert.That(Command(clock, "Play"), Is.False);
                Assert.That(SongTimeMs(clock),
                    Is.GreaterThanOrEqualTo(scheduledTime));

                yield return WaitForState(clock, "Playing", 0.5f);
                double beforePause = SongTimeMs(clock);
                Assert.That(Command(clock, "Pause"), Is.True);
                Assert.That(StateName(clock), Is.EqualTo("Paused"));
                double pausedTime = SongTimeMs(clock);
                yield return new WaitForSecondsRealtime(0.04f);
                Assert.That(SongTimeMs(clock),
                    Is.EqualTo(pausedTime).Within(2d));

                Assert.That(Command(clock, "Resume"), Is.True);
                yield return WaitForState(clock, "Playing", 0.5f);
                Assert.That(SongTimeMs(clock),
                    Is.GreaterThanOrEqualTo(beforePause - 2d));

                Assert.That(Command(clock, "Stop"), Is.True);
                Assert.That(StateName(clock), Is.EqualTo("Stopped"));
                Assert.That(SongTimeMs(clock), Is.Zero);

                Assert.That(Command(clock, "Replay"), Is.True);
                yield return WaitForState(clock, "Finished", 1f);
                Assert.That(SongTimeMs(clock), Is.EqualTo(100d).Within(2d));

                Assert.That(Command(clock, "Replay"), Is.True);
                yield return WaitForState(clock, "Finished", 1f);
                Assert.That(SongTimeMs(clock), Is.EqualTo(100d).Within(2d));
            }
            finally
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.Destroy(gameObject);
                }

                if (clip != null)
                {
                    UnityEngine.Object.Destroy(clip);
                }
            }

            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MissingResources_FaultOnceWithoutExceptionsOrSpam()
        {
            GameObject gameObject = new GameObject(
                "AudioClockMissingResourcesPlayModeTests");
            try
            {
                Component clock = RuntimeReflection.AddComponent(
                    gameObject,
                    "SashimiBoy.AudioClock");
                LogAssert.Expect(LogType.Error, "MissingAudioSource");
                Assert.That(Command(clock, "Play"), Is.False);
                Assert.That(StateName(clock), Is.EqualTo("Faulted"));
                Assert.That(Command(clock, "Play"), Is.False);
                Assert.That(LastError(clock),
                    Is.EqualTo("MissingAudioSource"));

                Assert.That(Command(clock, "Stop"), Is.True);
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                RuntimeReflection.SetField(clock, "audioSource", source);
                LogAssert.Expect(LogType.Error, "MissingClip");
                Assert.That(Command(clock, "Replay"), Is.False);
                Assert.That(StateName(clock), Is.EqualTo("Faulted"));
                Assert.That(Command(clock, "Replay"), Is.False);
                Assert.That(LastError(clock), Is.EqualTo("MissingClip"));
            }
            finally
            {
                UnityEngine.Object.Destroy(gameObject);
            }

            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Stage01ExistingStartPath_StartsAudioClock()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                "Stage01_Salmon",
                LoadSceneMode.Single);
            yield return load;
            yield return null;

            Component clock = RuntimeReflection.FindActiveComponent(
                "SashimiBoy.AudioClock");
            Assert.That(clock, Is.Not.Null);
            Assert.That(StateName(clock),
                Is.EqualTo("Scheduled").Or.EqualTo("Playing"));
            Assert.That(Convert.ToBoolean(GetProperty(clock, "IsRunning")),
                Is.True);
            Assert.That(LastError(clock), Is.Empty);

            AudioSource source = (AudioSource)RuntimeReflection.GetField(
                clock,
                "audioSource");
            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.Not.Null);
            LogAssert.NoUnexpectedReceived();
        }

        private static IEnumerator WaitForState(
            Component clock,
            string expected,
            float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (StateName(clock) != expected &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(StateName(clock), Is.EqualTo(expected));
        }

        private static bool Command(Component clock, string name)
        {
            return Convert.ToBoolean(RuntimeReflection.Invoke(clock, name));
        }

        private static object GetProperty(
            Component clock,
            string propertyName)
        {
            PropertyInfo property = clock.GetType().GetProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            return property.GetValue(clock);
        }

        private static string StateName(Component clock)
        {
            return GetProperty(clock, "State").ToString();
        }

        private static double SongTimeMs(Component clock)
        {
            return Convert.ToDouble(GetProperty(clock, "SongTimeMs"));
        }

        private static string LastError(Component clock)
        {
            return Convert.ToString(GetProperty(clock, "LastError"));
        }
    }
}
