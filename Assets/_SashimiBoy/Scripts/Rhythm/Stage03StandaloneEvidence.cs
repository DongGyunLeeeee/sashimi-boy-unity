#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy
{
    /// <summary>
    /// Explicit opt-in development Player evidence probe. This drives public gameplay
    /// APIs at the REAL AudioClock time; it does not simulate a human keyboard/mouse
    /// test and never reports Owner visual, sync, input or save verification as passed.
    /// Normal Players and the Unity Editor never run the probe.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class Stage03StandaloneEvidence : MonoBehaviour
    {
        [Serializable] private sealed class HitEvidence
        { public int phase, ordinal; public double songMs, noteMs, offsetMs; public bool accepted; }
        [Serializable] private sealed class Snapshot
        {
            public string label, scene, utc, clockState, bodyState, saveJson;
            public double songMs;
            public int phase, phaseHits, totalSlices, spawnedSlices, score, combo;
            public int[] filletCuts;
            public bool turned, turning, resultShown, clearRecorded;
            public float maximumObservedGripError;
        }
        [Serializable] private sealed class Report
        {
            public string outcome, error, outputDirectory, validationProfile, validationSavePath;
            public string sourceAudioSha256, startedUtc, finishedUtc;
            public string defaultSaveHashBefore, defaultSaveHashAfter;
            public bool defaultSavePreserved;
            public bool syntheticApiInput = true;
            public string humanVerification = "Pending: this is NOT a human input, visual, music-sync or save-flow approval.";
            public string fixture = "Day3 Work with prior days and Junseok seeded in a NEW isolated validation profile.";
            public bool coreCompleted, extendedRequested, extendedCompleted;
            public int spawnedSlices, resultCount, errorLogCount;
            public float clipLengthSeconds, elapsedSeconds, maximumObservedGripError, maximumFrameMs;
            public float[] phaseMaximumGripError = new float[9];
            public List<HitEvidence> hits = new List<HitEvidence>();
            public List<string> captures = new List<string>();
            public List<string> errorLogs = new List<string>();
        }

        private static bool initialized;
        private Report report;
        private Stage03GameController controller;
        private string profile, outputDirectory, defaultSavePath;
        private float startedAt;
        private int captureIndex;
        private bool stopped;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartOptInProbe()
        {
            if (Application.isEditor || initialized || !Has("-stage3Evidence")) return;
            initialized = true;
            var host = new GameObject("Stage03_ExplicitStandaloneEvidence");
            DontDestroyOnLoad(host);
            host.AddComponent<Stage03StandaloneEvidence>();
        }

        private void Start()
        {
            report = new Report { outcome = "Running", startedUtc = DateTime.UtcNow.ToString("o"),
                extendedRequested = Has("-stage3EvidenceExtended") };
            startedAt = Time.realtimeSinceStartup;
            Application.logMessageReceived += ObserveLog;
            StartCoroutine(Guarded(Workflow()));
        }

        // Unroll nested iterators so a failure in a scene/action wait is recorded,
        // instead of becoming an unhandled coroutine exception followed by a fake PASS.
        private IEnumerator Guarded(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                object current = null;
                bool moved = false;
                Exception fault = null;
                try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
                catch (Exception error) { fault = error; }
                if (fault != null)
                {
                    foreach (var item in stack) (item as IDisposable)?.Dispose();
                    Fail(fault); yield break;
                }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        private IEnumerator Workflow()
        {
            ValidateCommandAndOutput();
            yield return WaitFor(() => SaveManager.Instance != null && GameFlowManager.Instance != null &&
                SceneTransitionService.Instance != null && DayWorldFlow.Instance != null, 30f, "Bootstrap services");
            string expectedSave = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Logs", "DayWorld", "Profiles", profile + ".json");
            Require(string.Equals(Path.GetFullPath(SaveManager.Instance.SavePath), Path.GetFullPath(expectedSave),
                StringComparison.OrdinalIgnoreCase), "Actual SavePath is not the requested validation profile.");
            // Never overwrite even another evidence run's profile. Launch with a fresh name.
            Require(!File.Exists(expectedSave), "Evidence profile already exists; use a new stage3-evidence-* profile.");
            report.validationSavePath = expectedSave;
            defaultSavePath = Path.Combine(Application.persistentDataPath, SashimiBoyConstants.SaveKeys.SaveFileName);
            report.defaultSaveHashBefore = SaveHash(defaultSavePath);

            SaveManager.Instance.StartDayWorldNewGameWithFace("CuteFace");
            var save = SaveManager.Instance.Current;
            save.currentStageId = SashimiBoyConstants.StageIds.Sole;
            save.UnlockStage(SashimiBoyConstants.StageIds.Rockfish);
            save.UnlockStage(SashimiBoyConstants.StageIds.Sole);
            save.MarkStageCleared(SashimiBoyConstants.StageIds.Salmon);
            save.MarkStageCleared(SashimiBoyConstants.StageIds.Rockfish);
            save.AddEquipment(EquipmentId.SamplePackDrumKit);
            save.AddEquipment(EquipmentId.DawSoftware);
            save.dayWorld.active = true; save.dayWorld.day = 3; save.dayWorld.beat = DayWorldBeat.Work;
            save.dayWorld.nightsSlept = 2; save.dayWorld.pendingStageClear = 0;
            save.dayWorld.checkpointScene = SashimiBoyConstants.Scenes.FishShopDialogue;
            save.dayWorld.checkpointSpawn = "StageReturn";
            save.dayWorld.completedDialogues.AddRange(new[] { "1:misuk", "1:cheolsu", "2:seongho", "2:minjae", "3:junseok" });
            save.dayWorld.placedEquipment.AddRange(new[] { "SamplePackDrumKit", "DawSoftware" });
            save.dayWorld.practicedDays.AddRange(new[] { 1, 2 });
            SaveManager.Instance.RaiseChanged();
            GameFlowManager.Instance.RequestStage(SashimiBoyConstants.StageIds.Sole);
            Require(GameFlowManager.Instance.PendingStageId == SashimiBoyConstants.StageIds.Sole,
                "Stage3 pending-stage request was rejected.");
            SceneTransitionService.Instance.LoadScene(DayWorldRules.StageThreeScene);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == DayWorldRules.StageThreeScene &&
                !SceneTransitionService.Instance.IsLoading, 30f, "Stage3 scene load");
            yield return null; yield return null;
            controller = FindAnyObjectByType<Stage03GameController>();
            Require(controller != null, "Loaded Stage3 has no controller.");
            report.sourceAudioSha256 = controller.chart.sourceSha256;
            report.clipLengthSeconds = controller.chart.music.length;
            yield return Capture("whole");
            Require(controller.StartRun(), "Stage3 StartRun was rejected.");

            float coreDeadline = Time.realtimeSinceStartup + 240f;
            bool head = false, front = false, turned = false, back = false, slice = false, cap = false, plate = false;
            while (!controller.ResultShown)
            {
                Require(Time.realtimeSinceStartup < coreDeadline, "Stage3 real-time playback exceeded 240 seconds.");
                Require(!controller.Run.Failed, "Stage3 gameplay failed; no automatic cut/reward completion was substituted.");
                Require(controller.clock.State != AudioClockState.Faulted, "AudioClock fault: " + controller.clock.LastError);
                if (controller.Run.WaitingForTurn && !controller.assembly.IsTurningOver && !controller.assembly.IsBackFacing)
                    Require(controller.assembly.TryTurnOver(), "The same turnover call used by E was rejected.");
                double next = controller.NextNoteMs;
                double now = controller.clock.SongTimeMs;
                if (next >= 0d && Math.Abs(now - next) <= 20d)
                {
                    int phase = controller.Run.Phase, ordinal = controller.Run.Hits;
                    bool accepted = controller.SubmitAt(now);
                    report.hits.Add(new HitEvidence { phase = phase, ordinal = ordinal,
                        songMs = now, noteMs = next, offsetMs = now - next, accepted = accepted });
                }
                if (!head && controller.assembly.HeadRemoved) { head = true; yield return Capture("head-removed"); }
                if (!front && controller.assembly.DetachedFilletCount >= 2) { front = true; yield return Capture("front-two"); }
                if (!turned && controller.assembly.IsBackFacing) { turned = true; yield return Capture("turned"); }
                if (!back && controller.assembly.DetachedFilletCount == 4 && !controller.assembly.HasBody)
                { back = true; yield return Capture("back-four-body-cleared"); }
                if (!slice && controller.SpawnedSlices >= 1) { slice = true; yield return Capture("slice-one"); }
                if (!cap && controller.SpawnedSlices >= 3) { cap = true; yield return Capture("partial-cut-cap"); }
                if (!plate && controller.SpawnedSlices == 24) { plate = true; yield return Capture("slice-twenty-four"); }
                yield return null;
            }
            yield return Capture("stage-result");
            Require(head && front && turned && back && slice && cap && plate, "One or more presentation milestones were not observed.");
            Require(controller.Run.Completed && controller.Run.TotalSlices == 24 && controller.SpawnedSlices == 24,
                "Result did not contain four actually completed fillets and exactly 24 spawned slices.");
            Require(controller.ClearRecorded && SaveManager.Instance.Current.IsStageCleared(SashimiBoyConstants.StageIds.Sole),
                "Stage3 clear/reward was not recorded.");
            report.coreCompleted = true; report.spawnedSlices = controller.SpawnedSlices; report.resultCount = 1;
            if (report.extendedRequested) yield return ExtendedDay3();
            Require(SaveHash(defaultSavePath) == report.defaultSaveHashBefore, "Default user save changed during the probe.");
            Require(report.errorLogs.Count == 0, "Player emitted errors; inspect errorLogs.");
            report.outcome = "SyntheticApiQaCompleted";
            FinishReport();
            Debug.Log("[Stage3Evidence] Synthetic public-API QA completed. Human input/visual/music/save approval remains pending. " + outputDirectory);
        }

        private IEnumerator ExtendedDay3()
        {
            controller.ReturnToShop();
            yield return WaitScene(SashimiBoyConstants.Scenes.FishShopDialogue);
            yield return Talk("cheolsu", "cheolsu-second-encounter");
            Require(SaveManager.Instance.Current.dayWorld.beat == DayWorldBeat.Purchase, "Cheolsu did not unlock purchase.");
            yield return Travel(SashimiBoyConstants.Scenes.EquipmentShop, "Entry");
            var director = FindAnyObjectByType<DayWorldSceneDirector>();
            yield return WaitFor(() => !DayWorldFlow.InputSuppressed, 30f, "Shop input ownership");
            director.OpenShop();
            Require(director.ShopOpen, "Actual shop UI did not open.");
            var shop = FindAnyObjectByType<EquipmentShopController>();
            Require(shop != null && shop.buyButton != null && shop.buyButton.interactable, "Actual Plugin buy button is not available.");
            yield return Capture("plugin-shop");
            shop.buyButton.onClick.Invoke();
            Require(SaveManager.Instance.Current.HasEquipment(EquipmentId.Plugin), "Actual shop action did not award Plugin.");
            director.CloseShop();
            yield return Travel(DayWorldRules.Home, "Entry");
            var station = FindObjectsByType<HomeEquipmentStation>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(value => value.equipmentId == EquipmentId.Plugin);
            var actor = FindAnyObjectByType<SimpleTopDownPlayerController>().gameObject;
            yield return WaitFor(() => !DayWorldFlow.InputSuppressed, 30f, "Placement input ownership");
            station.Interact(actor);
            Require(SaveManager.Instance.Current.dayWorld.beat == DayWorldBeat.Practice, "Actual Plugin placement was rejected.");
            yield return Capture("plugin-placed");
            yield return WaitFor(() => !DayWorldFlow.InputSuppressed, 30f, "Practice input ownership");
            // Placement does not imply navigation was tested: position is a QA fixture,
            // while practice uses the actual station/body/camera interaction coroutine.
            var character = actor.GetComponent<CharacterController>();
            if (character != null) character.enabled = false;
            actor.transform.SetPositionAndRotation(station.playerPosition.position, station.playerPosition.rotation);
            if (character != null) character.enabled = true;
            station.Interact(actor);
            yield return null;
            Require(station.IsPracticing, "Actual Plugin practice did not start.");
            yield return Capture("plugin-practice");
            yield return WaitFor(() => !station.IsPracticing &&
                SaveManager.Instance.Current.dayWorld.beat == DayWorldBeat.EveningConversation, 30f, "Plugin practice completion");
            yield return Travel(SashimiBoyConstants.Scenes.Street, "KevinHome");
            yield return Talk("jaehoon", "jaehoon-first-encounter");
            Require(SaveManager.Instance.Current.dayWorld.beat == DayWorldBeat.Sleep, "Jaehoon did not unlock sleep.");
            yield return Travel(DayWorldRules.Home, "Entry");
            yield return WaitFor(() => !DayWorldFlow.InputSuppressed, 30f, "Bed input ownership");
            var bed = FindObjectsByType<DayWorldInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(value => value.kind == DayWorldInteractionKind.Bed);
            actor = FindAnyObjectByType<SimpleTopDownPlayerController>().gameObject;
            bed.Interact(actor);
            yield return WaitFor(() => SaveManager.Instance.Current.dayWorld.pendingStageClear == 3, 30f, "Day3 sleep");
            yield return Capture("day-three-clear");
            var clear = FindObjectsByType<DayWorldStageClearScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            Require(!clear.continueButton.interactable, "Stage4 unexpectedly became playable.");
            Require(clear.SaveCheckpointForExit(), "Actual final checkpoint save failed.");
            var disk = JsonUtility.FromJson<SaveData>(File.ReadAllText(SaveManager.Instance.SavePath));
            Require(disk.dayWorld.pendingStageClear == 3 && disk.HasEquipment(EquipmentId.Plugin) &&
                disk.dayWorld.completedDialogues.Contains("3:cheolsu") && disk.dayWorld.completedDialogues.Contains("3:jaehoon"),
                "Written validation-profile checkpoint is incomplete.");
            report.extendedCompleted = true;
        }

        private IEnumerator Talk(string id, string captureLabel)
        {
            yield return WaitFor(() => !DayWorldFlow.InputSuppressed, 30f, "Dialogue input ownership");
            var npc = FindObjectsByType<DayWorldNpc>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(value => value.npcId == id && value.IsAvailable);
            var actor = FindAnyObjectByType<SimpleTopDownPlayerController>().gameObject;
            bool autoLine = false;
            Action<DialogueLine> shown = line => autoLine = line.autoAdvanceDelay > 0f;
            npc.runner.OnLineShown += shown;
            try
            {
                npc.Interact(actor);
                Require(npc.runner.IsRunning, "Actual NPC interaction did not start: " + id);
                yield return Capture(captureLabel);
                float deadline = Time.realtimeSinceStartup + 30f;
                while (npc.runner.IsRunning)
                {
                    Require(Time.realtimeSinceStartup < deadline, "Dialogue exceeded 30 seconds: " + id);
                    yield return new WaitForSecondsRealtime(.08f);
                    if (!autoLine && npc.runner.IsRunning) npc.runner.Advance();
                }
            }
            finally { npc.runner.OnLineShown -= shown; }
        }

        private IEnumerator Travel(string scene, string spawn)
        {
            yield return WaitFor(() => !DayWorldFlow.InputSuppressed &&
                !SceneTransitionService.Instance.IsLoading, 30f, "World travel ownership");
            DayWorldFlow.Instance.LoadWorld(scene, spawn);
            yield return WaitScene(scene);
        }
        private IEnumerator WaitScene(string scene)
        {
            yield return WaitFor(() => SceneManager.GetActiveScene().name == scene &&
                !SceneTransitionService.Instance.IsLoading, 30f, "Scene " + scene);
            yield return null; yield return null;
        }
        private static IEnumerator WaitFor(Func<bool> predicate, float seconds, string operation)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!predicate())
            {
                Require(Time.realtimeSinceStartup < deadline, operation + " timed out after " + seconds + " seconds.");
                yield return null;
            }
        }

        private IEnumerator Capture(string label)
        {
            string prefix = (++captureIndex).ToString("00") + "-" + label;
            string png = Path.Combine(outputDirectory, prefix + ".png");
            yield return new WaitForEndOfFrame();
            var snapshot = new Snapshot
            {
                label = label, scene = SceneManager.GetActiveScene().name, utc = DateTime.UtcNow.ToString("o"),
                saveJson = SaveManager.Instance != null ? JsonUtility.ToJson(SaveManager.Instance.Current) : "",
                maximumObservedGripError = report.maximumObservedGripError
            };
            if (controller != null)
            {
                snapshot.clockState = controller.clock.State.ToString(); snapshot.songMs = controller.clock.SongTimeMs;
                snapshot.bodyState = controller.assembly.BodyState.ToString(); snapshot.phase = controller.Run.Phase;
                snapshot.phaseHits = controller.Run.Hits; snapshot.totalSlices = controller.Run.TotalSlices;
                snapshot.spawnedSlices = controller.SpawnedSlices; snapshot.score = controller.Score; snapshot.combo = controller.Combo;
                snapshot.filletCuts = controller.fillets.Select(surface => surface.CutCount).ToArray();
                snapshot.turned = controller.assembly.IsBackFacing; snapshot.turning = controller.assembly.IsTurningOver;
                snapshot.resultShown = controller.ResultShown; snapshot.clearRecorded = controller.ClearRecorded;
            }
            File.WriteAllText(Path.Combine(outputDirectory, prefix + ".json"), JsonUtility.ToJson(snapshot, true));
            ScreenCapture.CaptureScreenshot(png);
            yield return WaitFor(() => File.Exists(png) && new FileInfo(png).Length > 0, 10f, "Player PNG " + label);
            report.captures.Add(png);
            Debug.Log("[Stage3Evidence] Captured " + label + ": " + png);
        }

        private void LateUpdate()
        {
            if (stopped || report == null || controller == null || !controller.Started || controller.ResultShown) return;
            report.maximumFrameMs = Mathf.Max(report.maximumFrameMs, Time.unscaledDeltaTime * 1000f);
            if (controller.cooking == null || controller.cooking.body == null) return;
            float error = controller.cooking.body.MaximumGripError;
            report.maximumObservedGripError = Mathf.Max(report.maximumObservedGripError, error);
            int phase = controller.Run.Phase;
            if (phase >= 0 && phase < 9)
                report.phaseMaximumGripError[phase] = Mathf.Max(report.phaseMaximumGripError[phase], error);
        }

        private void ValidateCommandAndOutput()
        {
            Require(Has("-dayWorldValidation"), "Evidence requires -dayWorldValidation.");
            profile = Value("-dayWorldProfile");
            Require(!string.IsNullOrEmpty(profile) && Regex.IsMatch(profile, "^stage3-evidence-[a-zA-Z0-9_-]+$") &&
                profile.Length <= 64, "Use a fresh stage3-evidence-* validation profile.");
            string log = Value("-logFile");
            Require(!string.IsNullOrEmpty(log) && Path.IsPathRooted(log), "Evidence requires an absolute owned -logFile path.");
            string logDirectory = Path.GetFullPath(Path.GetDirectoryName(log));
            string configured = Environment.GetEnvironmentVariable("SASHIMI_STAGE3_EVIDENCE");
            string basis = string.IsNullOrEmpty(configured) ? Path.Combine(logDirectory, "Stage3Evidence") : configured;
            Require(Path.IsPathRooted(basis), "Evidence output must be absolute.");
            basis = Path.GetFullPath(basis);
            Require(IsInside(basis, logDirectory), "Evidence output must remain inside the explicit log directory.");
            outputDirectory = Path.Combine(basis, "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Require(!Directory.Exists(outputDirectory), "Evidence run directory already exists.");
            Directory.CreateDirectory(outputDirectory);
            report.outputDirectory = outputDirectory; report.validationProfile = profile;
            File.WriteAllText(Path.Combine(outputDirectory, "probe-mode.txt"),
                "SYNTHETIC PUBLIC-API QA. Actual Player rendering and music-clock timing. NOT a human keyboard/mouse, visual, sync or save-flow PASS. Default user save is not used.");
        }
        private static bool IsInside(string candidate, string parent) =>
            string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static string SaveHash(string path)
        {
            if (!File.Exists(path)) return "ABSENT";
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        private static bool Has(string flag) => Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;
        private static string Value(string flag)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(arguments, flag);
            return i >= 0 && i + 1 < arguments.Length ? arguments[i + 1] : null;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("[Stage3Evidence] " + message); }
        private void ObserveLog(string message, string stack, LogType type)
        {
            if (report == null || stopped || type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            report.errorLogCount++;
            if (report.errorLogs.Count < 100) report.errorLogs.Add(type + ": " + message + "\n" + stack);
        }
        private void Fail(Exception error)
        {
            report.outcome = "Failed"; report.error = error.ToString();
            FinishReport();
            Debug.LogError("[Stage3Evidence] FAILED synthetic QA; Player remains open. " + error);
        }
        private void FinishReport()
        {
            stopped = true;
            report.finishedUtc = DateTime.UtcNow.ToString("o");
            report.elapsedSeconds = Time.realtimeSinceStartup - startedAt;
            if (!string.IsNullOrEmpty(defaultSavePath))
            {
                report.defaultSaveHashAfter = SaveHash(defaultSavePath);
                report.defaultSavePreserved = report.defaultSaveHashBefore == report.defaultSaveHashAfter;
            }
            if (!string.IsNullOrEmpty(outputDirectory))
                File.WriteAllText(Path.Combine(outputDirectory, "report.json"), JsonUtility.ToJson(report, true));
            Application.logMessageReceived -= ObserveLog;
            enabled = false;
        }
        private void OnDestroy() { Application.logMessageReceived -= ObserveLog; }
    }
}
#endif
