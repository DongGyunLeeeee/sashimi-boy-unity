using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SashimiBoy.Semantics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class ApprovedPhaseChartEditModeTests
    {
        private const string Authoring = "SashimiBoy.EditorTools.Stage01ApprovedPhaseChartAuthoring";
        private const string ScenePath = "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity";
        private const string ChartPath = "Assets/_SashimiBoy/Data/Generated/Stage01SemanticBeatmap.asset";

        [TestCase(6)] [TestCase(12)] [TestCase(13)] [TestCase(157)]
        public void Partition_CoversActualIdsOnceWithoutChangingTimes(int count)
        {
            var times = Enumerable.Range(0, count).Select(i => 11.592d + i * .341d).ToArray();
            var before = (double[])times.Clone();
            var chart = ApprovedPhaseChart.Build(times);
            Assert.That(SemanticChartValidator.Validate(chart, count).IsValid, Is.True);
            CollectionAssert.AreEqual(before, times);
            CollectionAssert.AreEqual(Enumerable.Range(0, count), chart.notes.Select(n => n.noteId));
            Assert.That(chart.phases.Max(p => p.expectedNoteCount) - chart.phases.Min(p => p.expectedNoteCount), Is.LessThanOrEqualTo(1));
            for (int p = 0; p < 6; p++)
            {
                var gate = chart.phases[p];
                Assert.That(gate.firstNoteId, Is.EqualTo((long)p * count / 6));
                Assert.That(gate.lastNoteId, Is.EqualTo((long)(p + 1) * count / 6 - 1));
                Assert.That(gate.expectedNoteCount, Is.GreaterThan(0));
                Assert.That(gate.metric, Is.EqualTo(GateMetric.Ratio));
                Assert.That(gate.requiredQuality, Is.EqualTo(.60d));
                Assert.That(gate.successTransition, Does.StartWith("stage01.phase."));
                Assert.That(gate.failureTransition, Does.StartWith("stage01.phase."));
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(5)]
        public void Partition_RejectsFewerThanSix(int count) =>
            Assert.Throws<ArgumentException>(() => ApprovedPhaseChart.Build(new double[count]));

        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(-1d)] [TestCase(0d)]
        public void Partition_RejectsNonfiniteOrUnordered(double bad)
        {
            var times = new[] { 0d, 1d, 2d, 3d, 4d, bad };
            Assert.Throws<ArgumentException>(() => ApprovedPhaseChart.Build(times));
        }

        [Test] public void ApprovedWeights_AreMonotonicBestToWorst()
        {
            var weights = new QualityWeights();
            Assert.That(weights.nasty, Is.EqualTo(1d)); Assert.That(weights.clean, Is.EqualTo(.75d));
            Assert.That(weights.slipped, Is.EqualTo(.35d)); Assert.That(weights.whack, Is.Zero);
            Assert.That(weights.nasty, Is.GreaterThan(weights.clean));
            Assert.That(weights.clean, Is.GreaterThan(weights.slipped));
            Assert.That(weights.slipped, Is.GreaterThan(weights.whack));
        }

        [TestCase(45d, "Nasty", QualityGrade.Nasty, 1000)]
        [TestCase(45.001d, "Smooth", QualityGrade.Clean, 700)]
        [TestCase(90d, "Smooth", QualityGrade.Clean, 700)]
        [TestCase(90.001d, "Slipped", QualityGrade.Slipped, 300)]
        [TestCase(140d, "Slipped", QualityGrade.Slipped, 300)]
        [TestCase(140.001d, "Whack", QualityGrade.Whack, 0)]
        public void LegacyWindowsGradeMapAndScore_AreUnchanged(double offset, string legacy, QualityGrade quality, int score)
        {
            foreach (double sign in new[] { -1d, 1d })
            {
                object result = RuntimeReflection.InvokeStatic("SashimiBoy.RhythmJudge", "JudgeFixedWindows", sign * offset, 45d, 90d, 140d);
                object grade = RuntimeReflection.GetField(result, "grade");
                Assert.That(grade.ToString(), Is.EqualTo(legacy));
                Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.Stage01ActiveNoteTracker", "ToQualityGrade", grade), Is.EqualTo(quality));
                Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.Stage01SalmonTimingScaffold", "ScoreForGrade", grade), Is.EqualTo(score));
            }
        }

        [TestCase(true)] [TestCase(false)]
        public void ApprovedThreshold_ReachableGradesWaitForLastMiss(bool exactSuccess)
        {
            var chart = ApprovedPhaseChart.Build(Enumerable.Range(0, 60).Select(i => (double)i).ToArray());
            var tracker = new PhasePerformanceTracker();
            tracker.Reset(chart, 60, 1);
            int gates = 0;
            tracker.EventRaised += e => { if (e.kind == SemanticEventKind.PhaseSucceeded || e.kind == SemanticEventKind.PhaseFailed) gates++; };
            for (int n = 0; n < 10; n++)
            {
                Assert.That(gates, Is.Zero, "No gate before last resolved note.");
                var grade = n < 5 || (n == 5 && exactSuccess) ? QualityGrade.Nasty : n == 5 ? QualityGrade.Clean : QualityGrade.Whack;
                bool miss = grade == QualityGrade.Whack;
                tracker.Consume(new JudgementOutcome(1, n, grade, miss ? 0 : n + 1, miss));
            }
            Assert.That(tracker.TotalPoints / 10, Is.EqualTo(exactSuccess ? .60d : .575d));
            Assert.That(tracker.Failed, Is.EqualTo(!exactSuccess)); Assert.That(gates, Is.EqualTo(1));
        }

        [Test] public void ApprovedThreshold_AdjacentDoubleAboveReachableRatioFails()
        {
            var chart = ApprovedPhaseChart.Build(Enumerable.Range(0, 60).Select(i => (double)i).ToArray());
            chart.phases[0].requiredQuality = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(.60d) + 1);
            var tracker = new PhasePerformanceTracker(); tracker.Reset(chart, 60, 1);
            for (int n = 0; n < 10; n++)
                tracker.Consume(new JudgementOutcome(1, n, n < 6 ? QualityGrade.Nasty : QualityGrade.Whack, n < 6 ? n + 1 : 0, n >= 6));
            Assert.That(tracker.Failed, Is.True);
        }

        [Test, Category("RequiresHostAuthoring")]
        public void SavedProductionChart_RealExpansionBindingAndIdempotence()
        {
            Assert.That(File.Exists(ChartPath), Is.True, "Run Apply Approved Phase Chart once before the suite.");
            var sceneBytes = File.ReadAllBytes(ScenePath); var assetBytes = File.ReadAllBytes(ChartPath);
            string guid = AssetDatabase.AssetPathToGUID(ChartPath);
            var paths = AssetDatabase.FindAssets("t:SemanticBeatmapDefinition").OrderBy(v => v).ToArray();
            RuntimeReflection.InvokeStatic(Authoring, "ApplyApprovedPhaseChartBatch");
            RuntimeReflection.InvokeStatic(Authoring, "ApplyApprovedPhaseChartBatch");
            CollectionAssert.AreEqual(sceneBytes, File.ReadAllBytes(ScenePath));
            CollectionAssert.AreEqual(assetBytes, File.ReadAllBytes(ChartPath));
            Assert.That(AssetDatabase.AssetPathToGUID(ChartPath), Is.EqualTo(guid));
            CollectionAssert.AreEqual(paths, AssetDatabase.FindAssets("t:SemanticBeatmapDefinition").OrderBy(v => v).ToArray());
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var timing = FindTiming(scene);
                var saved = (ScriptableObject)RuntimeReflection.GetField(timing, "semanticBeatmap");
                Assert.That(AssetDatabase.GetAssetPath(saved), Is.EqualTo(ChartPath));
                RuntimeReflection.InvokeStatic(Authoring, "ValidateCanonicalSource", timing);
                var provider = (Component)RuntimeReflection.GetField(timing, "notePatternProvider");
                RuntimeReflection.Invoke(provider, "Initialize", timing);
                var notes = (IList)RuntimeReflection.GetField(provider, "runtimeNotes");
                var times = notes.Cast<object>().Select(n => (double)RuntimeReflection.GetField(n, "songTimeSeconds")).ToArray();
                var ids = notes.Cast<object>().Select(n => (int)RuntimeReflection.GetField(n, "sequenceIndex")).ToArray();
                Assert.That(notes.Cast<object>().Any(n => (bool)RuntimeReflection.GetField(n, "repeated")), Is.True);
                Assert.That(notes.Count, Is.EqualTo(157), "Existing canonical schedule baseline.");
                Assert.That(RuntimeReflection.Invoke(saved, "MatchesSource", timing), Is.EqualTo(true));
                RuntimeReflection.Invoke(provider, "Initialize", timing);
                notes = (IList)RuntimeReflection.GetField(provider, "runtimeNotes");
                CollectionAssert.AreEqual(times, notes.Cast<object>().Select(n => (double)RuntimeReflection.GetField(n, "songTimeSeconds")));
                CollectionAssert.AreEqual(ids, notes.Cast<object>().Select(n => (int)RuntimeReflection.GetField(n, "sequenceIndex")));
                var chart = (SemanticChart)RuntimeReflection.GetField(saved, "chart");
                Assert.That(SemanticChartValidator.Validate(chart, notes.Count).IsValid, Is.True);
                var originalPattern = (ScriptableObject)RuntimeReflection.GetField(provider, "pattern");
                var ownedPattern = UnityEngine.Object.Instantiate(originalPattern);
                var ownedChart = UnityEngine.Object.Instantiate(saved);
                try
                {
                    RuntimeReflection.SetField(provider, "pattern", ownedPattern);
                    RuntimeReflection.Invoke(provider, "Initialize", timing);
                    RuntimeReflection.Invoke(ownedChart, "StampSource", timing);
                    Assert.That(RuntimeReflection.Invoke(ownedChart, "MatchesSource", timing), Is.EqualTo(true));
                    RuntimeReflection.SetField(ownedPattern, "repeatFromBar", (int)RuntimeReflection.GetField(ownedPattern, "manualBarCount"));
                    RuntimeReflection.Invoke(provider, "Initialize", timing);
                    Assert.That(RuntimeReflection.Invoke(ownedChart, "MatchesSource", timing), Is.EqualTo(false), "Real repeated source input changes must stale the chart.");
                }
                finally
                {
                    RuntimeReflection.SetField(provider, "pattern", originalPattern);
                    UnityEngine.Object.DestroyImmediate(ownedChart);
                    UnityEngine.Object.DestroyImmediate(ownedPattern);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test, Category("RequiresHostAuthoring")]
        public void InvalidSource_ActionLeavesSavedAssetAndSceneUntouched()
        {
            Assert.That(File.Exists(ChartPath), Is.True, "Host authoring required.");
            var sceneBytes = File.ReadAllBytes(ScenePath); var assetBytes = File.ReadAllBytes(ChartPath);
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var timing = FindTiming(scene);
            float bpm = (float)RuntimeReflection.GetField(timing, "bpm");
            try
            {
                RuntimeReflection.SetField(timing, "bpm", float.NaN);
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => RuntimeReflection.InvokeStatic(Authoring, "ApplyApprovedPhaseChartBatch"));
                Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
                CollectionAssert.AreEqual(sceneBytes, File.ReadAllBytes(ScenePath));
                CollectionAssert.AreEqual(assetBytes, File.ReadAllBytes(ChartPath));
            }
            finally
            {
                RuntimeReflection.SetField(timing, "bpm", bpm);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Component FindTiming(UnityEngine.SceneManagement.Scene scene) =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(RuntimeReflection.RuntimeType("SashimiBoy.Stage01SalmonTimingScaffold"), true)).Single();
    }
}
