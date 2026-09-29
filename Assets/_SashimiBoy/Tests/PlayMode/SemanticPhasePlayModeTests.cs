using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using SashimiBoy.Semantics;

namespace SashimiBoy.Tests
{
    public sealed class SemanticPhasePlayModeTests
    {
        private Component timing, provider, active, save, flow;
        private object originalSave, testSave;
        private ScriptableObject originalChart;
        private bool originalAutoSave;
        private float originalBpm, originalAudioOffsetMs;
        private bool timingSnapshotCaptured, saveSnapshotCaptured;
        private readonly List<ScriptableObject> fixtures = new List<ScriptableObject>();
        private PhasePerformanceTracker Performance => (PhasePerformanceTracker)RuntimeReflection.Invoke(timing, "get_PhasePerformance");
        private IList Notes => (IList)RuntimeReflection.GetField(provider, "runtimeNotes");
        private double TimeAt(int n) => (double)RuntimeReflection.GetField(Notes[n], "songTimeSeconds");
        private int IntProperty(Component component, string name) => (int)RuntimeReflection.Invoke(component, "get_" + name);

        [UnitySetUp] public IEnumerator Setup()
        {
            timingSnapshotCaptured = saveSnapshotCaptured = false;
            yield return SceneManager.LoadSceneAsync("Stage01_Salmon", LoadSceneMode.Single);
            yield return null;
            timing = RuntimeReflection.FindActiveComponent("SashimiBoy.Stage01SalmonTimingScaffold");
            Assert.That(timing, Is.Not.Null);
            originalBpm = (float)RuntimeReflection.GetField(timing, "bpm");
            originalAudioOffsetMs = (float)RuntimeReflection.GetField(timing, "manualAudioOffsetMs");
            originalChart = (ScriptableObject)RuntimeReflection.GetField(timing, "semanticBeatmap");
            timingSnapshotCaptured = true;
            provider = (Component)RuntimeReflection.GetField(timing, "notePatternProvider");
            active = (Component)RuntimeReflection.GetField(timing, "activeNoteTracker");
            save = RuntimeReflection.FindActiveComponent("SashimiBoy.SaveManager");
            flow = RuntimeReflection.FindActiveComponent("SashimiBoy.GameFlowManager");
            originalSave = RuntimeReflection.GetField(save, "current");
            originalAutoSave = (bool)RuntimeReflection.GetField(save, "autoSaveOnChange");
            saveSnapshotCaptured = true;
            testSave = RuntimeReflection.InvokeStatic("SashimiBoy.SaveData", "CreateNew");
            RuntimeReflection.SetField(save, "autoSaveOnChange", false);
            RuntimeReflection.SetField(save, "current", testSave);
            RuntimeReflection.SetField(timing, "presentationController", null);
            RuntimeReflection.SetField(timing, "judgementFeedback", null);
        }
        [TearDown] public void Cleanup()
        {
            try
            {
                // Restore injected timing before another frame/scene load can observe it.
                if (timingSnapshotCaptured && timing != null)
                {
                    RuntimeReflection.SetField(timing, "bpm", originalBpm);
                    RuntimeReflection.SetField(timing, "manualAudioOffsetMs", originalAudioOffsetMs);
                    RuntimeReflection.SetField(timing, "semanticBeatmap", originalChart);
                    var clock = (Component)RuntimeReflection.GetField(timing, "audioClock");
                    if (clock != null) RuntimeReflection.Invoke(clock, "Stop");
                }
            }
            finally
            {
                try
                {
                    if (saveSnapshotCaptured && save != null)
                    {
                        RuntimeReflection.SetField(save, "current", originalSave);
                        RuntimeReflection.SetField(save, "autoSaveOnChange", originalAutoSave);
                    }
                }
                finally
                {
                    foreach (var fixture in fixtures) UnityEngine.Object.Destroy(fixture);
                    fixtures.Clear();
                    timingSnapshotCaptured = saveSnapshotCaptured = false;
                }
            }
        }
        private void Configure(double threshold = .60d) => fixtures.Add(SemanticTestFixture.Configure(timing, threshold));
        private void AssertNoProgression()
        {
            Assert.That(RuntimeReflection.Invoke(testSave, "IsStageCleared", "STAGE_01_SALMON"), Is.EqualTo(false));
            Assert.That(RuntimeReflection.Invoke(testSave, "IsStageUnlocked", "STAGE_02_ROCKFISH"), Is.EqualTo(false));
            object salmon = Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.FishType"), "Salmon");
            Assert.That(RuntimeReflection.Invoke(testSave, "GetPlates", salmon), Is.EqualTo(0));
            Assert.That(RuntimeReflection.GetField(timing, "stageResultFinalized"), Is.EqualTo(false));
        }
        [UnityTest] public IEnumerator MissingChart_BlocksStartInputAndLegacyResult()
        {
            RuntimeReflection.SetField(timing, "semanticBeatmap", null);
            Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(false));
            Assert.That(((SemanticValidationResult)RuntimeReflection.Invoke(timing, "get_SemanticValidation")).code,
                Is.EqualTo("MISSING_CHART"));
            Assert.That(RuntimeReflection.Invoke(timing, "StartStagePlayback"), Is.EqualTo(false));
            RuntimeReflection.Invoke(timing, "ResolveGameplayInput", TimeAt(0));
            RuntimeReflection.Invoke(timing, "AdvanceStageTimeline", 121d, 121d);
            RuntimeReflection.Invoke(timing, "FinalizeStageResultOnce");
            Assert.That(IntProperty(active, "HitCount"), Is.Zero);
            Assert.That(IntProperty(active, "MissCount"), Is.Zero);
            AssertNoProgression();
            yield return null;
        }
        [UnityTest] public IEnumerator AutoMiss_FailsOnceStopsAudioAndDoesNotRewardOrAdvance()
        {
            Configure();
            var events = new List<SemanticEvent>();
            Performance.EventRaised += events.Add;
            int returnRequests = 0;
            Action<SemanticEvent> onReturn = e => returnRequests++;
            timing.GetType().GetEvent("FailureReturnRequested").AddEventHandler(timing, onReturn);
            int clears = 0;
            // Save state and semantic completion count jointly guard the existing progression path.
            Performance.EventRaised += e => { if (e.kind == SemanticEventKind.StageCompleted) clears++; };
            Assert.That(RuntimeReflection.Invoke(timing, "StartStagePlayback"), Is.EqualTo(true));
            RuntimeReflection.Invoke(timing, "AdvanceStageTimeline", 121d, 121d);
            RuntimeReflection.Invoke(active, "ResolveRemainingNotesAsMissed");
            RuntimeReflection.Invoke(timing, "ResolveGameplayInput", TimeAt(Notes.Count - 1));
            RuntimeReflection.Invoke(timing, "FinalizeStageResultOnce");
            Assert.That(Performance.Failed, Is.True);
            Assert.That(events.FindAll(e => e.kind == SemanticEventKind.PhaseFailed).Count, Is.EqualTo(1));
            Assert.That(events.FindAll(e => e.kind == SemanticEventKind.PhaseSucceeded).Count, Is.Zero);
            Assert.That(returnRequests, Is.EqualTo(1)); Assert.That(clears, Is.Zero);
            Assert.That(IntProperty(active, "MissCount"), Is.EqualTo(Notes.Count / 6));
            Assert.That(IntProperty(active, "UnresolvedCount"), Is.GreaterThan(0));
            var clock = (Component)RuntimeReflection.GetField(timing, "audioClock");
            Assert.That(RuntimeReflection.Invoke(clock, "get_IsRunning"), Is.EqualTo(false));
            AssertNoProgression();
            yield return null;
        }
        [UnityTest] public IEnumerator PhysicalEdge_ResolvesOnlyOneNoteAndKeepsTimingAndScoreMapping()
        {
            Configure();
            var outcomes = new List<JudgementOutcome>();
            Action<JudgementOutcome> receive = outcomes.Add;
            active.GetType().GetEvent("OutcomeResolved").AddEventHandler(active, receive);
            RuntimeReflection.Invoke(active, "JudgePhysicalInput", TimeAt(0), 45d, 90d, 140d, 77L);
            RuntimeReflection.Invoke(active, "JudgePhysicalInput", TimeAt(1), 45d, 90d, 140d, 77L);
            Assert.That(outcomes.Count, Is.EqualTo(1));
            Assert.That(outcomes[0].grade, Is.EqualTo(QualityGrade.Nasty));
            Assert.That(IntProperty(active, "HitCount"), Is.EqualTo(1));
            Assert.That(Performance.NoteCursor, Is.EqualTo(1));
            Assert.That(Performance.TotalPoints, Is.EqualTo(1d));
            RuntimeReflection.Invoke(timing, "ResolveGameplayInput", TimeAt(1) + .06d);
            Assert.That(Performance.TotalPoints, Is.EqualTo(1.75d));
            Assert.That(IntProperty(timing, "Score"), Is.EqualTo(700));
            Assert.That(IntProperty(timing, "Combo"), Is.EqualTo(1));
            Assert.That(Notes.Count, Is.EqualTo(157));
            AssertNoProgression();
            yield return null;
        }
        [UnityTest] public IEnumerator Retry_RebuildsRhythmAndRejectsDelayedOutcomeFromPreviousRun()
        {
            Configure();
            RuntimeReflection.Invoke(timing, "ResolveGameplayInput", TimeAt(0));
            long oldRun = Performance.RunId;
            double originalTime = TimeAt(0);
            RuntimeReflection.Invoke(timing, "AdvanceStageTimeline", 121d, 121d);
            Assert.That(Performance.Failed, Is.True);
            Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(true));
            Assert.That(Performance.RunId, Is.Not.EqualTo(oldRun));
            Assert.That(Performance.NoteCursor, Is.Zero); Assert.That(Performance.PhaseIndex, Is.Zero);
            Assert.That(Performance.TotalPoints, Is.Zero); Assert.That(Performance.Failed || Performance.Completed, Is.False);
            Assert.That(IntProperty(active, "HitCount"), Is.Zero); Assert.That(IntProperty(active, "MissCount"), Is.Zero);
            Assert.That(IntProperty(active, "EmptyHitCount"), Is.Zero); Assert.That(IntProperty(timing, "Score"), Is.Zero);
            Assert.That(IntProperty(timing, "Combo"), Is.Zero); Assert.That(IntProperty(active, "UnresolvedCount"), Is.EqualTo(157));
            Assert.That(TimeAt(0), Is.EqualTo(originalTime));
            RuntimeReflection.Invoke(timing, "HandleJudgementOutcome", new JudgementOutcome(oldRun, 0, QualityGrade.Clean, 1, false));
            Assert.That(Performance.NoteCursor, Is.Zero);
            RuntimeReflection.Invoke(timing, "ResolveGameplayInput", TimeAt(0));
            Assert.That(Performance.NoteCursor, Is.EqualTo(1));
            AssertNoProgression();
            yield return null;
        }
        [UnityTest] public IEnumerator InvalidTiming_RefusesRetryBeforeBuildingSchedule()
        {
            Configure();
            RuntimeReflection.SetField(timing, "bpm", float.NaN);
            Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(false));
            Assert.That(((SemanticValidationResult)RuntimeReflection.Invoke(timing, "get_SemanticValidation")).code,
                Is.EqualTo("INVALID_TIMING"));
            Assert.That(RuntimeReflection.Invoke(timing, "StartStagePlayback"), Is.EqualTo(false));
            Assert.That(Performance.IsRunning, Is.False);
            AssertNoProgression();
            yield return null;
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator InvalidTiming_HidesCueAndRecoversAfterValidRetry()
        {
            Configure();
            Component cue = RuntimeReflection.FindActiveComponent("SashimiBoy.SliceCuePresenter");
            Assert.That(cue, Is.Not.Null, "The real scene cue must participate in this regression.");
            Assert.That(((Behaviour)cue).isActiveAndEnabled, Is.True);
            var cueRoot = (GameObject)RuntimeReflection.GetField(cue, "cueRoot");
            Assert.That(cueRoot, Is.Not.Null);
            var lane = (Transform)RuntimeReflection.GetField(cue, "rhythmLane");
            Assert.That(lane, Is.Not.Null);

            // Hold the stopped clock just after the first gameplay note, within its window.
            // This is test-only calibration; no Scene, BPM, source audio or chart asset is saved.
            RuntimeReflection.SetField(timing, "manualAudioOffsetMs", (float)((TimeAt(0) + .01d) * 1000d));
            RuntimeReflection.Invoke(cue, "Update");
            Assert.That(cueRoot.activeInHierarchy, Is.True, "A permanently hidden cue is not a fix.");
            Assert.That(lane.gameObject.activeInHierarchy, Is.True);
            Assert.That(IntProperty(cue, "VisibleUpcomingCount"), Is.GreaterThan(0));
            AssertCueTransformsFinite(cue);

            foreach (float invalidBpm in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f })
            {
                RuntimeReflection.SetField(timing, "bpm", invalidBpm);
                // Exercise live-field validation before RetryStage updates cached validation.
                RuntimeReflection.Invoke(cue, "Update");
                Assert.That(cueRoot.activeSelf, Is.False);
                Assert.That(lane.gameObject.activeSelf, Is.False);
                Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(false));
                Assert.That(((SemanticValidationResult)RuntimeReflection.Invoke(timing, "get_SemanticValidation")).code,
                    Is.EqualTo("INVALID_TIMING"));
                yield return null;
                yield return null;
                Assert.That(((Behaviour)cue).isActiveAndEnabled, Is.True);
                AssertCueTransformsFinite(cue);
                AssertNoProgression();
                LogAssert.NoUnexpectedReceived();

                RuntimeReflection.SetField(timing, "bpm", originalBpm);
                Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(true));
                RuntimeReflection.Invoke(cue, "Update");
                yield return null;
                Assert.That(cueRoot.activeInHierarchy, Is.True);
                Assert.That(lane.gameObject.activeInHierarchy, Is.True);
                Assert.That(IntProperty(cue, "VisibleUpcomingCount"), Is.GreaterThan(0));
                AssertCueTransformsFinite(cue);
                Assert.That(Performance.NoteCursor, Is.Zero);
                AssertNoProgression();
                LogAssert.NoUnexpectedReceived();
            }
        }

        private static void AssertCueTransformsFinite(Component cue)
        {
            foreach (string field in new[] { "leftBracket", "rightBracket", "forecastLeftBracket",
                "forecastRightBracket", "cutGuideLine", "hitCursor", "rhythmLane" })
            {
                var component = RuntimeReflection.GetField(cue, field) as Component;
                Assert.That(component, Is.Not.Null, "Required cue field: " + field);
                Transform value = component.transform;
                AssertFiniteVector(value.localPosition, field + " localPosition");
                AssertFiniteVector(value.localScale, field + " localScale");
            }
            float approach = (float)RuntimeReflection.Invoke(cue, "get_CurrentApproach01");
            Assert.That(float.IsNaN(approach) || float.IsInfinity(approach), Is.False);
        }

        private static void AssertFiniteVector(Vector3 value, string label)
        {
            Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z), Is.False, label);
        }

        [UnityTest] public IEnumerator FailingHit_PreservesLegacyScoreWithoutFinalizingSuccess()
        {
            Configure(.60d);
            int firstPhaseCount = Notes.Count / 6;
            for (int i = 0; i < firstPhaseCount; i++)
                RuntimeReflection.Invoke(timing, "ResolveGameplayInput", TimeAt(i) + .11d);
            Assert.That(Performance.Failed, Is.True);
            Assert.That(IntProperty(timing, "Score"), Is.EqualTo(firstPhaseCount * 300));
            Assert.That(IntProperty(timing, "Combo"), Is.EqualTo(firstPhaseCount));
            RuntimeReflection.Invoke(timing, "FinalizeStageResultOnce");
            AssertNoProgression();
            yield return null;
        }

        [UnityTest, Category("RequiresHostAuthoring")]
        public IEnumerator ProductionScene_SavedChartStartsWithoutFixtureInjection()
        {
            Assert.That(originalChart, Is.Not.Null, "Host must apply the approved chart before running tests.");
            Assert.That(((SemanticValidationResult)RuntimeReflection.Invoke(timing, "get_SemanticValidation")).IsValid, Is.True);
            Assert.That(Performance.IsRunning, Is.True); Assert.That(Performance.PhaseIndex, Is.Zero);
            Assert.That(RuntimeReflection.GetField(timing, "musicClip"), Is.Not.Null);
            Assert.That(RuntimeReflection.GetField(timing, "audioSource"), Is.Not.Null);
            Assert.That(RuntimeReflection.GetField(timing, "inputKey"), Is.EqualTo(KeyCode.Space));
            var clock = (Component)RuntimeReflection.GetField(timing, "audioClock");
            Assert.That(RuntimeReflection.Invoke(clock, "get_IsRunning"), Is.EqualTo(true));
            // Actual judgement path stays available, without injecting semantic data.
            RuntimeReflection.Invoke(timing, "ResolveGameplayInput", TimeAt(0));
            Assert.That(Performance.NoteCursor, Is.EqualTo(1));
            Assert.That(IntProperty(timing, "Score"), Is.EqualTo(1000));
            AssertNoProgression();
            yield return null;
        }

        [UnityTest, Category("RequiresHostAuthoring")]
        public IEnumerator ProductionChart_AllNastyPassesSixGatesOnlyAfterFinalNote()
        {
            Assert.That(originalChart, Is.Not.Null, "Host authoring required.");
            Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(true));
            var events = new List<SemanticEvent>(); Performance.EventRaised += events.Add;
            for (int i = 0; i < Notes.Count; i++)
            {
                Assert.That(Performance.Completed, Is.False);
                RuntimeReflection.Invoke(timing, "ResolveGameplayInput", TimeAt(i));
            }
            Assert.That(Performance.Completed, Is.True); Assert.That(Performance.Failed, Is.False);
            Assert.That(events.FindAll(e => e.kind == SemanticEventKind.PhaseSucceeded).Count, Is.EqualTo(6));
            Assert.That(events.FindAll(e => e.kind == SemanticEventKind.StageCompleted).Count, Is.EqualTo(1));
            Assert.That(IntProperty(timing, "Score"), Is.EqualTo(Notes.Count * 1000));
            Assert.That(Performance.TotalPoints, Is.EqualTo(Notes.Count));
            yield return null;
        }

        [UnityTest] public IEnumerator InvalidAndStaleCharts_RejectWithoutChangingProductionAsset()
        {
            Configure(.60d);
            var owned = fixtures[fixtures.Count - 1];
            var chart = (SemanticChart)RuntimeReflection.GetField(owned, "chart");
            chart.phases[0].requiredQuality = double.NaN;
            Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(false));
            Assert.That(((SemanticValidationResult)RuntimeReflection.Invoke(timing, "get_SemanticValidation")).code, Is.EqualTo("INVALID_THRESHOLD"));
            chart.phases[0].requiredQuality = .60d;
            RuntimeReflection.SetField(owned, "sourceScheduleIdentity", "stale-test-owned-identity");
            Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(false));
            Assert.That(((SemanticValidationResult)RuntimeReflection.Invoke(timing, "get_SemanticValidation")).code, Is.EqualTo("STALE_CHART"));
            Assert.That(Performance.IsRunning, Is.False);
            Assert.That(RuntimeReflection.Invoke(timing, "StartStagePlayback"), Is.EqualTo(false));
            AssertNoProgression();
            RuntimeReflection.Invoke(owned, "StampSource", timing);
            int originalNoteCount = Notes.Count;
            // Keep coverage unchanged so this specifically tests a stale timing identity.
            // Increasing BPM crosses the first-note boundary and changes coverage first.
            RuntimeReflection.SetField(timing, "bpm", originalBpm - .01f);
            Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(false));
            Assert.That(Notes.Count, Is.EqualTo(originalNoteCount));
            Assert.That(((SemanticValidationResult)RuntimeReflection.Invoke(timing, "get_SemanticValidation")).code, Is.EqualTo("STALE_CHART"));
            yield return null;
        }

        [UnityTest] public IEnumerator LateMissesAcrossBoundary_ResolveInNoteOrderAndStopAtFailedGate()
        {
            Configure(.60d);
            int firstCount = Notes.Count / 6;
            int requiredHits = (int)Math.Ceiling(firstCount * .60d);
            var events = new List<SemanticEvent>(); Performance.EventRaised += events.Add;
            for (int i = 0; i < requiredHits; i++)
                RuntimeReflection.Invoke(timing, "ResolveGameplayInput", TimeAt(i));
            Assert.That(Performance.PhaseIndex, Is.Zero, "Enough quality is not early success.");
            int secondLast = (2 * Notes.Count / 6) - 1;
            RuntimeReflection.Invoke(active, "ProcessExpiredNotes", TimeAt(secondLast) + 1d, 140d);
            Assert.That(Performance.Failed, Is.True); Assert.That(Performance.PhaseIndex, Is.EqualTo(1));
            Assert.That(Performance.NoteCursor, Is.EqualTo(secondLast + 1));
            var actions = events.FindAll(e => e.kind == SemanticEventKind.ActionResolved);
            for (int i = 0; i < actions.Count; i++) Assert.That(actions[i].noteId, Is.EqualTo(i));
            var success = events.Find(e => e.kind == SemanticEventKind.PhaseSucceeded);
            Assert.That(success.noteId, Is.EqualTo(firstCount - 1));
            Assert.That(success.ratio, Is.EqualTo((double)requiredHits / firstCount));
            Assert.That(events.FindAll(e => e.kind == SemanticEventKind.PhaseSucceeded).Count, Is.EqualTo(1));
            Assert.That(events.FindAll(e => e.kind == SemanticEventKind.PhaseFailed).Count, Is.EqualTo(1));
            AssertNoProgression();
            yield return null;
        }
    }
}
