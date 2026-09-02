using System;
using UnityEngine;

namespace SashimiBoy
{
    public enum AudioClockState
    {
        Stopped,
        Scheduled,
        Playing,
        Paused,
        Finished,
        Faulted
    }

    public sealed class AudioClock : MonoBehaviour
    {
        [Flags]
        private enum LifecyclePauseReason
        {
            None = 0,
            FocusLoss = 1 << 0,
            ApplicationPause = 1 << 1
        }

        public AudioSource audioSource;
        public bool playOnStart;
        public double scheduledLeadTime = 0.1d;

        private double startDspTime;
        private double frozenSongTimeMs;
        private double clipDurationMs;
        private bool scheduledPlaybackStarted;
        private bool resumeAfterLifecyclePause;
        private LifecyclePauseReason lifecyclePauseReasons;

        public AudioClockState State { get; private set; } =
            AudioClockState.Stopped;

        public double SongTimeMs
        {
            get
            {
                if (State == AudioClockState.Scheduled ||
                    State == AudioClockState.Playing)
                {
                    return EvaluateSongTimeMs(AudioSettings.dspTime);
                }

                return frozenSongTimeMs;
            }
        }

        public bool IsRunning => State == AudioClockState.Scheduled ||
            State == AudioClockState.Playing;

        public string LastError { get; private set; } = string.Empty;

        private void OnEnable()
        {
            AudioSettings.OnAudioConfigurationChanged +=
                OnAudioConfigurationChanged;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -=
                OnAudioConfigurationChanged;
        }

        private void Start()
        {
            if (playOnStart)
            {
                Play();
            }
        }

        private void Update()
        {
            Tick(AudioSettings.dspTime,
                audioSource != null && audioSource.isPlaying);
        }

        public bool Play()
        {
            if (State != AudioClockState.Stopped &&
                State != AudioClockState.Faulted)
            {
                return false;
            }

            resumeAfterLifecyclePause = false;
            return BeginFromStart(AudioSettings.dspTime);
        }

        public bool Pause()
        {
            if (State != AudioClockState.Scheduled &&
                State != AudioClockState.Playing)
            {
                return false;
            }

            resumeAfterLifecyclePause = false;
            return PauseAt(AudioSettings.dspTime);
        }

        public bool Resume()
        {
            if (State != AudioClockState.Paused ||
                lifecyclePauseReasons != LifecyclePauseReason.None)
            {
                return false;
            }

            resumeAfterLifecyclePause = false;
            return ResumeAt(AudioSettings.dspTime);
        }

        public bool Stop()
        {
            if (State == AudioClockState.Stopped)
            {
                return false;
            }

            StopAudioSource();
            ResetSourcePosition();
            State = AudioClockState.Stopped;
            frozenSongTimeMs = 0d;
            clipDurationMs = 0d;
            scheduledPlaybackStarted = false;
            resumeAfterLifecyclePause = false;
            LastError = string.Empty;
            return true;
        }

        public bool Replay()
        {
            resumeAfterLifecyclePause = false;
            return BeginFromStart(AudioSettings.dspTime);
        }

        private bool BeginFromStart(double dspTime)
        {
            double previousSongTimeMs = CaptureSongTimeMs(dspTime);
            if (!TryValidateConfiguration(out string error))
            {
                EnterFaulted(error, previousSongTimeMs);
                return false;
            }

            try
            {
                StopAudioSource();
                audioSource.timeSamples = 0;
                clipDurationMs = GetClipDurationMs();
                startDspTime = dspTime + scheduledLeadTime;
                frozenSongTimeMs = -scheduledLeadTime * 1000d;
                scheduledPlaybackStarted = false;
                LastError = string.Empty;
                audioSource.PlayScheduled(startDspTime);
                State = AudioClockState.Scheduled;
                return true;
            }
            catch (Exception exception)
            {
                EnterFaulted(
                    "AudioPlaybackStartFailed: " + exception.Message,
                    previousSongTimeMs);
                return false;
            }
        }

        private bool PauseAt(double dspTime)
        {
            frozenSongTimeMs = CaptureSongTimeMs(dspTime);
            try
            {
                if (audioSource != null)
                {
                    audioSource.Pause();
                }

                State = AudioClockState.Paused;
                return true;
            }
            catch (Exception exception)
            {
                EnterFaulted(
                    "AudioPauseFailed: " + exception.Message,
                    frozenSongTimeMs);
                return false;
            }
        }

        private bool ResumeAt(double dspTime)
        {
            double resumeSongTimeMs = frozenSongTimeMs;
            if (!TryValidateConfiguration(out string error))
            {
                EnterFaulted(error, resumeSongTimeMs);
                return false;
            }

            if (!TryRestoreAt(resumeSongTimeMs, dspTime, out error))
            {
                EnterFaulted(error, resumeSongTimeMs);
                return false;
            }

            LastError = string.Empty;
            return true;
        }

        private bool TryRestoreAt(
            double songTimeMs,
            double dspTime,
            out string error)
        {
            error = string.Empty;
            try
            {
                StopAudioSource();
                clipDurationMs = GetClipDurationMs();
                frozenSongTimeMs = songTimeMs;

                if (songTimeMs < 0d)
                {
                    audioSource.timeSamples = 0;
                    startDspTime = dspTime - songTimeMs / 1000d;
                    scheduledPlaybackStarted = false;
                    audioSource.PlayScheduled(startDspTime);
                    State = AudioClockState.Scheduled;
                    return true;
                }

                SetSourcePosition(songTimeMs);
                startDspTime = dspTime - songTimeMs / 1000d;
                scheduledPlaybackStarted = true;
                audioSource.PlayScheduled(dspTime);
                State = AudioClockState.Playing;
                return true;
            }
            catch (Exception exception)
            {
                error = "AudioPlaybackResumeFailed: " + exception.Message;
                return false;
            }
        }

