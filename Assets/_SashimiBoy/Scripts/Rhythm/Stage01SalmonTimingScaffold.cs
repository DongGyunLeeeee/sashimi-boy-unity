using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public enum Stage01SalmonSection
    {
        BossDemo,
        Gameplay,
        Result
    }

    /// <summary>
    /// Owns Stage 01's DSP-backed timing, input judgement, and prototype score.
    /// Presentation components only read this state and never determine a hit.
    /// </summary>
    public sealed class Stage01SalmonTimingScaffold : MonoBehaviour
    {
        [Header("Audio")]
        public AudioClip musicClip;
        public AudioSource audioSource;
        public AudioClock audioClock;
        public bool playOnStart = true;

        [Header("Timing from stage01_salmon_notes.md")]
        public float bpm = 88f;
        public double firstDownbeatSec = 0.683d;
        public double gameplayStartSec = 11.592d;
        public double gameplayEndSec = 120.683d;

        [Header("Manual Calibration")]
        public float manualAudioOffsetMs;
        public float manualInputLatencyMs;

        [Header("Input")]
        public KeyCode inputKey = KeyCode.Space;

        [Header("Stage 01 Pattern")]
        public Stage01NotePatternProvider notePatternProvider;
        public Stage01ActiveNoteTracker activeNoteTracker;

        [Header("Presentation")]
        public Stage01SalmonPresentationController presentationController;

        [Header("Legacy/Fallback UI")]
        public bool createDebugUiIfMissing;
        public Text hudText;
        public Text judgeText;
        public Text dialogueText;
        public Text missingClipWarningText;
        public GameObject resultRoot;
        public Text resultText;
        public Image progressFill;
        public Image yieldFill;
        public Image warningFlash;

        [Header("Judgement Visual")]
        public JudgementVisualLibrary judgementVisualLibrary;
        public JudgementFeedbackView judgementFeedback;

        private const double BeatsPerBar = 4d;
        private const double NastyWindowMs = 45d;
        private const double SmoothWindowMs = 90d;
        private const double SlippedWindowMs = 140d;
        private const float JudgeDisplayDuration = 0.5f;

        private readonly StringBuilder debugBuilder = new StringBuilder(1024);
        private float judgeDisplayTimer;
        private bool resultShown;
        private bool clipMissing;
        private int score;
        private int combo;
        private int maxCombo;
        private int totalGameplayInputs;
        private float yieldPercent;
        private double lastInputSongSec;
        private double lastInputOffsetMs;
        private string lastJudge = "None";
        private string lastInputDirection = string.Empty;

        public double BeatLengthSeconds => 60d / Math.Max(1d, bpm);
        public double BarLengthSeconds => BeatLengthSeconds * BeatsPerBar;
        public double CountdownStartSeconds => gameplayStartSec - BeatLengthSeconds * 4d;
        public double SongTimeSeconds => CalibratedSongTimeSec();
        public bool IsResultShown => resultShown;
        public bool IsMusicClipMissing => clipMissing;
        public int Score => score;
        public int Combo => combo;
        public int MaxCombo => maxCombo;
        public int TotalGameplayInputs => totalGameplayInputs;
        public float YieldPercent => yieldPercent;
        public double LastInputSongSeconds => lastInputSongSec;
        public double LastInputOffsetMilliseconds => lastInputOffsetMs;
        public string LastJudge => lastJudge;
        public string LastInputDirection => lastInputDirection;
        public int FirstGameplayBeatIndex => Math.Max(
            0,
            (int)Math.Ceiling(
                (gameplayStartSec - firstDownbeatSec) /
                BeatLengthSeconds - 0.000001d));

        public Stage01SalmonSection CurrentSection
        {
            get
            {
                double songSec = SongTimeSeconds;
                if (songSec < gameplayStartSec)
                {
                    return Stage01SalmonSection.BossDemo;
                }

                return songSec >= gameplayEndSec
                    ? Stage01SalmonSection.Result
                    : Stage01SalmonSection.Gameplay;
            }
        }

        private void Awake()
        {
            Camera mainCamera = EnsureMainCamera();
            EnsureExactlyOneAudioListener(mainCamera);
            HideLegacyWorldSpaceDebugObjects();
            ConfigureAudio();
            InitializePatternTracking();

            if (presentationController == null)
            {
                presentationController = FindAnyObjectByType<
                    Stage01SalmonPresentationController>();
            }

            if (presentationController != null)
            {
                presentationController.Bind(this);
            }

            SetResultVisible(false);
        }

        private void OnDestroy()
        {
            if (activeNoteTracker != null)
            {
                activeNoteTracker.NoteMissed -= HandleAutoMiss;
            }
        }

        private void Start()
        {
            clipMissing = audioSource == null || audioSource.clip == null;
            RefreshMissingClipWarning();

            if (playOnStart && !clipMissing &&
                audioClock != null && !audioClock.IsRunning)
            {
                audioClock.Play();
            }
        }

        private void Update()
        {
            double songSec = SongTimeSeconds;

            if (!resultShown && songSec >= gameplayEndSec)
            {
                ShowResultPlaceholder(songSec);
            }

            if (!resultShown &&
                CurrentSection == Stage01SalmonSection.Gameplay &&
                activeNoteTracker != null)
            {
                activeNoteTracker.ProcessExpiredNotes(
                    InputAdjustedSongTimeSec(),
                    SlippedWindowMs);
            }

            if (Input.GetKeyDown(inputKey))
            {
                HandleTimingInput();
            }

            RefreshFallbackUi(songSec);
            RefreshMissingClipWarning();
        }

        public double GetBeatTimeSeconds(int beatIndex)
        {
            return firstDownbeatSec + beatIndex * BeatLengthSeconds;
        }

        public int GetBeatIndexAtOrBefore(double songSec)
        {
            if (songSec < firstDownbeatSec)
            {
                return -1;
            }

            return Math.Max(
                0,
                (int)Math.Floor(
                    (songSec - firstDownbeatSec) / BeatLengthSeconds));
        }

        public int GetCurrentBeatInBar(double songSec)
        {
            int beatIndex = GetBeatIndexAtOrBefore(songSec);
            return beatIndex < 0 ? 0 : beatIndex % 4 + 1;
        }

        public int GetCurrentBar(double songSec)
        {
            int beatIndex = GetBeatIndexAtOrBefore(songSec);
            return beatIndex < 0 ? 0 : beatIndex / 4 + 1;
        }

        public float GetGameplayProgress01(double songSec)
        {
            return Mathf.Clamp01(Mathf.InverseLerp(
                (float)gameplayStartSec,
                (float)gameplayEndSec,
                (float)songSec));
        }

        public string BuildDeveloperDebugText()
        {
            double songSec = SongTimeSeconds;
            int nearestBeatIndex = NearestBeatIndex(songSec);
            double nearestBeatTime = GetBeatTimeSeconds(nearestBeatIndex);
            double offsetMs = (songSec - nearestBeatTime) * 1000d;

            debugBuilder.Length = 0;
            debugBuilder.AppendLine("Stage 01 Timing");
            debugBuilder.AppendLine($"Section: {CurrentSection}");
            debugBuilder.AppendLine($"Song Time: {songSec:0.000}s");
            debugBuilder.AppendLine(
                $"Bar / Beat: {GetCurrentBar(songSec)} / " +
                $"{GetCurrentBeatInBar(songSec)}");
            debugBuilder.AppendLine(
                $"Nearest Beat Offset: {offsetMs:+0.0;-0.0;0.0}ms");
            debugBuilder.AppendLine(
                $"Last Input: {lastInputSongSec:0.000}s  " +
                $"{lastInputOffsetMs:+0.0;-0.0;0.0}ms " +
                lastInputDirection);
            debugBuilder.AppendLine(
                $"Judge: {lastJudge}  Score: {score}  " +
                $"Combo: {combo}  Max: {maxCombo}");
            debugBuilder.AppendLine($"Yield: {yieldPercent:0.0}%");
            if (activeNoteTracker != null)
            {
                Stage01RuntimeNote nextNote =
                    activeNoteTracker.NextActiveNote;
                debugBuilder.AppendLine(
                    $"Notes H/M/E: {activeNoteTracker.HitCount} / " +
                    $"{activeNoteTracker.MissCount} / " +
                    $"{activeNoteTracker.EmptyHitCount}");
                debugBuilder.AppendLine(nextNote != null
                    ? $"Next Note: {nextNote.songTimeSeconds:0.000}s  " +
                        $"Pattern Bar {nextNote.playbackBarIndex}, " +
                        $"Step {nextNote.eighthStepInBar + 1}/8"
                    : "Next Note: none");
            }

            debugBuilder.AppendLine(
                $"manual_audio_offset_ms: {manualAudioOffsetMs:0.0}");
            debugBuilder.AppendLine(
                $"manual_input_latency_ms: {manualInputLatencyMs:0.0}");
            debugBuilder.Append(
                $"Bar 1: {firstDownbeatSec:0.000}s  " +
                $"Bar 5: {gameplayStartSec:0.000}s");
            return debugBuilder.ToString();
        }

        private void HandleTimingInput()
        {
            double rawSec = RawSongTimeSec();
            double inputSec = InputAdjustedSongTimeSec();
            lastInputSongSec = rawSec;

            if (inputSec < gameplayStartSec)
            {
                lastJudge = "Boss demo / no scoring yet";
                lastInputDirection = string.Empty;
                ShowFallbackJudge(lastJudge);
                presentationController?.PresentNonScoringInput();
                return;
            }

            if (inputSec >= gameplayEndSec || resultShown)
            {
                lastJudge = "Result / input blocked";
                lastInputDirection = string.Empty;
                ShowFallbackJudge(lastJudge);
                presentationController?.PresentBlockedInput();
                return;
            }

            if (activeNoteTracker == null ||
                !activeNoteTracker.IsInitialized)
            {
                lastInputOffsetMs = 0d;
                lastJudge = "Stage pattern missing";
                lastInputDirection = string.Empty;
                ShowFallbackJudge(lastJudge);
                return;
            }

            Stage01NoteInputOutcome outcome = activeNoteTracker.JudgeInput(
                inputSec,
                NastyWindowMs,
                SmoothWindowMs,
                SlippedWindowMs);
            if (outcome.kind == Stage01NoteInputKind.Empty)
            {
                lastInputOffsetMs = 0d;
                lastJudge = "EMPTY HIT / WHACK";
                lastInputDirection = string.Empty;
                totalGameplayInputs++;
                combo = 0;
                UpdateYieldPercent();
                ShowFallbackJudge(lastJudge);
                presentationController?.PresentEmptyHit();
                return;
            }

            JudgeResult judgeResult = outcome.judge;
            double offsetMs = judgeResult.offsetMs;
            JudgeGrade grade = judgeResult.grade;
            string direction = DirectionLabel(offsetMs);
            lastInputOffsetMs = offsetMs;
            lastJudge = GradeLabel(grade);
            lastInputDirection = direction;
            totalGameplayInputs++;

            if (grade == JudgeGrade.Whack)
            {
                combo = 0;
            }
            else
            {
                combo++;
                maxCombo = Mathf.Max(maxCombo, combo);
            }

            score += ScoreForGrade(grade);
            UpdateYieldPercent();

            string judgeMessage =
                $"{lastJudge} {offsetMs:+0;-0;0}ms {direction}";
            ShowFallbackJudge(judgeMessage);

            if (presentationController != null)
            {
                presentationController.PresentJudgement(
                    grade,
                    offsetMs,
                    direction);
            }
            else if (judgementFeedback != null)
            {
                judgementFeedback.visualLibrary = judgementVisualLibrary;
                judgementFeedback.Show(grade, offsetMs, direction);
            }
        }

        private void InitializePatternTracking()
        {
            if (notePatternProvider == null)
            {
                notePatternProvider = FindAnyObjectByType<
                    Stage01NotePatternProvider>();
            }

            if (activeNoteTracker == null)
            {
                activeNoteTracker = FindAnyObjectByType<
                    Stage01ActiveNoteTracker>();
            }

            if (notePatternProvider != null)
            {
                notePatternProvider.Initialize(this);
            }

            if (activeNoteTracker == null)
            {
                return;
            }

            activeNoteTracker.Initialize(notePatternProvider);
            activeNoteTracker.NoteMissed -= HandleAutoMiss;
            activeNoteTracker.NoteMissed += HandleAutoMiss;
        }

        private void HandleAutoMiss(Stage01RuntimeNote note)
        {
            if (resultShown ||
                CurrentSection != Stage01SalmonSection.Gameplay)
            {
                return;
            }

            totalGameplayInputs++;
            combo = 0;
            lastJudge = "MISS";
            lastInputDirection = string.Empty;
            UpdateYieldPercent();
            ShowFallbackJudge(lastJudge);
            presentationController?.PresentMiss(note);
        }

        private Camera EnsureMainCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = FindAnyObjectByType<Camera>();
            }

            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
            }

            camera.gameObject.SetActive(true);
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.03f, 0.035f);
            camera.orthographic = true;
            camera.orthographicSize = 3.25f;
            camera.transform.SetPositionAndRotation(
                new Vector3(0f, 8f, 0f),
                Quaternion.Euler(90f, 0f, 0f));
            return camera;
        }

        private static void EnsureExactlyOneAudioListener(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            AudioListener mainListener = camera.GetComponent<AudioListener>();
            if (mainListener == null)
            {
                mainListener = camera.gameObject.AddComponent<AudioListener>();
            }

            mainListener.enabled = true;
            AudioListener[] listeners = FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include);
            int enabledCount = 0;
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener != null && listener.isActiveAndEnabled)
                {
                    enabledCount++;
                }
            }

            if (enabledCount > 1)
            {
                Debug.LogWarning(
                    "Stage01_Salmon found multiple active AudioListeners. " +
                    "Keeping Main Camera AudioListener and disabling the rest.");
            }

            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener != null && listener != mainListener)
                {
                    listener.enabled = false;
                }
            }
        }

        private void ConfigureAudio()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            if (musicClip != null)
            {
                audioSource.clip = musicClip;
            }
            else if (audioSource.clip != null)
            {
                musicClip = audioSource.clip;
            }

            audioSource.playOnAwake = false;
            audioSource.volume = 1f;
            audioSource.mute = false;
            audioSource.spatialBlend = 0f;

            if (audioClock == null)
            {
                audioClock = GetComponent<AudioClock>();
            }

            if (audioClock == null)
            {
                audioClock = gameObject.AddComponent<AudioClock>();
            }

            audioClock.audioSource = audioSource;
            audioClock.playOnStart = false;
        }

        private void RefreshFallbackUi(double songSec)
        {
            if (judgeDisplayTimer > 0f)
            {
                judgeDisplayTimer -= Time.unscaledDeltaTime;
                if (judgeDisplayTimer <= 0f && judgeText != null)
                {
                    judgeText.text = string.Empty;
                }
            }

            if (hudText != null)
            {
                hudText.text = BuildDeveloperDebugText();
            }

            if (progressFill != null)
            {
                progressFill.fillAmount = GetGameplayProgress01(songSec);
            }

            if (yieldFill != null)
            {
                yieldFill.fillAmount = Mathf.Clamp01(yieldPercent / 100f);
            }

            if (dialogueText != null && presentationController == null)
            {
                dialogueText.text = CurrentSection ==
                    Stage01SalmonSection.BossDemo
                    ? "Boss demo / no scoring yet"
                    : CurrentSection == Stage01SalmonSection.Gameplay
                        ? "Space: slice on beat"
                        : "Result placeholder";
            }
        }

        private void RefreshMissingClipWarning()
        {
            clipMissing = audioSource == null || audioSource.clip == null;
            if (missingClipWarningText == null)
            {
                return;
            }

            missingClipWarningText.gameObject.SetActive(clipMissing);
            if (clipMissing)
            {
                missingClipWarningText.text = "Stage 1 music clip missing";
            }
        }

        private void ShowFallbackJudge(string message)
        {
            judgeDisplayTimer = JudgeDisplayDuration;
            if (judgeText != null)
            {
                judgeText.text = message;
            }
        }

        private void ShowResultPlaceholder(double songSec)
        {
            resultShown = true;
            lastJudge = "Result / input blocked";
            SetResultVisible(true);

            if (resultText != null)
            {
                resultText.text =
                    $"RESULT PLACEHOLDER\nSCORE {score}\n" +
                    $"MAX COMBO {maxCombo}\nYIELD {yieldPercent:0.0}%";
            }

            presentationController?.PresentResult();
        }

        private void SetResultVisible(bool visible)
        {
            if (resultRoot != null)
            {
                resultRoot.SetActive(visible);
            }
        }

        private double RawSongTimeSec()
        {
            if (audioClock != null && audioClock.IsRunning)
            {
                return Math.Max(0d, audioClock.SongTimeMs / 1000d);
            }

            return audioSource != null ? Math.Max(0d, audioSource.time) : 0d;
        }

        private double CalibratedSongTimeSec()
        {
            return RawSongTimeSec() + manualAudioOffsetMs / 1000d;
        }

        private double InputAdjustedSongTimeSec()
        {
            return CalibratedSongTimeSec() -
                manualInputLatencyMs / 1000d;
        }

        private int NearestBeatIndex(double songSec)
        {
            return Mathf.Max(
                0,
                Mathf.RoundToInt((float)(
                    (songSec - firstDownbeatSec) / BeatLengthSeconds)));
        }

        private double NearestBeatOffsetMs(double songSec)
        {
            int beatIndex = NearestBeatIndex(songSec);
            return (songSec - GetBeatTimeSeconds(beatIndex)) * 1000d;
        }

        private static int ScoreForGrade(JudgeGrade grade)
        {
            switch (grade)
            {
                case JudgeGrade.Nasty:
                    return 1000;
                case JudgeGrade.Smooth:
                    return 700;
                case JudgeGrade.Slipped:
                    return 300;
                default:
                    return 0;
            }
        }

        private static string GradeLabel(JudgeGrade grade)
        {
            switch (grade)
            {
                case JudgeGrade.Nasty:
                    return "NASTY";
                case JudgeGrade.Smooth:
                    return "SMOOTH";
                case JudgeGrade.Slipped:
                    return "SLIPPED";
                default:
                    return "WHACK";
            }
        }

        private static string DirectionLabel(double offsetMs)
        {
            if (offsetMs < -0.0001d)
            {
                return "EARLY";
            }

            return offsetMs > 0.0001d ? "LATE" : "ON";
        }

        private void UpdateYieldPercent()
        {
            if (totalGameplayInputs <= 0)
            {
                yieldPercent = 0f;
                return;
            }

            yieldPercent = Mathf.Clamp(
                score / (totalGameplayInputs * 1000f) * 100f,
                0f,
                100f);
        }

        private static void HideLegacyWorldSpaceDebugObjects()
        {
            string[] names =
            {
                "Label_Stage01",
                "Label_Demo",
                "Label_Gameplay",
                "Boss_Demo_Placeholder",
                "Kevin_Input_Placeholder",
                "Stage01Salmon_DebugCanvas"
            };

            for (int i = 0; i < names.Length; i++)
            {
                GameObject target = GameObject.Find(names[i]);
                if (target != null)
                {
                    target.SetActive(false);
                }
            }
        }

    }
}
