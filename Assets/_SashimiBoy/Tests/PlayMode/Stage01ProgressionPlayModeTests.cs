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
        public IEnumerator Stage01Result_FinalizesProgressionOnlyOnce()
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
                    "gameplayEndSec",
                    -1d);

                yield return null;
                yield return null;

                Assert.That(
                    Convert.ToBoolean(RuntimeReflection.GetField(
                        timing,
                        "stageResultFinalized")),
                    Is.True);
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
