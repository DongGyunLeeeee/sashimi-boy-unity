using System;
using System.Collections;
using System.IO;
using System.Linq;
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
        static Component[] All(string name) => Resources.FindObjectsOfTypeAll(RuntimeReflection.RuntimeType("SashimiBoy." + name))
            .Cast<Component>().Where(c => c.gameObject.scene.IsValid() && c.gameObject.activeInHierarchy).ToArray();
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

        private sealed class PracticePose
        {
            public Component body;
            public Camera camera;
            public Vector3 bodyPosition, cameraPosition;
            public Quaternion bodyRotation, cameraRotation;
            public float cameraFov;
        }

        IEnumerator HomePracticeFixture(bool required)
        {
            Set(Progress, "active", true); Set(Progress, "day", 3);
            Set(Progress, "beat", Beat(required ? "Practice" : "Sleep"));
            Set(Progress, "nightsSlept", 2); Set(Progress, "pendingStageClear", 0);
            Set(Current, "currentStageId", "STAGE_03_SOLE");
            foreach (string id in new[] { "STAGE_01_SALMON", "STAGE_02_ROCKFISH", "STAGE_03_SOLE" })
            { Call(Current, "UnlockStage", id); Call(Current, "MarkStageCleared", id); }
            foreach (string id in new[] { "SamplePackDrumKit", "DawSoftware", "Plugin" })
            {
                ((IList)Get(Current, "ownedEquipmentIds")).Add(id);
                ((IList)Get(Progress, "placedEquipment")).Add(id);
            }
            foreach (string id in new[] { "3:junseok", "3:cheolsu", "3:jaehoon" })
                ((IList)Get(Progress, "completedDialogues")).Add(id);
            ((IList)Get(Progress, "practicedDays")).Add(1);
            ((IList)Get(Progress, "practicedDays")).Add(2);
            if (!required) ((IList)Get(Progress, "practicedDays")).Add(3);
            yield return SceneManager.LoadSceneAsync("KevinHome");
            yield return new WaitForSecondsRealtime(.4f);
            Call(Find("KevinFirstPersonCameraRig"), "SetLookEnabled", false);
            Assert.That(All("HomeEquipmentStation").Length, Is.EqualTo(3));
            Call(save, "Save");
        }

        static PracticePose CapturePracticePose(GameObject actor)
        {
            var body = actor.GetComponentInChildren(RuntimeReflection.RuntimeType("SashimiBoy.KevinBodyRig"), true);
            var camera = (Camera)Get(actor.GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.KevinFirstPersonCameraRig")), "controlledCamera");
            Assert.That(body, Is.Not.Null); Assert.That(camera, Is.Not.Null);
            return new PracticePose { body = body, camera = camera, bodyPosition = body.transform.localPosition,
                bodyRotation = body.transform.localRotation, cameraPosition = camera.transform.localPosition,
                cameraRotation = camera.transform.localRotation, cameraFov = camera.fieldOfView };
        }

        void AssertPracticeRestored(Component station, PracticePose pose)
        {
            Assert.That(Call(station, "get_IsPracticing"), Is.EqualTo(false));
            Assert.That(Call(flow, "get_Busy"), Is.EqualTo(false));
            Assert.That(Get(pose.body, "working"), Is.EqualTo(false));
            Assert.That(Vector3.Distance(pose.body.transform.localPosition, pose.bodyPosition), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(pose.body.transform.localRotation, pose.bodyRotation), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(pose.camera.transform.localPosition, pose.cameraPosition), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(pose.camera.transform.localRotation, pose.cameraRotation), Is.LessThan(.01f));
            Assert.That(pose.camera.fieldOfView, Is.EqualTo(pose.cameraFov).Within(.0001f));
            Assert.That(((Renderer[])Get(pose.body, "headRenderers")).All(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly), Is.True);
        }

        IEnumerator BeginPractice(Component station, GameObject actor)
        {
            // SetBusy(false) deliberately has an input hold; respect that same
            // hold between repeated interactions rather than bypassing it.
            yield return new WaitForSecondsRealtime(.32f);
            Assert.That(Call(station, "get_IsAvailable"), Is.EqualTo(true));
            Assert.That(station.transform.Find("StationName").gameObject.activeSelf, Is.True);
            Assert.That(((GameObject)Get(station, "equipmentVisual")).activeSelf, Is.True);
            Assert.That(station.GetComponents<Collider>().All(c => c.enabled), Is.True);
            Call(station, "Interact", actor);
            Assert.That(Call(station, "get_IsPracticing"), Is.EqualTo(true));
            Assert.That(Call(flow, "get_Busy"), Is.EqualTo(true));
            Assert.That(Call(station, "get_IsAvailable"), Is.EqualTo(false));
        }

        static IEnumerator FinishPractice(Component station)
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while ((bool)Call(station, "get_IsPracticing") && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(Call(station, "get_IsPracticing"), Is.EqualTo(false), "The real practice coroutine must finish.");
        }

        [UnityTest]
        public IEnumerator AllPlacedInstruments_AllowRepeatedFreePracticeWithoutChangingAnySavedProgressOrReward()
        {
            yield return HomePracticeFixture(false);
            var actor = Find("SimpleTopDownPlayerController").gameObject;
            object savedObject = Current;
            string checkpoint = JsonUtility.ToJson(Current);
            string path = (string)Get(save, "validationSavePath"), disk = File.ReadAllText(path);
            foreach (var station in All("HomeEquipmentStation").OrderBy(s => Get(s, "equipmentId").ToString()))
            {
                Set(station, "practiceSeconds", .24f);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var pose = CapturePracticePose(actor);
                    yield return BeginPractice(station, actor);
                    yield return FinishPractice(station);
                    AssertPracticeRestored(station, pose);
                    Assert.That(Current, Is.SameAs(savedObject));
                    Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(checkpoint), "Free practice must retain day, beat, practiced days, purchases, plates and rewards.");
                    Assert.That(File.ReadAllText(path), Is.EqualTo(disk), "Free practice must not write a new progression checkpoint.");
                    Assert.That(Call(station, "get_IsAvailable"), Is.EqualTo(true));
                    Assert.That(((GameObject)Get(station, "equipmentVisual")).activeSelf, Is.True);
                }
            }
        }

        [UnityTest]
        public IEnumerator FreePractice_CancelAndDisableRestoreTheCameraBodyAndBusyStateWithoutSavingProgress()
        {
            yield return HomePracticeFixture(false);
            var actor = Find("SimpleTopDownPlayerController").gameObject;
            var station = All("HomeEquipmentStation").Single(s => Get(s, "equipmentId").ToString() == "DawSoftware");
            Set(station, "practiceSeconds", .75f);
            string checkpoint = JsonUtility.ToJson(Current);
            string path = (string)Get(save, "validationSavePath"), disk = File.ReadAllText(path);
            foreach (bool disable in new[] { false, true })
            {
                var pose = CapturePracticePose(actor);
                yield return BeginPractice(station, actor);
                yield return null;
                if (disable) station.gameObject.SetActive(false);
                else Call(station, "CancelPractice");
                AssertPracticeRestored(station, pose);
                station.gameObject.SetActive(true);
                yield return new WaitForSecondsRealtime(.8f);
                Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(checkpoint), "A cancelled coroutine must not grant delayed practice completion.");
                Assert.That(File.ReadAllText(path), Is.EqualTo(disk));
                Assert.That(Call(station, "get_IsAvailable"), Is.EqualTo(true));
            }
        }

        [UnityTest]
        public IEnumerator EarlierEquipmentPracticeAndCancelledRequiredPractice_DoNotCompleteDay3_ThenPluginCompletesExactlyOnce()
        {
            yield return HomePracticeFixture(true);
            var actor = Find("SimpleTopDownPlayerController").gameObject;
            var stations = All("HomeEquipmentStation");
            var earlier = stations.Single(s => Get(s, "equipmentId").ToString() == "SamplePackDrumKit");
            var required = stations.Single(s => Get(s, "equipmentId").ToString() == "Plugin");
            string before = JsonUtility.ToJson(Current);
            string path = (string)Get(save, "validationSavePath"), disk = File.ReadAllText(path);
            Set(earlier, "practiceSeconds", .24f);
            var pose = CapturePracticePose(actor);
            yield return BeginPractice(earlier, actor);
            yield return FinishPractice(earlier);
            AssertPracticeRestored(earlier, pose);
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(before), "An older placed instrument is free practice even during the required Plugin objective.");
            Set(required, "practiceSeconds", .75f);
            pose = CapturePracticePose(actor);
            yield return BeginPractice(required, actor);
            Call(required, "CancelPractice");
            AssertPracticeRestored(required, pose);
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(before));
            Assert.That(File.ReadAllText(path), Is.EqualTo(disk));

            object expected = JsonUtility.FromJson(before, Current.GetType());
            var expectedProgress = Get(expected, "dayWorld");
            Set(expectedProgress, "beat", Beat("Sleep"));
            ((IList)Get(expectedProgress, "practicedDays")).Add(3);
            Set(required, "practiceSeconds", .24f);
            pose = CapturePracticePose(actor);
            yield return BeginPractice(required, actor);
            yield return FinishPractice(required);
            AssertPracticeRestored(required, pose);
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(JsonUtility.ToJson(expected)), "Only the required practice beat and one practiced-day marker may change.");
            Assert.That(JsonUtility.ToJson(JsonUtility.FromJson(File.ReadAllText(path), Current.GetType())), Is.EqualTo(JsonUtility.ToJson(expected)));
            string completed = JsonUtility.ToJson(Current), completedDisk = File.ReadAllText(path);
            pose = CapturePracticePose(actor);
            yield return BeginPractice(required, actor);
            yield return FinishPractice(required);
            AssertPracticeRestored(required, pose);
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(completed));
            Assert.That(File.ReadAllText(path), Is.EqualTo(completedDisk));
        }

        void ConfigureDayThreeAfterWork(string beat, bool pluginOwned)
        {
            Set(save, "current", RuntimeReflection.InvokeStatic("SashimiBoy.SaveData", "CreateNew"));
            Set(Progress, "active", true); Set(Progress, "day", 3); Set(Progress, "beat", Beat(beat));
            Set(Progress, "nightsSlept", 2); Set(Progress, "pendingStageClear", 0);
            Set(Progress, "checkpointScene", "FishShopDialogue"); Set(Progress, "checkpointSpawn", "StageReturn");
            Set(Current, "currentStageId", "STAGE_03_SOLE");
            foreach (string id in new[] { "STAGE_01_SALMON", "STAGE_02_ROCKFISH", "STAGE_03_SOLE" })
            { Call(Current, "UnlockStage", id); Call(Current, "MarkStageCleared", id); }
            foreach (string id in new[] { "SamplePackDrumKit", "DawSoftware" })
            {
                ((IList)Get(Current, "ownedEquipmentIds")).Add(id);
                ((IList)Get(Progress, "placedEquipment")).Add(id);
            }
            ((IList)Get(Progress, "practicedDays")).Add(1); ((IList)Get(Progress, "practicedDays")).Add(2);
            ((IList)Get(Progress, "completedDialogues")).Add("3:junseok");
            if (pluginOwned)
            {
                ((IList)Get(Current, "ownedEquipmentIds")).Add("Plugin");
                ((IList)Get(Progress, "placedEquipment")).Add("Plugin");
            }
        }

        [UnityTest]
        public IEnumerator Day3DialogueEventsAndRealWorldRoute_RequireCheolsuThenJaehoonBeforeTheEquipmentShop()
        {
            ConfigureDayThreeAfterWork("AfterWorkConversation", false);
            yield return SceneManager.LoadSceneAsync("FishShopDialogue");
            yield return new WaitForSecondsRealtime(.4f);
            var cheolsu = All("DayWorldNpc").Single(c => Get(c, "npcId").ToString() == "cheolsu");
            Assert.That(Call(cheolsu, "get_IsAvailable"), Is.EqualTo(true));
            // Complete the same persistent event used by the dialogue runner;
            // route changes below use the actual runtime scene transition.
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules", "FinishDialogue", Current, "cheolsu"), Is.EqualTo(true));
            Call(flow, "Commit");
            Assert.That(Get(Progress, "beat").ToString(), Is.EqualTo("EveningConversation"));
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules", "RequiredNpc", Current), Is.EqualTo("jaehoon"));
            Call(flow, "LoadWorld", "Street", "FishShopDialogue");
            yield return WaitScene("Street");
            var jaehoon = All("DayWorldNpc").Single(c => Get(c, "npcId").ToString() == "jaehoon");
            Assert.That(Call(jaehoon, "get_IsAvailable"), Is.EqualTo(true));
            string blocked = JsonUtility.ToJson(Current);
            Call(flow, "LoadWorld", "EquipmentShop", "Entry");
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Street"), "The real world route must reject the shop before Jaehoon.");
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(blocked));
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules", "FinishDialogue", Current, "jaehoon"), Is.EqualTo(true));
            Call(flow, "Commit");
            Assert.That(Get(Progress, "beat").ToString(), Is.EqualTo("Purchase"));
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules", "RequiredNpc", Current), Is.EqualTo(""));
            Call(flow, "LoadWorld", "EquipmentShop", "Entry");
            yield return WaitScene("EquipmentShop");
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules", "ShopAvailable", Current), Is.EqualTo(true));
            Assert.That(((IList)Get(Current, "ownedEquipmentIds")).Contains("Plugin"), Is.False, "Opening the shop must not buy or grant the reward.");
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator LegacyDay3PurchaseAndPracticeDiskCheckpoints_RouteToJaehoonAndResumeOnlyTheRemainingStep()
        {
            foreach (string remaining in new[] { "Purchase", "Practice" })
            {
                // Enter Continue from a distinct scene for both profiles so
                // WaitScene cannot observe the preceding Street instance.
                yield return SceneManager.LoadSceneAsync("Bootstrap");
                yield return null;
                ConfigureDayThreeAfterWork(remaining, remaining == "Practice");
                ((IList)Get(Progress, "completedDialogues")).Add("3:cheolsu");
                Set(Progress, "checkpointScene", "KevinHome"); Set(Progress, "checkpointSpawn", "Entry");
                Call(save, "Save");
                object expected = JsonUtility.FromJson(JsonUtility.ToJson(Current), Current.GetType());
                var expectedProgress = Get(expected, "dayWorld");
                Set(expectedProgress, "beat", Beat("EveningConversation"));
                Set(expectedProgress, "checkpointScene", "Street");
                Set(expectedProgress, "checkpointSpawn", "FishShopDialogue");
                string migrated = JsonUtility.ToJson(expected);
                Call(save, "LoadOrCreate");
                Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(migrated), "Disk migration must retain every clear, item, reward and practiced day.");
                Call(save, "Save"); Call(save, "LoadOrCreate");
                Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(migrated), "Migration must be idempotent on a real disk round trip.");
                Call(flow, "SetBusy", false);
                Call(flow, "ContinueGame");
                yield return WaitScene("Street");
                Assert.That(Get(Progress, "checkpointSpawn"), Is.EqualTo("FishShopDialogue"));
                var jaehoon = All("DayWorldNpc").Single(c => Get(c, "npcId").ToString() == "jaehoon");
                Assert.That(Call(jaehoon, "get_IsAvailable"), Is.EqualTo(true));
                Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules", "FinishDialogue", Current, "jaehoon"), Is.EqualTo(true));
                Call(flow, "Commit");
                ((IList)Get(expectedProgress, "completedDialogues")).Add("3:jaehoon");
                Set(expectedProgress, "beat", Beat(remaining));
                string resumed = JsonUtility.ToJson(expected);
                Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(resumed), "Jaehoon must resume purchase or practice without granting or charging again.");
                Call(save, "LoadOrCreate");
                Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(resumed));
            }
        }

        [UnityTest]
        public IEnumerator Day3WorkstationPrompt_UsesFlounderDisplayNameAndApprovedStage()
        {
            Set(Progress,"active",true);Set(Progress,"day",3);Set(Progress,"beat",Beat("Work"));
            Call(Current,"UnlockStage","STAGE_03_SOLE");
            yield return SceneManager.LoadSceneAsync("FishShopDialogue");
            yield return null;
            var starter=Find("StageStarterInteractable");
            Assert.That(starter,Is.Not.Null);
            Assert.That(Call(starter,"get_Prompt"),Is.EqualTo("가자미 손질 시작"));
            Assert.That(Call(starter,"get_IsAvailable"),Is.EqualTo(true));
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
            // A supported completed Day3 checkpoint already passed all prior
            // clears and purchases; a new-game Stage1 ID is a migration fixture.
            Set(Current,"currentStageId","STAGE_04_MULLET");
            foreach(string id in new[]{"STAGE_01_SALMON","STAGE_02_ROCKFISH","STAGE_03_SOLE"})
            {
                Call(Current,"UnlockStage",id);Call(Current,"MarkStageCleared",id);
            }
            Call(Current,"UnlockStage","STAGE_04_MULLET");
            ((IList)Get(Current,"ownedEquipmentIds")).Add("SamplePackDrumKit");
            ((IList)Get(Current,"ownedEquipmentIds")).Add("DawSoftware");
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
