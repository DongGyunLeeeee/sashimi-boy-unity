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
        public IEnumerator ScheduledStop_CancelsPendingPlaybackPastOriginalDspDeadline()
        {
            const double scheduledLeadSeconds = 0.25d;
            const double minimumDeadlineMarginSeconds = 0.25d;
            const double observationCushionSeconds = 0.1d;
            const float timeoutSeconds = 5f;

            GameObject gameObject = new GameObject(
                "AudioClockScheduledStopPlayModeTests");
            AudioClip clip = null;
            try
            {
                AudioListener existingListener =
                    UnityEngine.Object.FindAnyObjectByType<AudioListener>();
                if (existingListener == null ||
                    !existingListener.isActiveAndEnabled)
                {
                    gameObject.AddComponent<AudioListener>();
                }

                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.mute = true;
                clip = AudioClip.Create(
                    "AudioClockScheduledStopPlayModeClip",
                    96000,
                    1,
                    48000,
                    false);
                source.clip = clip;

                Component clock = RuntimeReflection.AddComponent(
                    gameObject,
                    "SashimiBoy.AudioClock");
                RuntimeReflection.SetField(clock, "audioSource", source);
                RuntimeReflection.SetField(
                    clock,
                    "scheduledLeadTime",
                    scheduledLeadSeconds);

                Assert.That(Command(clock, "Play"), Is.True);
                double controlStartDspTime = Convert.ToDouble(
                    RuntimeReflection.GetField(clock, "startDspTime"));
                float controlTimeout =
                    Time.realtimeSinceStartup + timeoutSeconds;
                while ((AudioSettings.dspTime < controlStartDspTime ||
                    !source.isPlaying ||
                    source.timeSamples <= 0) &&
                    Time.realtimeSinceStartup < controlTimeout)
                {
                    yield return null;
                }

                Assert.That(
                    AudioSettings.dspTime,
                    Is.GreaterThanOrEqualTo(controlStartDspTime),
                    "Control playback did not reach its DSP deadline.");
                Assert.That(
                    source.isPlaying,
                    Is.True,
                    "The control phase must prove this runtime source can play.");
                Assert.That(source.timeSamples, Is.GreaterThan(0));
                Assert.That(StateName(clock), Is.EqualTo("Playing"));
                Assert.That(
                    Convert.ToBoolean(GetProperty(clock, "IsRunning")),
                    Is.True);
                double observedStartLagSeconds = Math.Max(
                    0d,
                    AudioSettings.dspTime - controlStartDspTime);
                double cancellationDeadlineMarginSeconds = Math.Max(
                    minimumDeadlineMarginSeconds,
                    observedStartLagSeconds + observationCushionSeconds);

                Assert.That(Command(clock, "Stop"), Is.True);
                Assert.That(source.isPlaying, Is.False);
                Assert.That(source.timeSamples, Is.Zero);
                Assert.That(StateName(clock), Is.EqualTo("Stopped"));
                Assert.That(SongTimeMs(clock), Is.Zero);
                Assert.That(
                    Convert.ToBoolean(GetProperty(clock, "IsRunning")),
                    Is.False);
                yield return null;
                Assert.That(source.isPlaying, Is.False);
                Assert.That(source.timeSamples, Is.Zero);
                Assert.That(StateName(clock), Is.EqualTo("Stopped"));
                Assert.That(SongTimeMs(clock), Is.Zero);

                Assert.That(Command(clock, "Play"), Is.True);
                double cancelledStartDspTime = Convert.ToDouble(
                    RuntimeReflection.GetField(clock, "startDspTime"));
                Assert.That(StateName(clock), Is.EqualTo("Scheduled"));
                Assert.That(
                    Convert.ToBoolean(GetProperty(clock, "IsRunning")),
                    Is.True);
                Assert.That(source.timeSamples, Is.Zero);
                Assert.That(SongTimeMs(clock), Is.LessThan(0d));

                double pendingObservationDspTime = cancelledStartDspTime -
                    scheduledLeadSeconds * 0.5d;
                float pendingTimeout =
                    Time.realtimeSinceStartup + timeoutSeconds;
                while (AudioSettings.dspTime < pendingObservationDspTime &&
                    Time.realtimeSinceStartup < pendingTimeout)
                {
                    yield return null;
                }

                Assert.That(
                    AudioSettings.dspTime,
                    Is.GreaterThanOrEqualTo(pendingObservationDspTime),
                    "The scheduled request was not held pending long enough.");
                Assert.That(StateName(clock), Is.EqualTo("Scheduled"));
                Assert.That(source.timeSamples, Is.Zero);
                Assert.That(SongTimeMs(clock), Is.LessThan(0d));
                Assert.That(
                    AudioSettings.dspTime,
                    Is.LessThan(cancelledStartDspTime),
                    "Stop must run while playback is still scheduled.");

                Assert.That(Command(clock, "Stop"), Is.True);
                Assert.That(source.isPlaying, Is.False);
                Assert.That(source.timeSamples, Is.Zero);
                Assert.That(StateName(clock), Is.EqualTo("Stopped"));
                Assert.That(SongTimeMs(clock), Is.Zero);
                Assert.That(
                    Convert.ToBoolean(GetProperty(clock, "IsRunning")),
                    Is.False);

                bool latePlaybackObserved = false;
                int maximumSamplesAfterStop = source.timeSamples;
                float cancellationTimeout =
                    Time.realtimeSinceStartup + timeoutSeconds;
                while (AudioSettings.dspTime <
                        cancelledStartDspTime +
                            cancellationDeadlineMarginSeconds &&
                    Time.realtimeSinceStartup < cancellationTimeout)
                {
                    latePlaybackObserved |= source.isPlaying;
                    maximumSamplesAfterStop = Math.Max(
                        maximumSamplesAfterStop,
                        source.timeSamples);
                    yield return null;
                }

                latePlaybackObserved |= source.isPlaying;
                maximumSamplesAfterStop = Math.Max(
                    maximumSamplesAfterStop,
                    source.timeSamples);
                Assert.That(
                    AudioSettings.dspTime,
                    Is.GreaterThanOrEqualTo(
                        cancelledStartDspTime +
                            cancellationDeadlineMarginSeconds),
                    "Cancellation observation timed out before the DSP deadline.");
                Assert.That(
                    latePlaybackObserved,
                    Is.False,
                    "The stopped scheduled source started after its deadline.");
                Assert.That(maximumSamplesAfterStop, Is.Zero);
                Assert.That(source.isPlaying, Is.False);
                Assert.That(source.timeSamples, Is.Zero);
                Assert.That(StateName(clock), Is.EqualTo("Stopped"));
                Assert.That(SongTimeMs(clock), Is.Zero);
                Assert.That(
                    Convert.ToBoolean(GetProperty(clock, "IsRunning")),
                    Is.False);
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
