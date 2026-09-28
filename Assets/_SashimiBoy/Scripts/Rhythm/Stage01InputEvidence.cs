using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy
{
    // Opt-in local evidence only. No input injection, phase skipping, clock changes or rewards.
    // External keyboard validation still goes through Input.GetKeyDown -> ordinary judgement.
    public sealed class Stage01InputEvidence : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private string directory, sceneName, captureName;
        private Stage01SalmonTimingScaffold timing;
        private Stage01ButcheryPresenter view;
        private float nextWrite, captureAt, worstFrame;
        private int lastPhase = -1, lastInputs = -1, captureIndex;
        private long lastRun = -1;
        private bool lastFailed, lastResult;

        [Serializable] private sealed class Snapshot
        {
            public double utcMs, songMs, nextNoteMs, offsetMs;
            public string scene, clock, grade, phase, prompt;
            public int cursor, inputCount, score, combo, slices, bones;
            public bool failed, result, plateComplete, focused, playerActive, inputEnabled, lookBlocked;
            public Vector3 playerPosition, viewAngles;
            public int frame;
            public long run;
            public float frameMs, worstFrameMs;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartCapture()
        {
            if (!SaveManager.IsStage01ValidationSession ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "-stage1Capture") < 0) return;
            var root = new GameObject("Stage1InputEvidence_DevelopmentOnly");
            DontDestroyOnLoad(root);
            root.AddComponent<Stage01InputEvidence>();
        }

        private void Awake()
        {
            // Keep diagnostics responsive while inspecting another window. AudioClock's normal
            // focus-loss pause remains authoritative and is recorded below.
            Application.runInBackground = true;
            directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/Stage1Playable/InputEvidence"));
            Directory.CreateDirectory(directory);
        }

        private void LateUpdate()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene != sceneName)
            {
                sceneName = scene;
                timing = FindAnyObjectByType<Stage01SalmonTimingScaffold>();
                view = FindAnyObjectByType<Stage01ButcheryPresenter>();
                lastRun = -1; lastPhase = -1; lastInputs = -1;
                lastFailed = lastResult = false; worstFrame = 0f;
                QueueCapture(scene, .8f);
            }
            worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime * 1000f);
            if (timing != null)
            {
                var performance = timing.PhasePerformance;
                if (lastRun != performance.RunId || lastPhase != performance.PhaseIndex)
                {
                    lastRun = performance.RunId; lastPhase = performance.PhaseIndex;
                    QueueCapture("stage1-run" + lastRun + "-phase" + lastPhase, .65f);
                }
                if (!lastFailed && performance.Failed) QueueCapture("failure", .45f);
                if (!lastResult && timing.IsResultShown) QueueCapture("clear", .3f);
                lastFailed = performance.Failed; lastResult = timing.IsResultShown;
            }
            if (Time.unscaledTime >= nextWrite)
            {
                nextWrite = Time.unscaledTime + .04f;
                var snapshot = new Snapshot { utcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), scene = scene,
                    frameMs = Time.unscaledDeltaTime * 1000f, worstFrameMs = worstFrame, nextNoteMs = -1d,
                    focused = Application.isFocused, frame = Time.frameCount };
                if (timing != null)
                {
                    snapshot.songMs = timing.SongTimeSeconds * 1000d;
                    snapshot.cursor = timing.PhasePerformance.NoteCursor;
                    snapshot.clock = timing.audioClock.State.ToString();
                    snapshot.run = timing.PhasePerformance.RunId;
                    snapshot.inputCount = timing.TotalGameplayInputs; snapshot.offsetMs = timing.LastInputOffsetMilliseconds;
                    snapshot.grade = timing.LastJudge; snapshot.score = timing.Score; snapshot.combo = timing.Combo;
                    snapshot.failed = timing.PhasePerformance.Failed; snapshot.result = timing.IsResultShown;
                    if (view != null)
                    {
                        snapshot.phase = view.PhaseLabel; snapshot.slices = view.SliceCount;
                        snapshot.bones = view.RemovedBoneCount; snapshot.plateComplete = view.PlateComplete;
                    }
                    var notes = timing.notePatternProvider.RuntimeNotes;
                    if (snapshot.cursor < notes.Count) snapshot.nextNoteMs = notes[snapshot.cursor].songTimeSeconds * 1000d;
                    if (lastInputs != snapshot.inputCount)
                    {
                        lastInputs = snapshot.inputCount;
                        File.AppendAllText(Path.Combine(directory, "inputs.jsonl"), JsonUtility.ToJson(snapshot) + "\n");
                    }
                }
                else
                {
                    var sensor = FindAnyObjectByType<InteractionSensor>();
                    snapshot.prompt = sensor != null && sensor.Current != null ? sensor.Current.Prompt : "";
                    var player = FindAnyObjectByType<SimpleTopDownPlayerController>();
                    var rig = FindAnyObjectByType<KevinFirstPersonCameraRig>();
                    snapshot.playerActive = player != null && player.isActiveAndEnabled;
                    snapshot.inputEnabled = player != null && player.InputEnabled;
                    snapshot.playerPosition = player != null ? player.transform.position : Vector3.zero;
                    snapshot.lookBlocked = rig != null && rig.IsInputBlocked;
                    snapshot.viewAngles = Camera.main != null ? Camera.main.transform.eulerAngles : Vector3.zero;
                }
                File.WriteAllText(Path.Combine(directory, "state.json"), JsonUtility.ToJson(snapshot));
            }
            if (captureName != null && Time.unscaledTime >= captureAt)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(directory,
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + (++captureIndex).ToString("00") + "-" + captureName + ".png"));
                captureName = null;
            }
        }

        private void QueueCapture(string label, float delay)
        { captureName = label; captureAt = Time.unscaledTime + delay; }
#endif
    }
}
