#if UNITY_EDITOR
using System;
using System.Linq;
using System.IO;
using System.Text;
using SashimiBoy.Semantics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EditorTools
{
    public static class Stage01ApprovedPhaseChartAuthoring
    {
        public const string ScenePath = "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity";
        public const string PatternPath = "Assets/_SashimiBoy/Data/Generated/Stage01NotePattern.asset";
        public const string MusicPath = "Assets/_SashimiBoy/Audio/Music/Stage_01_Salmon/stage01_salmon_main.mp3";
        public const string ChartPath = "Assets/_SashimiBoy/Data/Generated/Stage01SemanticBeatmap.asset";

        [MenuItem("Sashimi Boy/Stage 01/Apply Approved Phase Chart")]
        public static void ApplyApprovedPhaseChart() => Apply(false);

        // Stable -executeMethod counterpart; never prompts or silently regenerates tuning.
        public static void ApplyApprovedPhaseChartBatch() => Apply(false);

        [MenuItem("Sashimi Boy/Stage 01/Regenerate Approved Phase Chart...")]
        public static void RegenerateApprovedPhaseChart()
        {
            if (EditorUtility.DisplayDialog("Replace Stage01 phase tuning?",
                "Explicitly replace all owned chart ranges, weights and thresholds with Owner v2 prototype values from the current source?", "Regenerate", "Cancel"))
                Apply(true);
        }

        public static void BindExistingChart(Stage01SalmonTimingScaffold timing)
        {
            // Generator hookup only. Missing/stale charts remain refused at runtime.
            timing.semanticBeatmap = AssetDatabase.LoadAssetAtPath<SemanticBeatmapDefinition>(ChartPath);
        }

        private static void Apply(bool regenerate)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply phase chart outside Play mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Unsaved scene work exists. Save or resolve it yourself before authoring; no scenes were saved.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new InvalidOperationException("Canonical Stage01 scene is missing: " + ScenePath);
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SemanticBeatmapDefinition candidate = null;
            try
            {
                var timings = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Stage01SalmonTimingScaffold>(true)).ToArray();
                if (timings.Length != 1) throw new InvalidOperationException("Expected exactly one canonical timing component.");
                var timing = timings[0];
                ValidateCanonicalSource(timing);
                // Same validation and provider expansion as InitializePatternTracking; no audio, Awake or scene generator execution.
                timing.notePatternProvider.Initialize(timing);
                var notes = timing.notePatternProvider.RuntimeNotes;
                candidate = ScriptableObject.CreateInstance<SemanticBeatmapDefinition>();
                candidate.chart = ApprovedPhaseChart.Build(notes.Select(note => note.songTimeSeconds).ToArray());
                candidate.StampSource(timing);
                var existingObject = AssetDatabase.LoadMainAssetAtPath(ChartPath);
                var saved = existingObject as SemanticBeatmapDefinition;
                if (existingObject == null && (File.Exists(ChartPath) || File.Exists(ChartPath + ".meta")))
                    throw new InvalidOperationException("Unimported or orphaned chart output exists; refusing overwrite.");
                if (existingObject != null && saved == null)
                    throw new InvalidOperationException("Chart output is owned by another asset type.");
                if (saved != null && (EditorUtility.IsDirty(saved) || saved.authoringVersion != ApprovedPhaseChart.Version))
                    throw new InvalidOperationException("Chart has unsaved changes or unknown ownership; refusing overwrite.");
                if (timing.semanticBeatmap != null && timing.semanticBeatmap != saved)
                    throw new InvalidOperationException("Scene references a different chart; refusing ambiguous target.");
                if (saved != null && !regenerate)
                {
                    var valid = SemanticChartValidator.Validate(saved.chart, notes.Count);
                    if (!valid.IsValid || !saved.MatchesSource(timing))
                        throw new InvalidOperationException("Existing chart is invalid/stale. Review source changes, then explicitly use Regenerate Approved Phase Chart. " + valid);
                    // Preserve all later valid manual tuning, with no dirty mark or GUID churn.
                }
                else if (saved == null)
                {
                    AssetDatabase.CreateAsset(candidate, ChartPath);
                    saved = candidate; candidate = null;
                    AssetDatabase.SaveAssetIfDirty(saved);
                }
                else
                {
                    EditorUtility.CopySerialized(candidate, saved);
                    EditorUtility.SetDirty(saved);
                    AssetDatabase.SaveAssetIfDirty(saved);
                }
                if (timing.semanticBeatmap != saved)
                {
                    timing.semanticBeatmap = saved;
                    EditorUtility.SetDirty(timing);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene, ScenePath))
                        throw new InvalidOperationException("Chart persisted but canonical scene save failed; apply again after resolving save failure.");
                }
                var report = new StringBuilder("Stage01 approved phase chart applied\n");
                report.AppendLine("Scene: " + ScenePath + "\nPattern: " + PatternPath + "\nMusic: " + MusicPath + "\nChart: " + ChartPath);
                report.AppendLine("Source: " + saved.sourceScheduleIdentity + "; version: " + saved.authoringVersion);
                report.AppendLine($"Expanded playable notes: {notes.Count}; repeated notes: {notes.Count(note => note.repeated)}");
                report.AppendLine($"Weights Nasty={saved.chart.weights.nasty:R}, Clean={saved.chart.weights.clean:R}, Slipped={saved.chart.weights.slipped:R}, Whack={saved.chart.weights.whack:R}");
                foreach (var gate in saved.chart.phases)
                    report.AppendLine($"{gate.phase}: [{gate.firstNoteId}..{gate.lastNoteId}], count={gate.expectedNoteCount}, song={notes[gate.firstNoteId].songTimeSeconds:R}..{notes[gate.lastNoteId].songTimeSeconds:R}, {gate.metric}={gate.requiredQuality:R}");
                report.AppendLine("Binding: saved asset assigned before runtime initialization; prototype initial values, editable per phase.");
                Debug.Log(report.ToString(), saved);
            }
            finally
            {
                if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate);
                if (opened && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }
        }

        public static void ValidateCanonicalSource(Stage01SalmonTimingScaffold timing)
        {
            var valid = timing.ValidatePatternSource();
            if (!valid.IsValid) throw new InvalidOperationException(valid.ToString());
            if (timing.notePatternProvider.gameObject.scene != timing.gameObject.scene ||
                timing.activeNoteTracker.gameObject.scene != timing.gameObject.scene ||
                timing.notePatternProvider.timing != timing ||
                AssetDatabase.GetAssetPath(timing.notePatternProvider.pattern) != PatternPath ||
                AssetDatabase.GetAssetPath(timing.musicClip) != MusicPath ||
                EditorUtility.IsDirty(timing.notePatternProvider.pattern) ||
                timing.audioSource == null || timing.audioClock == null ||
                (timing.audioSource.clip != null && timing.audioSource.clip != timing.musicClip) ||
                timing.musicClip == null || timing.musicClip.samples <= 0 ||
                timing.gameplayEndSec > (double)timing.musicClip.samples / timing.musicClip.frequency)
                throw new InvalidOperationException("Canonical pattern/music/timing bindings are missing, inconsistent, unsaved or out of clip bounds.");
        }
    }
}
#endif