        private void Tick(double dspTime, bool sourceIsPlaying)
        {
            if (State == AudioClockState.Scheduled &&
                dspTime >= startDspTime)
            {
                State = AudioClockState.Playing;
                scheduledPlaybackStarted = true;
            }

            if (State != AudioClockState.Playing)
            {
                return;
            }

            frozenSongTimeMs = EvaluateSongTimeMs(dspTime);
            if (scheduledPlaybackStarted &&
                frozenSongTimeMs >= clipDurationMs &&
                !sourceIsPlaying)
            {
                State = AudioClockState.Finished;
                frozenSongTimeMs = clipDurationMs;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            SetLifecyclePauseReason(
                LifecyclePauseReason.FocusLoss,
                !hasFocus);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            SetLifecyclePauseReason(
                LifecyclePauseReason.ApplicationPause,
                pauseStatus);
        }

        private void SetLifecyclePauseReason(
            LifecyclePauseReason reason,
            bool active)
        {
            bool wasActive = (lifecyclePauseReasons & reason) != 0;
            if (active)
            {
                lifecyclePauseReasons |= reason;
                if (!wasActive &&
                    (State == AudioClockState.Scheduled ||
                        State == AudioClockState.Playing))
                {
                    resumeAfterLifecyclePause =
                        PauseAt(AudioSettings.dspTime);
                }

                return;
            }

            lifecyclePauseReasons &= ~reason;
            if (lifecyclePauseReasons == LifecyclePauseReason.None &&
                resumeAfterLifecyclePause &&
                State == AudioClockState.Paused)
            {
                resumeAfterLifecyclePause = false;
                ResumeAt(AudioSettings.dspTime);
            }
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            HandleAudioConfigurationChanged(AudioSettings.dspTime);
        }

        private void HandleAudioConfigurationChanged(double dspTime)
        {
            bool wasRunning = State == AudioClockState.Scheduled ||
                State == AudioClockState.Playing;
            bool wasPaused = State == AudioClockState.Paused;
            if (!wasRunning && !wasPaused)
            {
                return;
            }

            double recoverySongTimeMs = CaptureSongTimeMs(dspTime);
            if (!TryValidateConfiguration(out string ignoredError))
            {
                EnterFaulted(
                    "AudioConfigurationRecoveryFailed",
                    recoverySongTimeMs);
                return;
            }

            if (wasPaused)
            {
                try
                {
                    StopAudioSource();
                    clipDurationMs = GetClipDurationMs();
                    frozenSongTimeMs = recoverySongTimeMs;
                    State = AudioClockState.Paused;
                }
                catch (Exception)
                {
                    EnterFaulted(
                        "AudioConfigurationRecoveryFailed",
                        recoverySongTimeMs);
                }

                return;
            }

            if (!TryRestoreAt(
                recoverySongTimeMs,
                dspTime,
                out ignoredError))
            {
                EnterFaulted(
                    "AudioConfigurationRecoveryFailed",
                    recoverySongTimeMs);
                return;
            }

            LastError = string.Empty;
        }

        private bool TryValidateConfiguration(out string error)
        {
            if (audioSource == null)
            {
                error = "MissingAudioSource";
                return false;
            }

            AudioClip clip = audioSource.clip;
            if (clip == null)
            {
                error = "MissingClip";
                return false;
            }

            if (clip.samples <= 0 || clip.frequency <= 0)
            {
                error = "InvalidClip";
                return false;
            }

            if (double.IsNaN(scheduledLeadTime) ||
                double.IsInfinity(scheduledLeadTime) ||
                scheduledLeadTime < 0d)
            {
                error = "InvalidScheduledLead";
                return false;
            }

            if (audioSource.loop)
            {
                error = "LoopingClipUnsupported";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private double CaptureSongTimeMs(double dspTime)
        {
            if (State == AudioClockState.Scheduled ||
                State == AudioClockState.Playing)
            {
                return EvaluateSongTimeMs(dspTime);
            }

            return frozenSongTimeMs;
        }

        private double EvaluateSongTimeMs(double dspTime)
        {
            double songTimeMs = (dspTime - startDspTime) * 1000d;
            if (State == AudioClockState.Playing && clipDurationMs > 0d)
            {
                return Math.Min(songTimeMs, clipDurationMs);
            }

            return songTimeMs;
        }

        private double GetClipDurationMs()
        {
            return (double)audioSource.clip.samples /
                audioSource.clip.frequency * 1000d;
        }

        private void SetSourcePosition(double songTimeMs)
        {
            int finalSample = Math.Max(0, audioSource.clip.samples - 1);
            int sample = (int)Math.Floor(
                songTimeMs / 1000d * audioSource.clip.frequency);
            audioSource.timeSamples = Math.Min(Math.Max(0, sample), finalSample);
        }

        private void ResetSourcePosition()
        {
            if (audioSource != null && audioSource.clip != null)
            {
                try
                {
                    audioSource.timeSamples = 0;
                }
                catch (Exception)
                {
                    // Stopping remains successful even when a failed audio
                    // device cannot accept a position reset.
                }
            }
        }

        private void StopAudioSource()
        {
            if (audioSource != null)
            {
                audioSource.Stop();
            }
        }

        private void EnterFaulted(string error, double songTimeMs)
        {
            bool shouldLog = State != AudioClockState.Faulted;
            StopAudioSource();
            State = AudioClockState.Faulted;
            frozenSongTimeMs = songTimeMs;
            scheduledPlaybackStarted = false;
            resumeAfterLifecyclePause = false;
            LastError = error;

            if (shouldLog)
            {
                Debug.LogError(error, this);
            }
        }
    }
}
