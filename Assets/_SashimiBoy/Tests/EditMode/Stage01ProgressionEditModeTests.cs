using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    public sealed class Stage01ProgressionEditModeTests
    {
        private const string SalmonStageId = "STAGE_01_SALMON";
        private const string RockfishStageId = "STAGE_02_ROCKFISH";
        private const string Stage01PatternPath =
            "Assets/_SashimiBoy/Data/Generated/Stage01NotePattern.asset";
        private readonly List<GameObject> createdObjects =
            new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            RuntimeReflection.SetSingleton(
                "SashimiBoy.GameFlowManager",
                null);
            RuntimeReflection.SetSingleton(
                "SashimiBoy.SaveManager",
                null);
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeReflection.SetSingleton(
                "SashimiBoy.GameFlowManager",
                null);
            RuntimeReflection.SetSingleton(
                "SashimiBoy.SaveManager",
                null);

            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }

            createdObjects.Clear();
        }

        [Test]
        public void BuildPayload_UsesAuthoredNoteDenominatorAndRuntimeValues()
        {
            Component timing = CreateComponent(
                "Timing",
                "SashimiBoy.Stage01SalmonTimingScaffold");
            Component provider = CreateComponent(
                "Pattern",
                "SashimiBoy.Stage01NotePatternProvider");
            RuntimeReflection.SetField(
                timing,
                "notePatternProvider",
                provider);
            AddRuntimeNotes(provider, 5);

            RuntimeReflection.SetField(timing, "nastyCount", 3);
            RuntimeReflection.SetField(timing, "smoothCount", 1);
            RuntimeReflection.SetField(timing, "slippedCount", 1);
            RuntimeReflection.SetField(timing, "score", 4000);
            RuntimeReflection.SetField(timing, "yieldPercent", 80f);

            object stage = FindSalmonStage();
            object payload = RuntimeReflection.Invoke(
                timing,
                "BuildStageClearPayload",
                stage);

            Assert.That(
                RuntimeReflection.GetField(payload, "stageId"),
                Is.EqualTo(SalmonStageId));
            Assert.That(
                RuntimeReflection.GetField(payload, "nextStageId"),
                Is.EqualTo(RockfishStageId));
            Assert.That(
                Convert.ToInt32(
                    RuntimeReflection.GetField(payload, "rewardPlates")),
                Is.EqualTo(1));
            Assert.That(
                Convert.ToInt32(RuntimeReflection.GetField(payload, "score")),
                Is.EqualTo(4000));
            Assert.That(
                Convert.ToSingle(
                    RuntimeReflection.GetField(payload, "yield01")),
                Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(
                Convert.ToSingle(
                    RuntimeReflection.GetField(payload, "accuracy01")),
                Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(
                Convert.ToBoolean(
                    RuntimeReflection.GetField(payload, "allNasty")),
                Is.False);
        }

        [Test]
        public void BuildPayload_NoAuthoredNotes_DoesNotSetAllNasty()
        {
            Component timing = CreateComponent(
                "Timing",
                "SashimiBoy.Stage01SalmonTimingScaffold");
            object payload = RuntimeReflection.Invoke(
                timing,
                "BuildStageClearPayload",
                FindSalmonStage());

            Assert.That(
                Convert.ToSingle(
                    RuntimeReflection.GetField(payload, "accuracy01")),
                Is.Zero);
            Assert.That(
                Convert.ToBoolean(
                    RuntimeReflection.GetField(payload, "allNasty")),
                Is.False);
        }

        [Test]
        public void BuildPayload_AllAuthoredNotesNasty_SetsAllNasty()
        {
            Component timing = CreateComponent(
                "Timing",
                "SashimiBoy.Stage01SalmonTimingScaffold");
            Component provider = CreateComponent(
                "Pattern",
                "SashimiBoy.Stage01NotePatternProvider");
            RuntimeReflection.SetField(
                timing,
                "notePatternProvider",
                provider);
            AddRuntimeNotes(provider, 4);
            RuntimeReflection.SetField(timing, "nastyCount", 4);

            object payload = RuntimeReflection.Invoke(
                timing,
                "BuildStageClearPayload",
                FindSalmonStage());

            Assert.That(
                Convert.ToSingle(
                    RuntimeReflection.GetField(payload, "accuracy01")),
                Is.EqualTo(1f));
            Assert.That(
                Convert.ToBoolean(
                    RuntimeReflection.GetField(payload, "allNasty")),
                Is.True);
        }

        [Test]
        public void NaturalPattern_AuthorsOnlyNotesWithACompleteLateWindow()
        {
            Component provider;
            Component tracker;
            Component timing = CreateNaturalPatternRig(
                out provider,
                out tracker);
            IList notes = GetRuntimeNotes(provider);
            double gameplayEnd = Convert.ToDouble(
                RuntimeReflection.GetField(timing, "gameplayEndSec"));
            double lateWindow = Convert.ToDouble(RuntimeReflection.Invoke(
                timing,
                "get_LateJudgementWindowSeconds"));
            double epsilon = Convert.ToDouble(RuntimeReflection.Invoke(
                timing,
                "get_GameplayBoundaryEpsilonSeconds"));

            Assert.That(notes.Count, Is.EqualTo(157));
            for (int i = 0; i < notes.Count; i++)
            {
                double noteTime = Convert.ToDouble(
                    RuntimeReflection.GetField(notes[i], "songTimeSeconds"));
                Assert.That(
                    RuntimeReflection.Invoke(
                        timing,
                        "IsGameplayNotePlayable",
                        noteTime),
                    Is.EqualTo(true),
                    $"Runtime note {i} does not have a complete late window.");
            }

            double lastNoteTime = Convert.ToDouble(
                RuntimeReflection.GetField(
                    notes[notes.Count - 1],
                    "songTimeSeconds"));
            Assert.That(
                lastNoteTime + lateWindow,
                Is.LessThanOrEqualTo(gameplayEnd + epsilon));
            Assert.That(
                Convert.ToInt32(RuntimeReflection.Invoke(
                    tracker,
                    "get_UnresolvedCount")),
                Is.EqualTo(notes.Count));
        }

        [Test]
        public void PlayableBoundary_UsesNamedEpsilonDeterministically()
        {
            Component timing = CreateComponent(
                "Timing",
                "SashimiBoy.Stage01SalmonTimingScaffold");
            double gameplayEnd = Convert.ToDouble(
                RuntimeReflection.GetField(timing, "gameplayEndSec"));
            double lateWindow = Convert.ToDouble(RuntimeReflection.Invoke(
                timing,
                "get_LateJudgementWindowSeconds"));
            double epsilon = Convert.ToDouble(RuntimeReflection.Invoke(
                timing,
                "get_GameplayBoundaryEpsilonSeconds"));
            double insideTolerance = gameplayEnd - lateWindow + epsilon * 0.5d;
            double outsideTolerance = gameplayEnd - lateWindow + epsilon * 2d;

            Assert.That(
                RuntimeReflection.Invoke(
                    timing,
                    "IsGameplayNotePlayable",
                    insideTolerance),
                Is.EqualTo(true));
            Assert.That(
                RuntimeReflection.Invoke(
                    timing,
                    "IsGameplayNotePlayable",
                    outsideTolerance),
                Is.EqualTo(false));
        }

        [Test]
        public void NaturalPattern_AllNasty_ProducesConsistentPerfectPayload()
        {
            Component provider;
            Component tracker;
            Component timing = CreateNaturalPatternRig(
                out provider,
                out tracker);
            IList notes = GetRuntimeNotes(provider);

            for (int i = 0; i < notes.Count; i++)
            {
                double noteTime = Convert.ToDouble(
                    RuntimeReflection.GetField(notes[i], "songTimeSeconds"));
                RuntimeReflection.Invoke(
                    timing,
                    "ResolveGameplayInput",
                    noteTime);
            }

            object payload = RuntimeReflection.Invoke(
                timing,
                "BuildStageClearPayload",
                FindSalmonStage());
            Assert.That(
                Convert.ToInt32(RuntimeReflection.Invoke(
                    tracker,
                    "get_HitCount")),
                Is.EqualTo(notes.Count));
            Assert.That(
                Convert.ToInt32(RuntimeReflection.Invoke(
                    tracker,
                    "get_UnresolvedCount")),
                Is.Zero);
            Assert.That(
                Convert.ToInt32(
                    RuntimeReflection.GetField(timing, "nastyCount")),
                Is.EqualTo(notes.Count));
            Assert.That(
                Convert.ToSingle(
                    RuntimeReflection.GetField(payload, "yield01")),
                Is.EqualTo(1f));
            Assert.That(
                Convert.ToSingle(
                    RuntimeReflection.GetField(payload, "accuracy01")),
                Is.EqualTo(1f));
            Assert.That(
                Convert.ToBoolean(
                    RuntimeReflection.GetField(payload, "allNasty")),
                Is.True);
        }

        [Test]
        public void FinalizeStageResultOnce_AppliesProgressionOnlyOnce()
        {
            object saveData = RuntimeReflection.InvokeStatic(
                "SashimiBoy.SaveData",
                "CreateNew");
            Component saveManager = CreateComponent(
                "SaveManager",
                "SashimiBoy.SaveManager");
            RuntimeReflection.SetField(saveManager, "current", saveData);
            RuntimeReflection.SetField(
                saveManager,
                "autoSaveOnChange",
                false);
            RuntimeReflection.SetSingleton(
                "SashimiBoy.SaveManager",
                saveManager);

            Component gameFlow = CreateComponent(
                "GameFlowManager",
                "SashimiBoy.GameFlowManager");
            RuntimeReflection.SetSingleton(
                "SashimiBoy.GameFlowManager",
                gameFlow);

            Component timing = CreateComponent(
                "Timing",
                "SashimiBoy.Stage01SalmonTimingScaffold");
            RuntimeReflection.SetField(timing, "score", 2500);
            RuntimeReflection.SetField(timing, "yieldPercent", 72f);

            RuntimeReflection.Invoke(timing, "FinalizeStageResultOnce");
            RuntimeReflection.Invoke(timing, "FinalizeStageResultOnce");

            Assert.That(
                Convert.ToBoolean(RuntimeReflection.GetField(
                    timing,
                    "stageResultFinalized")),
                Is.True);
            Assert.That(
                RuntimeReflection.Invoke(
                    saveData,
                    "IsStageCleared",
                    SalmonStageId),
                Is.EqualTo(true));
            Assert.That(
                RuntimeReflection.Invoke(
                    saveData,
                    "IsStageUnlocked",
                    RockfishStageId),
                Is.EqualTo(true));
            Assert.That(GetSalmonPlates(saveData), Is.EqualTo(1));
        }

        [Test]
        public void CompleteStage_MissingSaveManager_LogsExplicitError()
        {
            Component gameFlow = CreateComponent(
                "GameFlowManager",
                "SashimiBoy.GameFlowManager");
            object payload = Activator.CreateInstance(
                RuntimeReflection.RuntimeType(
                    "SashimiBoy.StageClearPayload"));
            RuntimeReflection.SetField(payload, "stageId", SalmonStageId);

            LogAssert.Expect(
                LogType.Error,
                "Stage clear for 'STAGE_01_SALMON' failed because " +
                "SaveManager is missing.");
            RuntimeReflection.Invoke(
                gameFlow,
                "CompleteStage",
                payload);
        }

        [Test]
        public void FinalizeStageResultOnce_MissingGameFlow_RemainsFailed()
        {
            Component timing = CreateComponent(
                "Timing",
                "SashimiBoy.Stage01SalmonTimingScaffold");

            LogAssert.Expect(
                LogType.Error,
                "Stage01 result could not be finalized because " +
                "GameFlowManager is missing.");
            RuntimeReflection.Invoke(timing, "FinalizeStageResultOnce");
            RuntimeReflection.Invoke(timing, "FinalizeStageResultOnce");

            Assert.That(
                Convert.ToBoolean(RuntimeReflection.GetField(
                    timing,
                    "stageResultFinalized")),
                Is.False);
        }

        [Test]
        public void Stage01Scene_HasRequiredReferencesAndNoMissingScripts()
        {
            Scene scene = EditorSceneManager.OpenScene(
                "Assets/_SashimiBoy/Scenes/Stage01_Salmon.unity",
                OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    Assert.That(
                        GameObjectUtility
                            .GetMonoBehavioursWithMissingScriptCount(roots[i]),
                        Is.Zero,
                        $"Missing Script under '{roots[i].name}'.");
                }

                Component timing = FindComponentInScene(
                    scene,
                    "SashimiBoy.Stage01SalmonTimingScaffold");
                Assert.That(timing, Is.Not.Null);

                var serializedTiming = new SerializedObject(timing);
                string[] requiredReferences =
                {
                    "musicClip",
                    "audioSource",
                    "audioClock",
                    "notePatternProvider",
                    "activeNoteTracker",
                    "presentationController"
                };
                for (int i = 0; i < requiredReferences.Length; i++)
                {
                    SerializedProperty property = serializedTiming.FindProperty(
                        requiredReferences[i]);
                    Assert.That(
                        property,
                        Is.Not.Null,
                        $"Serialized field '{requiredReferences[i]}' is missing.");
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        $"Serialized field '{requiredReferences[i]}' is null.");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private Component CreateComponent(
            string name,
            string fullTypeName)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);
            createdObjects.Add(gameObject);
            return RuntimeReflection.AddComponent(gameObject, fullTypeName);
        }

        private static object FindSalmonStage()
        {
            return RuntimeReflection.InvokeStatic(
                "SashimiBoy.ContentDefaults",
                "FindStage",
                SalmonStageId);
        }

        private Component CreateNaturalPatternRig(
            out Component provider,
            out Component tracker)
        {
            Component timing = CreateComponent(
                "Timing",
                "SashimiBoy.Stage01SalmonTimingScaffold");
            provider = CreateComponent(
                "Pattern",
                "SashimiBoy.Stage01NotePatternProvider");
            tracker = CreateComponent(
                "Tracker",
                "SashimiBoy.Stage01ActiveNoteTracker");
            UnityEngine.Object pattern = AssetDatabase.LoadAssetAtPath(
                Stage01PatternPath,
                RuntimeReflection.RuntimeType(
                    "SashimiBoy.Stage01NotePatternDefinition"));
            Assert.That(pattern, Is.Not.Null);

            RuntimeReflection.SetField(provider, "pattern", pattern);
            RuntimeReflection.SetField(
                timing,
                "notePatternProvider",
                provider);
            RuntimeReflection.SetField(
                timing,
                "activeNoteTracker",
                tracker);
            RuntimeReflection.Invoke(timing, "InitializePatternTracking");
            return timing;
        }

        private static IList GetRuntimeNotes(Component provider)
        {
            return (IList)RuntimeReflection.GetField(provider, "runtimeNotes");
        }

        private static void AddRuntimeNotes(Component provider, int count)
        {
            var notes = (IList)RuntimeReflection.GetField(
                provider,
                "runtimeNotes");
            Type noteType = RuntimeReflection.RuntimeType(
                "SashimiBoy.Stage01RuntimeNote");
            for (int i = 0; i < count; i++)
            {
                notes.Add(Activator.CreateInstance(noteType));
            }
        }

        private static int GetSalmonPlates(object saveData)
        {
            object salmon = Enum.Parse(
                RuntimeReflection.RuntimeType("SashimiBoy.FishType"),
                "Salmon");
            return Convert.ToInt32(RuntimeReflection.Invoke(
                saveData,
                "GetPlates",
                salmon));
        }

        private static Component FindComponentInScene(
            Scene scene,
            string fullTypeName)
        {
            Type type = RuntimeReflection.RuntimeType(fullTypeName);
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Component component = roots[i].GetComponentInChildren(
                    type,
                    true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }
    }
}
