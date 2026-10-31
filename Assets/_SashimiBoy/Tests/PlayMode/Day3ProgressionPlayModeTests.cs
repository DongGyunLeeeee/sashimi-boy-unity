using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SashimiBoy.Tests
{
    public sealed class Day3ProgressionPlayModeTests
    {
        Component save, flow;
        object originalSave, originalAuto, originalPath;
        static object Get(object target, string name) => RuntimeReflection.GetField(target, name);
        static void Set(object target, string name, object value) => RuntimeReflection.SetField(target, name, value);
        static object Call(object target, string method, params object[] args) => RuntimeReflection.Invoke(target, method, args);
        static Component Find(string name) => RuntimeReflection.FindActiveComponent("SashimiBoy." + name);
        object Current => Get(save, "current");
        object Progress => Get(Current, "dayWorld");
        static object Beat(string name) => Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"), name);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            yield return null;
            save = Find("SaveManager");
            flow = Find("DayWorldFlow");
            Assert.That(save, Is.Not.Null);
            Assert.That(flow, Is.Not.Null);
            originalSave = Current;
            originalAuto = Get(save, "autoSaveOnChange");
            originalPath = Get(save, "validationSavePath");
            Call(save, "ConfigureValidationProfile", "stage3-progression-" + Guid.NewGuid().ToString("N"));
            Set(save, "autoSaveOnChange", true);
            Set(save, "current", RuntimeReflection.InvokeStatic("SashimiBoy.SaveData", "CreateNew"));
            Call(flow, "SetBusy", false);
            yield return new WaitForSecondsRealtime(.35f);
        }

        [TearDown]
        public void TearDown()
        {
            if (save != null)
            {
                Set(save, "current", originalSave);
                Set(save, "autoSaveOnChange", originalAuto);
                Set(save, "validationSavePath", originalPath);
            }
            if (flow != null) Call(flow, "SetBusy", false);
        }

        static IEnumerator WaitScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(scene));
            yield return new WaitForSecondsRealtime(.45f);
        }

        [UnityTest]
        public IEnumerator LegacyStage2DiskSave_OffersDay3WithoutRepeatingClearOrPurchase()
        {
            Set(Progress, "active", true);
            Set(Progress, "day", 2);
            Set(Progress, "beat", Beat("Complete"));
            Set(Progress, "nightsSlept", 2);
            Set(Progress, "pendingStageClear", 0);
            Set(Progress, "checkpointScene", "Street");
            Set(Current, "currentStageId", "STAGE_02_ROCKFISH");
            ((IList)Get(Current, "clearedStageIds")).Add("STAGE_01_SALMON");
            ((IList)Get(Current, "clearedStageIds")).Add("STAGE_02_ROCKFISH");
            ((IList)Get(Current, "ownedEquipmentIds")).Add("SamplePackDrumKit");
            ((IList)Get(Current, "ownedEquipmentIds")).Add("DawSoftware");
            ((IList)Get(Progress, "placedEquipment")).Add("SamplePackDrumKit");
            ((IList)Get(Progress, "placedEquipment")).Add("DawSoftware");
            Call(save, "Save");
            Call(save, "LoadOrCreate");
            Assert.That(Get(Progress, "day"), Is.EqualTo(3));
            Assert.That(Get(Progress, "pendingStageClear"), Is.EqualTo(2));
            Assert.That(Get(Current, "currentStageId"), Is.EqualTo("STAGE_03_SOLE"));
            Assert.That(((IList)Get(Current, "unlockedStageIds")).Contains("STAGE_03_SOLE"), Is.True);
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.EqualTo(2));
            Assert.That(((IList)Get(Current, "ownedEquipmentIds")).Count, Is.EqualTo(2));
            Assert.That(((IList)Get(Current, "fishPlates")).Count, Is.Zero);
            Call(save, "Save");
            string migrated = JsonUtility.ToJson(Current);
            Call(save, "LoadOrCreate");
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(migrated));
            Call(flow, "ContinueGame");
            yield return WaitScene("KevinHome");
            var screen = Find("DayWorldStageClearScreen");
            Assert.That(((Text)Get(screen, "title")).text, Is.EqualTo("2스테이지 클리어"));
            var next = (Button)Get(screen, "continueButton");
            Assert.That(next.interactable, Is.True);
            next.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(Get(Progress, "beat").ToString(), Is.EqualTo("MorningConversation"));
            Assert.That(Get(Progress, "pendingStageClear"), Is.Zero);
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.EqualTo(2));
            Assert.That(((IList)Get(Current, "ownedEquipmentIds")).Count, Is.EqualTo(2));
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules", "RequiredNpc", Current), Is.EqualTo("junseok"));
        }

        [UnityTest]
        public IEnumerator Day3ClearScreen_SaveFailureRetainsState_TitleAndResumeKeepStage4Unavailable()
        {
            Set(Progress, "active", true);
            Set(Progress, "day", 3);
            Set(Progress, "beat", Beat("Complete"));
            Set(Progress, "nightsSlept", 3);
            Set(Progress, "pendingStageClear", 3);
            Set(Progress, "checkpointScene", "KevinHome");
            Set(Progress, "checkpointSpawn", "Wake");
            ((IList)Get(Current, "ownedEquipmentIds")).Add("Plugin");
            Call(save, "Save");
            Call(flow, "ContinueGame");
            yield return WaitScene("KevinHome");
            var screen = Find("DayWorldStageClearScreen");
            Assert.That(((Text)Get(screen, "title")).text, Is.EqualTo("3스테이지 클리어"));
            var next = (Button)Get(screen, "continueButton");
            Assert.That(next.interactable, Is.False);
            Assert.That(next.GetComponentInChildren<Text>().text, Is.EqualTo("다음 스테이지 준비 중"));
            string checkpoint = JsonUtility.ToJson(Current);
            next.onClick.Invoke();
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(checkpoint));
            string actualPath = (string)Get(save, "validationSavePath");
            try
            {
                Set(save, "validationSavePath", Path.GetDirectoryName(actualPath));
                ((Button)Get(screen, "saveAndExitButton")).onClick.Invoke();
                yield return null;
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("KevinHome"));
                Assert.That(screen.gameObject.activeInHierarchy, Is.True);
                Assert.That(((Text)Get(screen, "saveStatus")).text, Does.Contain("저장하지 못했습니다"));
                Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(checkpoint));
            }
            finally { Set(save, "validationSavePath", actualPath); }
            ((Button)Get(screen, "saveAndExitButton")).onClick.Invoke();
            yield return WaitScene("Bootstrap");
            Assert.That(Call(flow, "get_Busy"), Is.EqualTo(false));
            Call(save, "LoadOrCreate");
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(checkpoint));
            ((Button)Get(Find("DayWorldSceneDirector"), "continueButton")).onClick.Invoke();
            yield return WaitScene("KevinHome");
            screen = Find("DayWorldStageClearScreen");
            Assert.That(((Button)Get(screen, "continueButton")).interactable, Is.False);
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(checkpoint));
        }

        [UnityTest]
        public IEnumerator UnsupportedMidStage3Checkpoint_ResumesAtShopWithoutGrantingClear()
        {
            Set(Progress, "active", true);
            Set(Progress, "day", 3);
            Set(Progress, "beat", Beat("Work"));
            Set(Progress, "checkpointScene", "Stage03_Flounder");
            Set(Progress, "checkpointSpawn", "StageReturn");
            ((IList)Get(Current, "unlockedStageIds")).Add("STAGE_03_SOLE");
            Call(flow, "ContinueGame");
            yield return WaitScene("FishShopDialogue");
            Assert.That(Get(Progress, "beat").ToString(), Is.EqualTo("Work"));
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.Zero);
            Assert.That(((IList)Get(Current, "fishPlates")).Count, Is.Zero);
            Assert.That(Get(Progress, "checkpointScene"), Is.EqualTo("FishShopDialogue"));
        }
    }
}
