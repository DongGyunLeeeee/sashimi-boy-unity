using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    public sealed class Stage01ProgressionPlayModeTests
    {
        [UnityTest]
        public IEnumerator NaturalBoundary_LastPlayableNoteMissesBeforeResult()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                "Stage01_Salmon",
                LoadSceneMode.Single);
            yield return load;
            yield return null;

            Component saveManager = RuntimeReflection.FindActiveComponent(
                "SashimiBoy.SaveManager");
            Component gameFlow = RuntimeReflection.FindActiveComponent(
                "SashimiBoy.GameFlowManager");
            Component timing = RuntimeReflection.FindActiveComponent(
                "SashimiBoy.Stage01SalmonTimingScaffold");
            Assert.That(saveManager, Is.Not.Null);
            Assert.That(gameFlow, Is.Not.Null);
            Assert.That(timing, Is.Not.Null);

            Component provider = (Component)RuntimeReflection.GetField(
                timing,
                "notePatternProvider");
            Component tracker = (Component)RuntimeReflection.GetField(
                timing,
                "activeNoteTracker");
            Assert.That(provider, Is.Not.Null);
            Assert.That(tracker, Is.Not.Null);

            object originalSave = RuntimeReflection.GetField(
                saveManager,
                "current");
            bool originalAutoSave = Convert.ToBoolean(
                RuntimeReflection.GetField(
                    saveManager,
                    "autoSaveOnChange"));
            object testSave = RuntimeReflection.InvokeStatic(
                "SashimiBoy.SaveData",
                "CreateNew");

            try
            {
                RuntimeReflection.SetField(
                    saveManager,
                    "autoSaveOnChange",
                    false);
                RuntimeReflection.SetField(
                    saveManager,
                    "current",
                    testSave);
                RuntimeReflection.SetField(
                    timing,
                    "presentationController",
                    null);
                RuntimeReflection.SetField(
                    timing,
                    "judgementFeedback",
                    null);
                var notes = (IList)RuntimeReflection.GetField(
                    provider,
                    "runtimeNotes");
                Assert.That(notes.Count, Is.EqualTo(157));
                for (int i = 0; i < notes.Count - 1; i++)
                {
                    double noteTime = Convert.ToDouble(
                        RuntimeReflection.GetField(
                            notes[i],
                            "songTimeSeconds"));
                    RuntimeReflection.Invoke(
                        timing,
                        "ResolveGameplayInput",
                        noteTime);
                }

                Assert.That(
                    Convert.ToInt32(RuntimeReflection.Invoke(
                        tracker,
                        "get_UnresolvedCount")),
                    Is.EqualTo(1));

                double gameplayEnd = Convert.ToDouble(
                    RuntimeReflection.GetField(timing, "gameplayEndSec"));
                double frameAfterEnd = gameplayEnd + 0.25d;
                RuntimeReflection.Invoke(
                    timing,
                    "AdvanceStageTimeline",
                    frameAfterEnd,
                    frameAfterEnd);
                yield return null;

                Assert.That(
                    Convert.ToBoolean(RuntimeReflection.GetField(
                        timing,
                        "stageResultFinalized")),
                    Is.True);
                Assert.That(
                    Convert.ToInt32(RuntimeReflection.Invoke(
                        tracker,
                        "get_HitCount")),
                    Is.EqualTo(notes.Count - 1));
                Assert.That(
                    Convert.ToInt32(RuntimeReflection.Invoke(
                        tracker,
                        "get_MissCount")),
                    Is.EqualTo(1));
                Assert.That(
                    Convert.ToInt32(RuntimeReflection.Invoke(
                        tracker,
                        "get_UnresolvedCount")),
                    Is.Zero);
                Assert.That(
                    Convert.ToInt32(RuntimeReflection.GetField(
                        timing,
                        "totalGameplayInputs")),
                    Is.EqualTo(notes.Count));

                float expectedRatio = (notes.Count - 1f) / notes.Count;
                Assert.That(
                    Convert.ToSingle(RuntimeReflection.GetField(
                        timing,
                        "yieldPercent")),
                    Is.EqualTo(expectedRatio * 100f).Within(0.0001f));

                object stage = RuntimeReflection.InvokeStatic(
                    "SashimiBoy.ContentDefaults",
                    "FindStage",
                    "STAGE_01_SALMON");
                object payload = RuntimeReflection.Invoke(
                    timing,
                    "BuildStageClearPayload",
                    stage);
                Assert.That(
                    Convert.ToSingle(
                        RuntimeReflection.GetField(payload, "yield01")),
                    Is.EqualTo(expectedRatio).Within(0.0001f));
                Assert.That(
                    Convert.ToSingle(
                        RuntimeReflection.GetField(payload, "accuracy01")),
                    Is.EqualTo(expectedRatio).Within(0.0001f));
                Assert.That(
                    Convert.ToBoolean(
                        RuntimeReflection.GetField(payload, "allNasty")),
                    Is.False);
                Assert.That(
                    RuntimeReflection.Invoke(
                        testSave,
                        "IsStageCleared",
                        "STAGE_01_SALMON"),
                    Is.EqualTo(true));
                Assert.That(
                    RuntimeReflection.Invoke(
                        testSave,
                        "IsStageUnlocked",
                        "STAGE_02_ROCKFISH"),
                    Is.EqualTo(true));
                Assert.That(GetSalmonPlates(testSave), Is.EqualTo(1));

                RuntimeReflection.Invoke(
                    timing,
                    "AdvanceStageTimeline",
                    frameAfterEnd + 0.25d,
                    frameAfterEnd + 0.25d);
                yield return null;
                Assert.That(GetSalmonPlates(testSave), Is.EqualTo(1));
            }
            finally
            {
                RuntimeReflection.SetField(
                    saveManager,
                    "current",
                    originalSave);
                RuntimeReflection.SetField(
                    saveManager,
                    "autoSaveOnChange",
                    originalAutoSave);
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
    }
}
