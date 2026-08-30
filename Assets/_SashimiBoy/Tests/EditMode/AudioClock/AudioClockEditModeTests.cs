using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    public sealed class AudioClockEditModeTests
    {
        private const int Frequency = 48000;
        private const int ClipSamples = 4800;
        private const double ClipDurationMs = 100d;

        private GameObject gameObject;
        private AudioSource source;
        private AudioClip clip;
        private Component clock;

        [SetUp]
        public void SetUp()
        {
            gameObject = new GameObject("AudioClockEditModeTests");
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.mute = true;
            clip = AudioClip.Create(
                "AudioClockEditModeClip",
                ClipSamples,
                1,
                Frequency,
                false);
            source.clip = clip;
            clock = RuntimeReflection.AddComponent(
                gameObject,
                "SashimiBoy.AudioClock");
            RuntimeReflection.SetField(clock, "audioSource", source);
            RuntimeReflection.SetField(clock, "scheduledLeadTime", 0.1d);
        }

        [TearDown]
        public void TearDown()
        {
            if (gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            if (clip != null)
            {
                UnityEngine.Object.DestroyImmediate(clip);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TransportCommands_EnforceStateMatrixAndDuplicateNoOps()
        {
            AssertState("Stopped");
            Assert.That(IsRunning, Is.False);
            Assert.That(SongTimeMs, Is.Zero);
            Assert.That(Command("Pause"), Is.False);
            Assert.That(Command("Resume"), Is.False);
            Assert.That(Command("Stop"), Is.False);

            Assert.That(Command("Play"), Is.True);
            AssertState("Scheduled");
            Assert.That(IsRunning, Is.True);
            double firstStart = GetPrivateDouble("startDspTime");

            Assert.That(Command("Play"), Is.False);
            AssertState("Scheduled");
            Assert.That(GetPrivateDouble("startDspTime"),
                Is.EqualTo(firstStart));

            Tick(firstStart + 0.02d, true);
            AssertState("Playing");
            Assert.That(Command("Play"), Is.False);
            Assert.That(Command("Resume"), Is.False);

            Assert.That(InvokeBool("PauseAt", firstStart + 0.03d),
                Is.True);
            AssertState("Paused");
            Assert.That(SongTimeMs, Is.EqualTo(30d).Within(0.01d));
            Assert.That(Command("Pause"), Is.False);
            Assert.That(Command("Play"), Is.False);

            Assert.That(InvokeBool("ResumeAt", firstStart + 1d),
                Is.True);
            AssertState("Playing");
            double resumedStart = GetPrivateDouble("startDspTime");
            Tick(firstStart + 1.01d, true);
            Assert.That(CaptureAt(firstStart + 1.01d),
                Is.EqualTo(40d).Within(0.01d));
            Assert.That(resumedStart,
                Is.EqualTo(firstStart + 0.97d).Within(0.0001d));

            Assert.That(Command("Stop"), Is.True);
            AssertState("Stopped");
            Assert.That(SongTimeMs, Is.Zero);
            Assert.That(IsRunning, Is.False);
            Assert.That(Command("Stop"), Is.False);

            Assert.That(Command("Replay"), Is.True);
            AssertState("Scheduled");
        }

        [Test]
        public void ScheduledPauseResume_PreservesNegativeLeadTime()
        {
            Assert.That(Command("Play"), Is.True);
            double start = GetPrivateDouble("startDspTime");
            Assert.That(CaptureAt(start - 0.04d),
                Is.EqualTo(-40d).Within(0.01d));

            Assert.That(InvokeBool("PauseAt", start - 0.04d), Is.True);
            AssertState("Paused");
            Assert.That(SongTimeMs, Is.EqualTo(-40d).Within(0.01d));

            Assert.That(InvokeBool("ResumeAt", 10d), Is.True);
            AssertState("Scheduled");
            Assert.That(GetPrivateDouble("startDspTime"),
                Is.EqualTo(10.04d).Within(0.0001d));
            Tick(10.02d, false);
            AssertState("Scheduled");
            Tick(10.04d, true);
            AssertState("Playing");
        }

        [Test]
        public void NaturalEnd_FinishesAtExactClipDuration_ThenReplays()
        {
            Assert.That(Command("Play"), Is.True);
            double start = GetPrivateDouble("startDspTime");
            Tick(start + 0.02d, true);
            AssertState("Playing");
            Tick(start + ClipDurationMs / 1000d + 0.01d, false);

            AssertState("Finished");
            Assert.That(SongTimeMs,
                Is.EqualTo(ClipDurationMs).Within(0.01d));
            Assert.That(IsRunning, Is.False);
            Assert.That(Command("Play"), Is.False);
            Assert.That(Command("Pause"), Is.False);
            Assert.That(Command("Resume"), Is.False);

            Assert.That(Command("Replay"), Is.True);
            AssertState("Scheduled");
            Assert.That(CaptureAt(GetPrivateDouble("startDspTime")),
                Is.EqualTo(0d).Within(0.01d));
        }

        [Test]
        public void Replay_RestartsFromEveryActiveAndTerminalState()
        {
            Assert.That(Command("Play"), Is.True);
            Assert.That(Command("Replay"), Is.True);
            AssertState("Scheduled");

            double start = GetPrivateDouble("startDspTime");
            Tick(start + 0.02d, true);
            AssertState("Playing");
            Assert.That(Command("Replay"), Is.True);
            AssertState("Scheduled");

            start = GetPrivateDouble("startDspTime");
            Assert.That(InvokeBool("PauseAt", start - 0.02d), Is.True);
            AssertState("Paused");
            Assert.That(IsRunning, Is.False);
            Assert.That(Command("Replay"), Is.True);
            AssertState("Scheduled");

            start = GetPrivateDouble("startDspTime");
            Tick(start + ClipDurationMs / 1000d + 0.01d, false);
            AssertState("Finished");
            Assert.That(Command("Replay"), Is.True);
            AssertState("Scheduled");
        }

        [Test]
        public void MissingAudioSource_FaultsOnce_AndStopRecovers()
        {
            RuntimeReflection.SetField(clock, "audioSource", null);
            LogAssert.Expect(LogType.Error, "MissingAudioSource");

            Assert.That(Command("Play"), Is.False);
            AssertState("Faulted");
            Assert.That(LastError, Is.EqualTo("MissingAudioSource"));
            Assert.That(Command("Play"), Is.False);
            AssertState("Faulted");

            Assert.That(Command("Stop"), Is.True);
            AssertState("Stopped");
            Assert.That(LastError, Is.Empty);
            Assert.That(SongTimeMs, Is.Zero);
        }

        [Test]
        public void FaultedCommands_StaySafeAndRecoveredResourceCanPlay()
        {
            RuntimeReflection.SetField(clock, "audioSource", null);
            LogAssert.Expect(LogType.Error, "MissingAudioSource");
            Assert.That(Command("Replay"), Is.False);
            AssertState("Faulted");
            Assert.That(Command("Pause"), Is.False);
            Assert.That(Command("Resume"), Is.False);
            Assert.That(Command("Replay"), Is.False);

            RuntimeReflection.SetField(clock, "audioSource", source);
            Assert.That(Command("Play"), Is.True);
            AssertState("Scheduled");
            Assert.That(LastError, Is.Empty);
        }

        [Test]
        public void MissingClipAndLoopingClip_AreRejectedSafely()
        {
            source.clip = null;
            LogAssert.Expect(LogType.Error, "MissingClip");
            Assert.That(Command("Play"), Is.False);
            AssertState("Faulted");
            Assert.That(LastError, Is.EqualTo("MissingClip"));

            Assert.That(Command("Stop"), Is.True);
            source.clip = clip;
            source.loop = true;
            LogAssert.Expect(LogType.Error, "LoopingClipUnsupported");
            Assert.That(Command("Replay"), Is.False);
            AssertState("Faulted");
            Assert.That(LastError, Is.EqualTo("LoopingClipUnsupported"));
        }

        [Test]
        public void InvalidScheduledLead_IsRejectedSafely()
        {
            RuntimeReflection.SetField(clock, "scheduledLeadTime", -0.01d);
            LogAssert.Expect(LogType.Error, "InvalidScheduledLead");

            Assert.That(Command("Play"), Is.False);
            AssertState("Faulted");
            Assert.That(LastError, Is.EqualTo("InvalidScheduledLead"));
        }

        [Test]
        public void LifecycleReasons_ResumeOnlyAfterAllReasonsClear()
        {
            Assert.That(Command("Play"), Is.True);
            RuntimeReflection.Invoke(clock, "OnApplicationFocus", false);
            AssertState("Paused");

            RuntimeReflection.Invoke(clock, "OnApplicationPause", true);
            RuntimeReflection.Invoke(clock, "OnApplicationFocus", true);
            AssertState("Paused");
            RuntimeReflection.Invoke(clock, "OnApplicationPause", false);
            Assert.That(StateName, Is.EqualTo("Scheduled").Or.EqualTo("Playing"));

            Assert.That(Command("Stop"), Is.True);
            Assert.That(Command("Play"), Is.True);
            Assert.That(Command("Pause"), Is.True);
            RuntimeReflection.Invoke(clock, "OnApplicationFocus", false);
            RuntimeReflection.Invoke(clock, "OnApplicationFocus", true);
            AssertState("Paused");
        }

        [Test]
        public void AudioConfigurationChange_RebasesWithoutReset_AndKeepsPause()
        {
            Assert.That(Command("Play"), Is.True);
            double start = GetPrivateDouble("startDspTime");
            Tick(start + 0.025d, true);
            AssertState("Playing");

            RuntimeReflection.Invoke(
                clock,
                "HandleAudioConfigurationChanged",
                start + 0.03d);
            AssertState("Playing");
            Assert.That(CaptureAt(start + 0.04d),
                Is.EqualTo(40d).Within(0.01d));

            Assert.That(InvokeBool("PauseAt", start + 0.05d), Is.True);
            double pausedTime = SongTimeMs;
            RuntimeReflection.Invoke(
                clock,
                "HandleAudioConfigurationChanged",
                start + 2d);
            AssertState("Paused");
            Assert.That(SongTimeMs, Is.EqualTo(pausedTime).Within(0.01d));
        }

        [Test]
        public void AudioConfigurationFailure_FaultsAndFreezesTime()
        {
            Assert.That(Command("Play"), Is.True);
            double start = GetPrivateDouble("startDspTime");
            Tick(start + 0.02d, true);
            source.clip = null;
            LogAssert.Expect(
                LogType.Error,
                "AudioConfigurationRecoveryFailed");

            RuntimeReflection.Invoke(
                clock,
                "HandleAudioConfigurationChanged",
                start + 0.025d);
            AssertState("Faulted");
            Assert.That(SongTimeMs, Is.EqualTo(25d).Within(0.01d));
            Assert.That(LastError,
                Is.EqualTo("AudioConfigurationRecoveryFailed"));
        }

        private bool Command(string name)
        {
            return Convert.ToBoolean(RuntimeReflection.Invoke(clock, name));
        }

        private bool InvokeBool(string name, params object[] arguments)
        {
            return Convert.ToBoolean(
                RuntimeReflection.Invoke(clock, name, arguments));
        }

        private void Tick(double dspTime, bool sourceIsPlaying)
        {
            RuntimeReflection.Invoke(clock, "Tick", dspTime, sourceIsPlaying);
        }

        private double CaptureAt(double dspTime)
        {
            return Convert.ToDouble(RuntimeReflection.Invoke(
                clock,
                "CaptureSongTimeMs",
                dspTime));
        }

        private double GetPrivateDouble(string fieldName)
        {
            return Convert.ToDouble(RuntimeReflection.GetField(clock, fieldName));
        }

        private object GetProperty(string propertyName)
        {
            PropertyInfo property = clock.GetType().GetProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            return property.GetValue(clock);
        }

        private string StateName => GetProperty("State").ToString();
        private double SongTimeMs => Convert.ToDouble(GetProperty("SongTimeMs"));
        private bool IsRunning => Convert.ToBoolean(GetProperty("IsRunning"));
        private string LastError => Convert.ToString(GetProperty("LastError"));

        private void AssertState(string expected)
        {
            Assert.That(StateName, Is.EqualTo(expected));
        }
    }
}
