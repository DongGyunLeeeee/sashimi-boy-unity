using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

namespace SashimiBoy.Tests
{
    // Interaction-path integration. Stage1 uses judged note inputs, not physical keyboard performance.
    // Stage2 also has a real DSP-clock run; automated inputs do not claim physical keyboard/music-feel verification.
    public sealed class DayWorldPlayModeTests
    {
        Component save,flow;
        object originalSave;
        object originalAuto,originalPath;
        object Current => RuntimeReflection.GetField(save,"current");
        object Progress => RuntimeReflection.GetField(Current,"dayWorld");
        string Beat => RuntimeReflection.GetField(Progress,"beat").ToString();
        int Day => (int)RuntimeReflection.GetField(Progress,"day");
        static Component Find(string name)=>RuntimeReflection.FindActiveComponent("SashimiBoy."+name);
        static Component[] All(string name)=>Resources.FindObjectsOfTypeAll(RuntimeReflection.RuntimeType("SashimiBoy."+name)).Cast<Component>().Where(c=>c.gameObject.scene.IsValid()&&c.gameObject.activeInHierarchy).ToArray();
        static object Call(object target,string method,params object[] args)=>RuntimeReflection.Invoke(target,method,args);
        static object Field(object target,string name)=>RuntimeReflection.GetField(target,name);
        static GameObject Actor=>Find("SimpleTopDownPlayerController").gameObject;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap");yield return null;
            save=Find("SaveManager");flow=Find("DayWorldFlow");
            Assert.That(save,Is.Not.Null);Assert.That(flow,Is.Not.Null);
            originalSave=Field(save,"current");originalAuto=Field(save,"autoSaveOnChange");originalPath=Field(save,"validationSavePath");
            Call(save,"ConfigureValidationProfile","automated-"+Guid.NewGuid().ToString("N"));
            RuntimeReflection.SetField(save,"autoSaveOnChange",true);
            RuntimeReflection.SetField(save,"current",RuntimeReflection.InvokeStatic("SashimiBoy.SaveData","CreateNew"));
            Call(flow,"SetBusy",false);yield return new WaitForSecondsRealtime(.35f);
        }
        [TearDown]
        public void TearDown()
        {
            if(save!=null){RuntimeReflection.SetField(save,"current",originalSave);RuntimeReflection.SetField(save,"autoSaveOnChange",originalAuto);RuntimeReflection.SetField(save,"validationSavePath",originalPath);}
            if(flow!=null)Call(flow,"SetBusy",false);
        }
        IEnumerator WaitScene(string scene)
        {
            float end=Time.realtimeSinceStartup+25f;
            while(SceneManager.GetActiveScene().name!=scene && Time.realtimeSinceStartup<end)yield return null;
            Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo(scene));
            yield return new WaitForSecondsRealtime(.45f);
            Assert.That(Object.FindObjectsByType<EventSystem>().Count(e=>e.isActiveAndEnabled),Is.EqualTo(1),scene);
            Assert.That(Object.FindObjectsByType<AudioListener>().Count(e=>e.isActiveAndEnabled),Is.EqualTo(1),scene);
        }
        IEnumerator Door(string destination)
        {
            var door=All("SceneDoor").FirstOrDefault(c=>(string)Field(c,"sceneName")==destination)
                ??All("ReturnToStreetDoor").FirstOrDefault(c=>(string)Field(c,"sceneName")==destination);
            Assert.That(door,Is.Not.Null,"Actual active door to "+destination);
            Call(door,"Interact",Actor);yield return WaitScene(destination);
            Capture(destination+"-entry-day"+Day+"-interaction-test");
        }
        IEnumerator Talk(string id,bool cancelFirst=false)
        {
            var npc=All("DayWorldNpc").Single(c=>(string)Field(c,"npcId")==id);
            Assert.That(npc.GetComponentsInChildren<Renderer>().Length,Is.GreaterThanOrEqualTo(2),"Owner body and actual face must render.");
            Call(npc,"Interact",Actor);yield return null;
            var runner=(Component)Field(npc,"runner");Assert.That(Call(runner,"get_IsRunning"),Is.EqualTo(true));
            if(id=="seongho") yield return new WaitForSecondsRealtime(1.2f);
            Capture(id+"-dialogue-interaction-test");
            if(cancelFirst)
            {
                string before=Beat;Call(runner,"Advance");Call(runner,"Cancel");yield return new WaitForSecondsRealtime(.35f);
                Assert.That(Beat,Is.EqualTo(before));Assert.That(Call(flow,"get_Busy"),Is.EqualTo(false));
                Assert.That(npc.gameObject.activeInHierarchy,Is.True,"Cancelled conversation must remain available.");
                Call(npc,"Interact",Actor);yield return null;
            }
            int remaining=40;
            while((bool)Call(runner,"get_IsRunning") && remaining-->0){Call(runner,"Advance");yield return null;}
            Assert.That(remaining,Is.GreaterThan(0));
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldFlow","get_InputSuppressed"),Is.EqualTo(true));
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(npc.gameObject.activeInHierarchy,Is.False,"Finished story NPC must leave the map.");
        }
        IEnumerator UseBed()
        {
            var bed=All("DayWorldInteractable").Single(c=>Field(c,"kind").ToString()=="Bed");
            Call(bed,"Interact",Actor);yield return new WaitForSecondsRealtime(1.85f);
        }
        IEnumerator NewGame()
        {
            yield return ChooseNewGameFace();
            yield return WaitScene("KevinHome");
            Assert.That(Beat,Is.EqualTo("MorningConversation"));
            Assert.That(Call(Find("SimpleTopDownPlayerController"),"get_InputEnabled"),Is.EqualTo(true));
            var bed=All("DayWorldInteractable").Single(c=>Field(c,"kind").ToString()=="Bed");
            Assert.That(Call(bed,"get_IsAvailable"),Is.EqualTo(false));
            Capture("home-morning-without-wake-button");
        }

        IEnumerator ChooseNewGameFace(int choice = 0)
        {
            ((Button)Field(Find("DayWorldSceneDirector"),"newGameButton")).onClick.Invoke();
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Bootstrap"), "New game first opens face selection.");
            var picker = Find("KevinCustomizationScreen");
            Assert.That(Call(picker,"get_IsOpen"), Is.EqualTo(true));
            ((Button[])Field(picker,"choiceButtons"))[choice].onClick.Invoke();
            yield return null;
            ((Button)Field(picker,"confirmButton")).onClick.Invoke();
        }

        [UnityTest]
        public IEnumerator OwnerLogo_FaceSelection_CancelThenConfirm_SaveContinueAndBothCookingBodies()
        {
            Call(save, "Save");
            string path = (string)Call(save, "get_SavePath"), before = File.ReadAllText(path);
            var director = Find("DayWorldSceneDirector");
            var logo = ((GameObject)Field(director, "menuRoot")).transform.Find("OwnerLogo").GetComponent<Image>();
            Assert.That(logo.sprite.name, Is.EqualTo("SashimiBoyLogo"));
            Capture("owner-logo-title");
            ((Button)Field(director, "newGameButton")).onClick.Invoke(); yield return null;
            var picker = Find("KevinCustomizationScreen"); var buttons = (Button[])Field(picker, "choiceButtons");
            Assert.That(buttons.Length, Is.EqualTo(4));
            var choices = (IList)Field(Field(picker, "catalog"), "choices");
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].onClick.Invoke(); yield return new WaitForSecondsRealtime(.2f);
                var preview = Field(picker, "preview");
                Assert.That(Call(preview, "get_SelectedFaceId"), Is.EqualTo(Field(choices[i], "id")));
                Assert.That(((SkinnedMeshRenderer)Field(preview, "faceRenderer")).sharedMesh, Is.EqualTo(Field(choices[i], "mesh")));
                Capture("owner-customize-" + Field(choices[i], "id"));
            }
            ((Button)Field(picker, "cancelButton")).onClick.Invoke(); yield return null;
            Assert.That(File.ReadAllText(path), Is.EqualTo(before), "Browsing/cancelling must not reset saved progress.");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Bootstrap"));
            yield return ChooseNewGameFace(3); yield return WaitScene("KevinHome");
            Assert.That(Day, Is.EqualTo(1)); Assert.That(Beat, Is.EqualTo("MorningConversation"));
            Assert.That(Field(Current, "kevinFaceId"), Is.EqualTo("WesternFace"));
            Assert.That(Call(Find("KevinAppearance"), "get_SelectedFaceId"), Is.EqualTo("WesternFace"));
            RoundTrip(); yield return ContinueSaved("KevinHome");
            Assert.That(Call(Find("KevinAppearance"), "get_SelectedFaceId"), Is.EqualTo("WesternFace"));
            yield return Door("Street"); yield return Talk("misuk");
            Assert.That(Call(Find("KevinAppearance"), "get_SelectedFaceId"), Is.EqualTo("WesternFace"));
            yield return Door("FishShopDialogue");
            // Actual physical route from the entry through the hall into the kitchen.
            yield return Walk(new Vector3(0f,0f,-.6f));
            yield return Walk(new Vector3(-4f,0f,-.6f));
            yield return Walk(new Vector3(-4f,0f,1.25f));
            var starter = Find("StageStarterInteractable");
            yield return LookAt(starter.GetComponent<Collider>().bounds.center);
            Call(Find("InteractionSensor"),"FindCurrent");
            Assert.That(Call(Find("InteractionSensor"),"get_Current"), Is.EqualTo(starter));
            Capture("owner-kitchen-start-interaction");
            Call(starter,"Interact",Actor); yield return WaitScene("Stage01_Salmon");
            Assert.That(Call(Find("KevinAppearance"), "get_SelectedFaceId"), Is.EqualTo("WesternFace"));
            Capture("owner-face-stage1-connected-body");
            Call(Find("DayWorldStageBridge"),"ReturnToShop"); yield return WaitScene("FishShopDialogue");
            Assert.That(Actor.transform.position.z, Is.GreaterThan(.9f), "Return preserves the kitchen entry pose.");
            // Direct scene load checks the second cooking prefab; it does not grant a Stage2 clear.
            yield return SceneManager.LoadSceneAsync("Stage02_Rockfish"); yield return new WaitForSecondsRealtime(.5f);
            Assert.That(Call(Find("KevinAppearance"), "get_SelectedFaceId"), Is.EqualTo("WesternFace"));
            Assert.That(((IList)Field(Current, "clearedStageIds")).Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator StandingStart_AndLegacyWakeResume_DoNotRequireBedInteraction()
        {
            yield return ChooseNewGameFace();
            yield return WaitScene("KevinHome");
            for(int day=1;day<=2;day++)
            {
                if(day==2)
                {
                    RuntimeReflection.SetField(Progress,"day",2);
                    RuntimeReflection.SetField(Progress,"beat",Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"),"Wake"));
                    RuntimeReflection.SetField(Progress,"checkpointScene","Street");
                    RuntimeReflection.SetField(Progress,"checkpointSpawn","Entry");
                    RoundTrip();
                    yield return SceneManager.LoadSceneAsync("Bootstrap");yield return new WaitForSecondsRealtime(.4f);
                    ((Button)Field(Find("DayWorldSceneDirector"),"continueButton")).onClick.Invoke();
                    yield return WaitScene("KevinHome");
                }
                var player=Find("SimpleTopDownPlayerController");
                yield return new WaitForSecondsRealtime(.4f);
                Assert.That(Call(player,"get_InputEnabled"),Is.EqualTo(true));
                Assert.That(Beat,Is.EqualTo("MorningConversation"));
                Assert.That((string)RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules","Objective",Current),Does.Not.Contain("일어나기"));
                var bed=All("DayWorldInteractable").Single(c=>Field(c,"kind").ToString()=="Bed");
                Assert.That(Call(bed,"get_IsAvailable"),Is.EqualTo(false));
                Assert.That(((IList)Field(Current,"clearedStageIds")).Count,Is.Zero);
                Capture("standing-morning-day"+day);
                yield return Door("Street");
            }
        }

        [UnityTest]
        public IEnumerator LegacyCompletedSave_ResumesStageTwoPendingScreenWithoutNewRewards()
        {
            RuntimeReflection.SetField(Progress,"active",true);
            RuntimeReflection.SetField(Progress,"day",2);
            RuntimeReflection.SetField(Progress,"beat",Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"),"Complete"));
            RuntimeReflection.SetField(Progress,"nightsSlept",2);
            RuntimeReflection.SetField(Progress,"pendingStageClear",0);
            RuntimeReflection.SetField(Progress,"checkpointScene","Street");
            Call(save,"Save");Call(save,"LoadOrCreate");
            Assert.That(Field(Progress,"pendingStageClear"),Is.EqualTo(2));
            Call(Find("DayWorldSceneDirector"),"Refresh",Current);
            Assert.That(((Button)Field(Find("DayWorldSceneDirector"),"continueButton")).interactable,Is.True);
            ((Button)Field(Find("DayWorldSceneDirector"),"continueButton")).onClick.Invoke();
            yield return WaitScene("KevinHome");
            var screen=Find("DayWorldStageClearScreen");Assert.That(screen,Is.Not.Null);
            Assert.That(((Text)Field(screen,"title")).text,Is.EqualTo("2스테이지 클리어"));
            Assert.That(((Button)Field(screen,"continueButton")).interactable,Is.False);
            Assert.That(((IList)Field(Current,"clearedStageIds")).Count,Is.Zero,"Migration does not grant rewards.");
            Assert.That(Call(Find("SimpleTopDownPlayerController"),"get_InputEnabled"),Is.EqualTo(false));
        }

        [UnityTest]
        public IEnumerator SavedClear_TitleNewGame_ConfirmStartsFreshWithNormalInput()
        {
            RuntimeReflection.SetField(Progress,"active",true);
            RuntimeReflection.SetField(Progress,"day",2);
            RuntimeReflection.SetField(Progress,"beat",Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"),"Complete"));
            RuntimeReflection.SetField(Progress,"nightsSlept",2);
            RuntimeReflection.SetField(Progress,"pendingStageClear",2);
            RuntimeReflection.SetField(Progress,"checkpointScene","KevinHome");
            ((IList)Field(Current,"ownedEquipmentIds")).Add("SamplePackDrumKit");
            Call(save,"Save");
            Call(flow,"ContinueGame");yield return WaitScene("KevinHome");
            var clear = Find("DayWorldStageClearScreen");
            ((Button)Field(clear,"saveAndExitButton")).onClick.Invoke();
            yield return WaitScene("Bootstrap");
            yield return ChooseNewGameFace(3);yield return WaitScene("KevinHome");
            Assert.That(Day,Is.EqualTo(1));Assert.That(Beat,Is.EqualTo("MorningConversation"));
            Assert.That(Field(Progress,"pendingStageClear"),Is.EqualTo(0));
            Assert.That(Field(Progress,"nightsSlept"),Is.EqualTo(0));
            Assert.That(((IList)Field(Current,"ownedEquipmentIds")).Count,Is.Zero);
            Assert.That(Field(Current,"kevinFaceId"),Is.EqualTo("WesternFace"));
            Assert.That(Call(Find("SimpleTopDownPlayerController"),"get_InputEnabled"),Is.EqualTo(true));
            RoundTrip();
        }

        [UnityTest]
        public IEnumerator OwnerJudgements_RealJudgedInputs_EmptyAndMiss_ShowPngInBothStages()
        {
            foreach(string scene in new[]{"Stage01_Salmon","Stage02_Rockfish"})
            {
                if(scene=="Stage02_Rockfish") yield return Day2Ready();
                else { yield return SceneManager.LoadSceneAsync(scene);yield return new WaitForSecondsRealtime(.5f); }
                var timing=Find("Stage01SalmonTimingScaffold");var clock=Field(timing,"audioClock");Call(clock,"Stop");
                var hud=Find("Stage01SalmonHUD");var image=(Image)Field(hud,"judgementImage");
                var text=(Text)Field(hud,"lastJudgementText");var detail=(Text)Field(hud,"judgementDetail");
                var notes=(IList)Field(Field(timing,"notePatternProvider"),"runtimeNotes");
                string[] expected={"nasty","clean","slipped","whack"};double[] offsets={0d,.065d,.115d};
                double last=0d;
                for(int i=0;i<4;i++)
                {
                    last=i<3?(double)Field(notes[i],"songTimeSeconds")+offsets[i]:last;
                    RuntimeReflection.SetField(clock,"frozenSongTimeMs",last*1000d);
                    Call(timing,"ResolveGameplayInput",last);yield return null;
                    Assert.That(image.enabled,Is.True);Assert.That(image.sprite.name,Is.EqualTo(expected[i]));
                    Assert.That(image.preserveAspect,Is.True);Assert.That(text.enabled,Is.False,"No duplicate text badge over the PNG.");
                    Assert.That(detail.text.ToUpperInvariant(),Does.Not.Contain(expected[i].ToUpperInvariant()));
                    Capture("owner-judgement-"+scene+"-"+expected[i]);
                }
                Call(Field(timing,"activeNoteTracker"),"ProcessExpiredNotes",(double)Field(notes[3],"songTimeSeconds")+.141d,140d);
                yield return null;
                Assert.That(image.sprite.name,Is.EqualTo("whack"));Assert.That(detail.text,Is.EqualTo("놓침"));
                Call(hud,"ResetForRetry");Assert.That(image.enabled,Is.False);Assert.That(detail.enabled,Is.False);Assert.That(text.enabled,Is.True);
            }
        }
        IEnumerator BuyAndGoHome(string expectedEquipment)
        {
            yield return Door("EquipmentShop");
            yield return Walk(new Vector3(0f,0f,.25f));
            var service=All("DayWorldInteractable").Single(c=>Field(c,"kind").ToString()=="Shop");
            Assert.That(service.name, Is.EqualTo("Owner_EquipmentShopOwner"), "The owner, not the counter, owns purchasing.");
            Assert.That(Call(service,"get_IsAvailable"),Is.EqualTo(true));
            yield return LookAt(service.GetComponent<Collider>().bounds.center);
            Call(Find("InteractionSensor"),"FindCurrent");
            Assert.That(Call(Find("InteractionSensor"),"get_Current"),Is.EqualTo(service));
            Capture("visibility-shop-purchase-available-day"+Day);
            Assert.That(Find("DayWorldSceneDirector").transform.Find("VenueAssets/Owner_EquipmentShopOwner").gameObject.activeInHierarchy,Is.True);
            var director=Find("DayWorldSceneDirector");Call(director,"OpenShop");yield return null;
            var shop=Find("EquipmentShopController");Assert.That(shop,Is.Not.Null);
            Capture("shop-"+Day+"-interaction-test");
            Call(shop,"BuyRecommended");Assert.That(Beat,Is.EqualTo("Placement"));
            Assert.That(Call(service,"get_IsAvailable"),Is.EqualTo(false));
            Assert.That(Find("DayWorldSceneDirector").transform.Find("ShopPrompt").gameObject.activeSelf,Is.False);
            Assert.That(((IList)Field(Current,"ownedEquipmentIds")).Contains(expectedEquipment),Is.True);
            string before=JsonUtility.ToJson(Current);Call(shop,"BuyRecommended");Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(before),"Repeated purchase is inert.");
            RoundTrip();Assert.That(Beat,Is.EqualTo("Placement"));
            Call(director,"CloseShop");yield return new WaitForSecondsRealtime(.35f);
            yield return ContinueSaved("EquipmentShop");Assert.That(Beat,Is.EqualTo("Placement"));
            yield return Door("Street");yield return WalkToStreetDoor("KevinHome");yield return Door("KevinHome");
        }
        IEnumerator PlaceAndPractice(string equipment)
        {
            var station=All("HomeEquipmentStation").Single(c=>Field(c,"equipmentId").ToString()==equipment);
            Assert.That(((GameObject)Field(station,"equipmentVisual")).activeSelf,Is.False);
            Assert.That(station.transform.Find("StationName").gameObject.activeSelf,Is.True);
            yield return Walk(new Vector3(0f,0f,-.55f));
            yield return Walk(((Transform)Field(station,"playerPosition")).position);
            yield return LookAt(station.GetComponent<Collider>().bounds.center);
            Call(Find("InteractionSensor"),"FindCurrent");
            Assert.That(Call(Find("InteractionSensor"),"get_Current"),Is.EqualTo(station),"Purchased equipment placement must be discoverable through the real sensor.");
            Capture("visibility-home-placement-available-day"+Day);
            Call(station,"Interact",Actor);Assert.That(Beat,Is.EqualTo("Practice"));
            RoundTrip();
            yield return ContinueSaved("KevinHome");
            station=All("HomeEquipmentStation").Single(c=>Field(c,"equipmentId").ToString()==equipment);
            Assert.That(((GameObject)Field(station,"equipmentVisual")).activeSelf,Is.True);
            Capture("home-placed-day"+Day+"-interaction-test");
            Call(station,"Interact",Actor);yield return new WaitForSecondsRealtime(1.0f);
            Assert.That(Call(station,"get_IsPracticing"),Is.EqualTo(true));
            Capture("home-practice-day"+Day+"-interaction-test");
            var body=Find("KevinBodyRig");
            Assert.That((float)Call(body,"get_MaximumGripError"),Is.LessThan(.08f),"Kevin's connected palms must reach the real equipment targets.");
            Vector3 face=Camera.main.WorldToViewportPoint(((Transform)Field(body,"eyeAnchor")).position);
            Assert.That(face.y,Is.InRange(.1f,.9f),"The practice Game camera must include Kevin's face.");
            yield return new WaitForSecondsRealtime(4f);Assert.That(Beat,Is.EqualTo("Sleep"));
            Assert.That(station.transform.Find("StationName").gameObject.activeSelf,Is.False);
            Assert.That(((GameObject)Field(station,"equipmentVisual")).activeSelf,Is.True,"Finished practice keeps the purchased instrument.");
            int clearedDay=Day;
            yield return UseBed();
            var complete=Find("DayWorldStageClearScreen");
            Assert.That(complete,Is.Not.Null);
            Assert.That(((Text)Field(complete,"title")).text,Is.EqualTo(clearedDay+"스테이지 클리어"));
            Assert.That(Call(flow,"get_Busy"),Is.EqualTo(true));
            Assert.That(Call(Find("SimpleTopDownPlayerController"),"get_InputEnabled"),Is.EqualTo(false));
            Assert.That(Call(complete,"SaveCheckpointForExit"),Is.EqualTo(true));
            string actualPath=(string)Field(save,"validationSavePath");
            try
            {
                RuntimeReflection.SetField(save,"validationSavePath",Path.GetDirectoryName(actualPath));
                ((Button)Field(complete,"saveAndExitButton")).onClick.Invoke();
                yield return null;
                Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo("KevinHome"));
                Assert.That(((Text)Field(complete,"saveStatus")).text,Does.Contain("저장하지 못했습니다"));
                Assert.That(complete.gameObject.activeInHierarchy,Is.True,"Saving failure must keep the screen open.");
                string pendingBefore=JsonUtility.ToJson(Current);
                ((Button)Field(complete,"continueButton")).onClick.Invoke();
                Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(pendingBefore),"Failed save must preserve the clear checkpoint for retry.");
                Assert.That(complete.gameObject.activeInHierarchy,Is.True);
                Capture("stage-clear-save-failure-day"+clearedDay);
            }
            finally { RuntimeReflection.SetField(save,"validationSavePath",actualPath); }
            Assert.That(Call(complete,"SaveCheckpointForExit"),Is.EqualTo(true));
            Capture("stage-clear-screen-day"+clearedDay);
            string checkpoint = JsonUtility.ToJson(Current);
            ((Button)Field(complete,"saveAndExitButton")).onClick.Invoke();
            yield return WaitScene("Bootstrap");
            Assert.That(Call(flow,"get_Busy"),Is.EqualTo(false),"The title buttons must be usable after leaving the clear screen.");
            Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible,Is.True);
            var titleDirector = Find("DayWorldSceneDirector");
            Assert.That(((GameObject)Field(titleDirector,"menuRoot")).activeInHierarchy,Is.True);
            Assert.That(All("DayWorldStageClearScreen"),Is.Empty);
            Call(save,"LoadOrCreate");
            Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(checkpoint),"Title return and a disk reload must preserve the completed checkpoint.");
            Capture("saved-title-menu-day"+clearedDay);
            // Opening and cancelling New Game must keep the saved continuation intact.
            ((Button)Field(titleDirector,"newGameButton")).onClick.Invoke();yield return null;
            ((Button)Field(Find("KevinCustomizationScreen"),"cancelButton")).onClick.Invoke();yield return null;
            Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(checkpoint));
            var resume = (Button)Field(titleDirector,"continueButton");
            Assert.That(resume.interactable,Is.True);resume.onClick.Invoke();
            yield return WaitScene("KevinHome");
            Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(checkpoint),"Continue must not duplicate progress or rewards.");
            complete=Find("DayWorldStageClearScreen");
            Assert.That(complete,Is.Not.Null,"Saved pending clear must resume before advancing.");
            Assert.That(Call(Find("SimpleTopDownPlayerController"),"get_InputEnabled"),Is.EqualTo(false));
            Assert.That(Cursor.lockState,Is.EqualTo(CursorLockMode.None));
            var continueStage=(Button)Field(complete,"continueButton");
            if(clearedDay==2)
            {
                Assert.That(continueStage.interactable,Is.False);
                Assert.That(continueStage.GetComponentInChildren<Text>().text,Is.EqualTo("다음 스테이지 준비 중"));
                string unchanged=JsonUtility.ToJson(Current);
                continueStage.onClick.Invoke();
                Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(unchanged));
                Assert.That(Field(Progress,"pendingStageClear"),Is.EqualTo(2));
                Assert.That(Call(flow,"get_Busy"),Is.EqualTo(true));
                Assert.That(EventSystem.current.currentSelectedGameObject,Is.EqualTo(((Button)Field(complete,"saveAndExitButton")).gameObject));
                RoundTrip();
                yield break;
            }
            Assert.That(continueStage.interactable,Is.True);
            continueStage.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.8f);yield return WaitScene("KevinHome");
            Assert.That(Field(Progress,"pendingStageClear"),Is.EqualTo(0));
            Assert.That(Field(Progress,"nightsSlept"),Is.EqualTo(clearedDay));
            Assert.That(Call(flow,"get_Busy"),Is.EqualTo(false));
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules","ContinueAfterStageClear",Current),Is.EqualTo(false));
            RoundTrip();
        }
        IEnumerator ContinueSaved(string destination)
        {
            // Reload the real isolated save, then use the actual Bootstrap Continue button.
            RoundTrip();string before=JsonUtility.ToJson(Current);
            yield return SceneManager.LoadSceneAsync("Bootstrap");yield return new WaitForSecondsRealtime(.4f);
            var button=(Button)Field(Find("DayWorldSceneDirector"),"continueButton");
            Assert.That(button.interactable,Is.True);button.onClick.Invoke();yield return WaitScene(destination);
            Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(before),"Continue must not charge again, repeat rewards, duplicate equipment or advance the day.");
        }
        void RoundTrip()
        {
            Call(save,"Save");string path=(string)Call(save,"get_SavePath");Assert.That(path,Does.Contain("Logs"));
            string before=JsonUtility.ToJson(Current);Call(save,"LoadOrCreate");Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(before));
            Assert.That(File.Exists(path),Is.True,"A real isolated disk save was written and reloaded.");
        }

        [UnityTest]
        public IEnumerator Day1_Interactions_FailureRetry_RealStage1Judgements_Purchase_Practice_ThenDay2Stage2Entry()
        {
            yield return NewGame();yield return Door("Street");
            yield return Door("FishShopDialogue");Call(Find("StageStarterInteractable"),"Interact",Actor);yield return null;
            Assert.That(All("DayWorldNpc"),Is.Empty,"Cheolsu must not appear before Stage1 clear.");
            Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo("FishShopDialogue"));Assert.That(Beat,Is.EqualTo("MorningConversation"));
            yield return Door("Street");yield return Talk("misuk",true);Assert.That(Beat,Is.EqualTo("Work"));
            Capture("street-day1-interaction-test");yield return Door("FishShopDialogue");
            Call(Find("StageStarterInteractable"),"Interact",Actor);yield return WaitScene("Stage01_Salmon");
            Call(Find("DayWorldStageBridge"),"ReturnToShop");yield return WaitScene("FishShopDialogue");
            Assert.That(Beat,Is.EqualTo("Work"));Assert.That(((IList)Field(Current,"clearedStageIds")).Count,Is.Zero);
            Call(Find("StageStarterInteractable"),"Interact",Actor);yield return WaitScene("Stage01_Salmon");
            var timing=Find("Stage01SalmonTimingScaffold");Call(Field(timing,"audioClock"),"Stop");
            Call(Field(timing,"activeNoteTracker"),"ResolveRemainingNotesAsMissed");yield return new WaitForSecondsRealtime(.45f);Capture("stage1-failure-story-test");
            yield return WaitScene("FishShopDialogue");Assert.That(Beat,Is.EqualTo("Work"));Assert.That(((IList)Field(Current,"clearedStageIds")).Count,Is.Zero);
            Assert.That(All("DayWorldNpc"),Is.Empty,"Failure must not expose the after-work conversation.");
            Assert.That(Call(Find("StageStarterInteractable"),"get_IsAvailable"),Is.EqualTo(true));
            Call(Find("StageStarterInteractable"),"Interact",Actor);yield return WaitScene("Stage01_Salmon");
            timing=Find("Stage01SalmonTimingScaffold");var clock=Field(timing,"audioClock");Call(clock,"Stop");
            var notes=(IList)Field(Field(timing,"notePatternProvider"),"runtimeNotes");
            foreach(object note in notes){double time=(double)Field(note,"songTimeSeconds");RuntimeReflection.SetField(clock,"frozenSongTimeMs",time*1000d);Call(timing,"ResolveGameplayInput",time);yield return null;}
            // The frozen clock needs the existing song-end timeline advanced explicitly.
            Call(timing,"AdvanceStageTimeline",121d,121d);
            yield return new WaitForSecondsRealtime(.8f);Assert.That(Call(timing,"get_IsResultShown"),Is.EqualTo(true));
            Assert.That(Beat,Is.EqualTo("AfterWorkConversation"));Capture("stage1-clear-story-judged-test");
            Call(Find("Stage01PlayableFlow"),"ReturnToShop");yield return WaitScene("FishShopDialogue");
            Assert.That(All("DayWorldNpc").Select(c=>Field(c,"npcId")),Is.EqualTo(new[]{"cheolsu"}));
            Assert.That(Call(Find("StageStarterInteractable"),"get_IsAvailable"),Is.EqualTo(false));
            yield return Walk(new Vector3(-4f,0f,-.6f));
            yield return Walk(new Vector3(0f,0f,-.6f));
            yield return Walk(new Vector3(0f,0f,-2.1f));
            yield return Walk(new Vector3(1.3f,0f,-2.1f));
            var cheolsu = All("DayWorldNpc").Single();
            yield return LookAt(((Transform)Field(cheolsu,"faceAnchor")).position);
            Call(Find("InteractionSensor"),"FindCurrent");
            Assert.That(Call(Find("InteractionSensor"),"get_Current"),Is.EqualTo(cheolsu),"Seated Cheolsu must remain reachable through the real interaction sensor.");
            Capture("cheolsu-seated-before-dialogue");
            yield return Talk("cheolsu");Assert.That(Beat,Is.EqualTo("Purchase"));
            yield return Door("Street");yield return BuyAndGoHome("SamplePackDrumKit");yield return PlaceAndPractice("SamplePackDrumKit");
            Assert.That(Day,Is.EqualTo(2));Assert.That(Beat,Is.EqualTo("MorningConversation"));
            Assert.That(((IList)Field(Progress,"placedEquipment")).Contains("SamplePackDrumKit"),Is.True);
            yield return Door("Street");
            var seongho=All("DayWorldNpc").Single(c=>(string)Field(c,"npcId")=="seongho");Assert.That(Field(seongho,"motorcycle"),Is.Not.Null,"The supplied Seongho FBX includes the motorcycle.");
            yield return Talk("seongho");yield return Door("FishShopDialogue");
            Assert.That(Call(Find("StageStarterInteractable"),"get_Prompt"),Is.EqualTo("우럭 손질 시작"));
            Call(Find("StageStarterInteractable"),"Interact",Actor);yield return WaitScene("Stage02_Rockfish");
            Assert.That(Field(Find("Stage01SalmonTimingScaffold"),"stageId"),Is.EqualTo("STAGE_02_ROCKFISH"));
            Capture("day2-real-stage2-entry-story-test");
            Call(Find("DayWorldStageBridge"),"ReturnToShop");yield return WaitScene("FishShopDialogue");
            Assert.That(Beat,Is.EqualTo("Work"));
            Assert.That(((IList)Field(Current,"clearedStageIds")).Contains("STAGE_02_ROCKFISH"),Is.False);
            yield return ContinueSaved("FishShopDialogue");
            Assert.That(Call(Find("StageStarterInteractable"),"get_IsAvailable"),Is.EqualTo(true));
            yield return Door("Street");yield return Door("KevinHome");
            var drum=All("HomeEquipmentStation").Single(c=>Field(c,"equipmentId").ToString()=="SamplePackDrumKit");
            Assert.That(((GameObject)Field(drum,"equipmentVisual")).activeSelf,Is.True,"Day1 equipment remains visible when returning from Stage2.");
            Assert.That(drum.transform.Find("StationName").gameObject.activeSelf,Is.False);
            Assert.That(Beat,Is.EqualTo("Work"));Assert.That(Day,Is.EqualTo(2));
            Capture("home-after-stage2-return-interaction-test");
        }

        [UnityTest]
        public IEnumerator ProgressVisibility_HidesUnpurchasedEquipment_AndOnlyShowsNextNpc()
        {
            yield return NewGame();
            foreach(var station in All("HomeEquipmentStation"))
            {
                Assert.That(Call(station,"get_IsAvailable"),Is.EqualTo(false));
                Assert.That(station.transform.Find("StationName").gameObject.activeInHierarchy,Is.False);
                Assert.That(station.GetComponent<Collider>().enabled,Is.False);
                Assert.That(((GameObject)Field(station,"equipmentVisual")).activeSelf,Is.False);
            }
            var bed=All("DayWorldInteractable").Single(c=>Field(c,"kind").ToString()=="Bed");
            Assert.That(Call(bed,"get_IsAvailable"),Is.EqualTo(false));
            Assert.That(Find("DayWorldSceneDirector").transform.Find("BedLabel").gameObject.activeSelf,Is.False);
            Assert.That(Find("DayWorldSceneDirector").transform.Find("InteriorShell").GetComponentsInChildren<Transform>()
                .Count(t=>t.name.StartsWith("LabelPlaque")),Is.EqualTo(1),"Only the usable exit sign backing remains; no blank panels over future equipment.");
            yield return Walk(new Vector3(.8f,0f,-1.3f));
            yield return LookAt(new Vector3(-1.3f,1.05f,1.7f));
            Call(Find("InteractionSensor"),"FindCurrent");
            Assert.That(Call(Find("InteractionSensor"),"get_Current"),Is.Null);
            Capture("visibility-home-before-purchase-no-floating-prompts");
            yield return Door("Street");
            Assert.That(All("DayWorldNpc").Select(c=>Field(c,"npcId")),Is.EqualTo(new[]{"misuk"}));
            var npc=All("DayWorldNpc").Single();
            Vector3 face=((Transform)Field(npc,"faceAnchor")).position;
            yield return LookAt(face);Capture("visibility-misuk-before-conversation");
            yield return Talk("misuk",true);
            Assert.That(All("DayWorldNpc"),Is.Empty);
            yield return LookAt(face);Capture("visibility-misuk-after-conversation");
            yield return ContinueSaved("Street");
            Assert.That(All("DayWorldNpc"),Is.Empty,"Continue must not respawn completed NPCs.");
        }

        [UnityTest]
        public IEnumerator KevinCuteFace_NeckStaysInCollarWhenHeadTurns()
        {
            yield return NewGame();yield return Door("Street");
            var body=Actor.GetComponentInChildren(RuntimeReflection.RuntimeType("SashimiBoy.KevinBodyRig"),true);
            var skin=((Renderer[])Field(body,"headRenderers")).OfType<SkinnedMeshRenderer>().Single();
            var head=(Transform)Field(body,"head");
            var vertices=skin.sharedMesh.vertices;
            int bottom=Array.FindIndex(vertices,p=>p.y<-.075f);
            int upper=Array.FindIndex(vertices,p=>p.y>.15f);
            Assert.That(bottom,Is.GreaterThanOrEqualTo(0));Assert.That(upper,Is.GreaterThanOrEqualTo(0));
            var baked=new Mesh();
            Vector3 Point(int i){skin.BakeMesh(baked);return skin.transform.TransformPoint(baked.vertices[i]);}
            Vector3 collar=Point(bottom),skull=Point(upper);
            Quaternion original=head.localRotation;
            try
            {
                foreach(float yaw in new[]{-35f,35f})
                {
                    head.localRotation=original*Quaternion.Euler(15f,yaw,0f);
                    yield return new WaitForSecondsRealtime(.15f);
                    Assert.That(Vector3.Distance(Point(bottom),collar),Is.LessThan(.003f),"The lower neck must stay attached to the chest when the head turns.");
                    Assert.That(Vector3.Distance(Point(upper),skull),Is.GreaterThan(.025f),"The supplied face must actually follow the head bone.");
                    Assert.That(baked.vertices.All(p=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)),Is.True);
                }
            }
            finally{head.localRotation=original;Object.Destroy(baked);}
        }

        [UnityTest]
        public IEnumerator MisukCloseApproaches_ClearFaceAndBody_RestoreViewOnCancelAndFinish()
        {
            yield return NewGame();yield return Door("Street");
            var npc=All("DayWorldNpc").Single(c=>(string)Field(c,"npcId")=="misuk");
            var controller=Actor.GetComponent<CharacterController>();
            var rig=Find("KevinFirstPersonCameraRig");
            var target=(Transform)Field(npc,"faceAnchor");
            var body=Actor.GetComponentInChildren(RuntimeReflection.RuntimeType("SashimiBoy.KevinBodyRig"),true);
            var heads=(Renderer[])Field(body,"headRenderers");
            var offsets=new[]{new Vector3(0,0,1.2f),new Vector3(0,0,.75f),new Vector3(.85f,0,1f),
                new Vector3(-.85f,0,1f),new Vector3(1.2f,0,0),new Vector3(-1.2f,0,0)};
            for(int i=0;i<offsets.Length;i++)
            {
                controller.enabled=false;
                Vector3 spot=npc.transform.position+offsets[i];spot.y=Actor.transform.position.y;
                Actor.transform.position=spot;controller.enabled=true;
                yield return new WaitForSecondsRealtime(.15f);
                Vector3 angles=Quaternion.LookRotation(target.position-Camera.main.transform.position).eulerAngles;
                Call(rig,"RestoreStageEntryView",angles.y-Actor.transform.eulerAngles.y,angles.x);yield return null;
                Camera camera=Camera.main;Vector3 oldPosition=camera.transform.localPosition;
                Quaternion oldRotation=camera.transform.localRotation;float oldFov=camera.fieldOfView;
                var shadows=heads.Select(r=>r.shadowCastingMode).ToArray();
                Call(npc,"Interact",Actor);yield return new WaitForSecondsRealtime(.3f);
                Assert.That(Call(Field(npc,"runner"),"get_IsRunning"),Is.EqualTo(true));
                Vector3 face=camera.WorldToViewportPoint(target.position);
                Assert.That(face.z,Is.GreaterThan(.5f));Assert.That(face.x,Is.InRange(.2f,.8f));Assert.That(face.y,Is.InRange(.2f,.8f));
                foreach(var renderer in body.GetComponentsInChildren<Renderer>().Where(r=>r.enabled))
                {
                    Bounds bounds=renderer.bounds;bounds.Expand(.10f);
                    Assert.That(bounds.Contains(camera.transform.position),Is.False,renderer.name+" camera inside body");
                    var ray=new Ray(camera.transform.position,(target.position-camera.transform.position).normalized);
                    bool blocked=bounds.IntersectRay(ray,out float distance) && distance<Vector3.Distance(camera.transform.position,target.position)-.20f;
                    Assert.That(blocked,Is.False,renderer.name+" obscures Misuk from approach "+i);
                }
                Assert.That(heads.All(r=>r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.On),Is.True,"The external view must show Kevin's actual head.");
                Capture("misuk-clear-approach-"+i);
                var runner=Field(npc,"runner");
                if(i==offsets.Length-1)
                {
                    int remaining=40;
                    while((bool)Call(runner,"get_IsRunning") && remaining-->0){Call(runner,"Advance");yield return null;}
                    Assert.That(Beat,Is.EqualTo("Work"));
                }
                else Call(runner,"Cancel");
                yield return new WaitForSecondsRealtime(.35f);
                Assert.That(Vector3.Distance(camera.transform.localPosition,oldPosition),Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(camera.transform.localRotation,oldRotation),Is.LessThan(.01f));
                Assert.That(camera.fieldOfView,Is.EqualTo(oldFov));
                Assert.That(heads.Select(r=>r.shadowCastingMode).ToArray(),Is.EqualTo(shadows));
                Assert.That(Call(flow,"get_Busy"),Is.EqualTo(false));
            }
        }

        [UnityTest]
        public IEnumerator StreetEdges_ContinuousGround_BlockedPerimeter_AndFallRecovery()
        {
            yield return NewGame(); yield return Door("Street");
            var safety=Find("StreetGroundSafety"); Assert.That(safety,Is.Not.Null);
            yield return Walk(new Vector3(Actor.transform.position.x,0,-.8f));
            yield return Walk(new Vector3(20.5f,0,-.8f));
            yield return LookAt(new Vector3(4f,2f,5f)); Capture("street-block-east-walk");
            var controller=Actor.GetComponent<CharacterController>();
            for(int i=0;i<55;i++) { controller.Move(new Vector3(.1f,-.02f,0f)); yield return null; }
            Assert.That(Actor.transform.position.x,Is.LessThan(21.35f),"Visible east boundary must stop walking before the ground ends.");
            foreach(var point in new[]{new Vector3(20.5f,0,13.8f),new Vector3(-20.5f,0,13.8f),new Vector3(-20.5f,0,-13.9f),new Vector3(20.5f,0,-13.9f)})
            {
                yield return Walk(point); Assert.That(Actor.transform.position.y,Is.InRange(0f,2f));
            }
            Assert.That(Call(safety,"get_RecoveryCount"),Is.EqualTo(0),"Normal perimeter walking must be held by ground and walls, without recovery teleports.");
            yield return LookAt(new Vector3(0f,3f,-8f)); Capture("street-block-south-neighbors-walk");
            string before=JsonUtility.ToJson(Current);
            controller.enabled=false; Actor.transform.position=new Vector3(20f,-5f,-14f); controller.enabled=true;
            yield return null; yield return null;
            Assert.That(Actor.transform.position.y,Is.GreaterThan(0f));
            Assert.That(Call(safety,"get_RecoveryCount"),Is.EqualTo(1));
            Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(before),"Recovery must not alter story or saves.");
        }

        [UnityTest]
        public IEnumerator InteriorShells_EncloseViews_KeepExitWalkingAndInteraction()
        {
            yield return NewGame();
            foreach(string room in new[]{"KevinHome","FishShopDialogue","EquipmentShop","Club"})
            {
                if(SceneManager.GetActiveScene().name!=room){yield return Door("Street");yield return Door(room);}
                var shell=GameObject.Find("DayWorld_Integration").transform.Find("InteriorShell");
                Assert.That(shell,Is.Not.Null,room);
                var exit=All("SceneDoor").FirstOrDefault(c=>(string)Field(c,"sceneName")=="Street")
                    ??All("ReturnToStreetDoor").Single(c=>(string)Field(c,"sceneName")=="Street");
                Assert.That(exit.transform.position.x,Is.EqualTo(0f).Within(.01f),room+" entrance must match the centered facade.");
                var boundaries=shell.GetComponentsInChildren<Collider>().Concat(exit.GetComponentsInChildren<Collider>()).ToArray();
                Physics.SyncTransforms();
                for(int angle=0;angle<360;angle+=5)
                {
                    var ray=new Ray(new Vector3(0,1.45f,0),Quaternion.Euler(0,angle,0)*Vector3.forward);
                    Assert.That(boundaries.Any(c=>c.Raycast(ray,out _,25f)),Is.True,room+" has an open wall at "+angle);
                }
                Assert.That(shell.Find("Ceiling").GetComponent<Collider>().Raycast(new Ray(new Vector3(0,1.45f,0),Vector3.up),out _,10f),Is.True);
                Assert.That(boundaries.Where(c=>c.transform.IsChildOf(shell)).Any(c=>c.bounds.Contains(Camera.main.transform.position)),Is.False,"Entry camera inside wall");
                var rig=Find("KevinFirstPersonCameraRig");
                foreach(float yaw in new[]{0f,90f,180f,270f})
                {
                    Call(rig,"RestoreStageEntryView",yaw-Actor.transform.eulerAngles.y,8f);yield return null;
                    Capture(room+"-walls-yaw-"+yaw);
                }
                // Walk the real controller along the clear front aisle, then target the retained exit via the normal sensor.
                if(room=="KevinHome") yield return Walk(new Vector3(.8f,0,-1.3f));
                yield return Walk(new Vector3(0f,0f,-1.3f));
                yield return LookAt(exit.GetComponent<Collider>().bounds.center);
                Capture(room+"-centered-front-wall");
                Vector3 approach=exit.transform.position+Vector3.forward*1.25f;
                if(room=="KevinHome"){yield return Walk(new Vector3(.8f,0,-1.3f));yield return Walk(new Vector3(approach.x,0,-1.3f));}
                else {yield return Walk(new Vector3(Actor.transform.position.x,0,approach.z));}
                yield return Walk(approach);
                Vector3 angles=Quaternion.LookRotation(exit.GetComponent<Collider>().bounds.center-Camera.main.transform.position).eulerAngles;
                Call(rig,"RestoreStageEntryView",angles.y-Actor.transform.eulerAngles.y,angles.x);yield return null;
                var sensor=Find("InteractionSensor");Call(sensor,"FindCurrent");
                Assert.That(Call(sensor,"get_Current"),Is.EqualTo(exit),room+" exit hidden by new shell");
                Capture(room+"-exit-walk");
            }
        }

        [UnityTest]
        public IEnumerator OptionalClubAndShop_DoNotLockTravelOrAdvanceStory()
        {
            yield return NewGame();yield return Door("Street");yield return WalkToStreetDoor("Club");yield return Door("Club");
            Assert.That(Beat,Is.EqualTo("MorningConversation"));
            yield return Door("Street");yield return WalkToStreetDoor("EquipmentShop");yield return Door("EquipmentShop");
            Assert.That(Beat,Is.EqualTo("MorningConversation"));
            yield return Door("Street");yield return WalkToStreetDoor("FishShopDialogue");yield return Door("FishShopDialogue");
            yield return Door("Street");yield return WalkToStreetDoor("KevinHome");yield return Door("KevinHome");
        }

        IEnumerator WalkToStreetDoor(string destination)
        {
            // Move the real CharacterController through the plaza; this checks collision and sight,
            // not physical keyboard timing. No teleport or collider disabling is used on this route.
            var door=All("SceneDoor").Single(c=>(string)Field(c,"sceneName")==destination);
            if(destination=="KevinHome")
            {
                // Stay in the front aisle before moving left; Day2's parked motorcycle occupies the direct diagonal.
                yield return Walk(new Vector3(Actor.transform.position.x,0f,-1.5f));
                yield return Walk(new Vector3(door.transform.position.x,0f,-1.5f));
                yield return Walk(new Vector3(door.transform.position.x,0f,0f));
                var homeAngles=Quaternion.LookRotation(door.transform.position+Vector3.up*.6f-Camera.main.transform.position).eulerAngles;
                Call(Find("KevinFirstPersonCameraRig"),"RestoreStageEntryView",homeAngles.y-Actor.transform.eulerAngles.y,homeAngles.x);yield return null;
                Capture("KevinHome-owner-facade-street-view");
            }
            Vector3 target=door.transform.position+(destination=="KevinHome"?Vector3.forward:Vector3.back)*1.3f;
            yield return Walk(new Vector3(Actor.transform.position.x,0f,-3.3f));
            yield return Walk(new Vector3(target.x,0f,-3.3f));
            if(destination=="EquipmentShop")
            {
                // Record the crossing from the actual walking route before approaching the shop.
                Vector3 crossingView=Quaternion.LookRotation(new Vector3(0f,.075f,-.8f)-Camera.main.transform.position).eulerAngles;
                Call(Find("KevinFirstPersonCameraRig"),"RestoreStageEntryView",crossingView.y-Actor.transform.eulerAngles.y,crossingView.x);
                yield return null;Capture("crosswalk-from-plaza-walk-test");
            }
            yield return Walk(target);
            var rig=Find("KevinFirstPersonCameraRig");
            Vector3 angles=Quaternion.LookRotation(door.GetComponent<Collider>().bounds.center-Camera.main.transform.position).eulerAngles;
            Call(rig,"RestoreStageEntryView",angles.y-Actor.transform.eulerAngles.y,angles.x);yield return null;
            var sensor=Find("InteractionSensor");Call(sensor,"FindCurrent");
            Assert.That(Call(sensor,"get_Current"),Is.EqualTo(door),"The provided facade's actual door must be visible and targetable at walking distance.");
            Capture(destination+"-facade-collision-walk-test");
        }

        [UnityTest]
        public IEnumerator VenueAssets_RenderDuringWalking_AndRetainServiceAndStageTargets()
        {
            yield return NewGame(); yield return Door("Street"); yield return Door("EquipmentShop");
            var venue = GameObject.Find("DayWorld_Integration").transform.Find("VenueAssets");
            Assert.That(venue, Is.Not.Null);
            foreach (string id in new[] { "ElectronicDrumKit", "MidiKeyboardController", "ModularSynthesizer", "EquipmentShopOwner", "SpeakerBox", "WoodenSofa" })
            {
                var asset = venue.Find("Owner_" + id);
                Assert.That(asset, Is.Not.Null, id);
                Assert.That(asset.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.vertexCount), Is.GreaterThan(1000), id + " still uses a primitive");
                bool expectedVisible=id!="EquipmentShopOwner";
                Assert.That(asset.GetComponentsInChildren<Renderer>(true).All(r => r.enabled && r.gameObject.activeInHierarchy==expectedVisible && r.sharedMaterials.All(m => m != null && m.mainTexture != null)), Is.True, id);
            }
            yield return Walk(new Vector3(0f, 0f, .25f));
            yield return LookAt(new Vector3(0f, 1.52f, 3.15f)); Capture("EquipmentShop-owner-counter-venue-test");
            var shop = venue.Find("Owner_EquipmentShopOwner").GetComponent(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldInteractable"));
            yield return LookAt(shop.GetComponent<Collider>().bounds.center);
            var sensor = Find("InteractionSensor"); Call(sensor, "FindCurrent");
            Assert.That(Call(sensor, "get_Current"), Is.Null, "The purchase prompt must be absent before Stage1 and Cheolsu.");
            Assert.That(Call(shop,"get_IsAvailable"),Is.EqualTo(false));
            Call(shop,"Interact",Actor);
            Assert.That(Call(Find("DayWorldSceneDirector"),"get_ShopOpen"),Is.EqualTo(false));
            yield return Walk(new Vector3(-2.30f, 0f, .25f));
            yield return LookAt(new Vector3(-3.65f, 1f, 1.75f)); Capture("EquipmentShop-owner-instruments-venue-test");
            yield return Door("Street"); yield return Door("FishShopDialogue");
            yield return Walk(new Vector3(0f, 0f, -.10f));
            yield return LookAt(new Vector3(0f, 1.30f, 1.72f)); Capture("FishShop-owner-display-venue-test");
            yield return Walk(new Vector3(0f, 0f, -3.20f)); yield return Walk(new Vector3(-2.1f, 0f, -3.20f));
            var starter = Find("StageStarterInteractable");
            yield return LookAt(starter.GetComponent<Collider>().bounds.center);
            sensor = Find("InteractionSensor"); Call(sensor, "FindCurrent");
            Assert.That(Call(sensor, "get_Current"), Is.Null, "Stage1 prompt must wait for Misuk's conversation.");
            Capture("FishShop-owner-stage-workbench-venue-test");
            yield return Door("Street"); yield return Door("Club");
            yield return Walk(new Vector3(0f, 0f, -3.25f)); yield return Walk(new Vector3(0f, 0f, 1.15f));
            yield return LookAt(new Vector3(0f, 1.35f, 3.75f)); Capture("Club-owner-dj-venue-test");
            Assert.That(SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>())
                .Any(t=>t.gameObject.activeInHierarchy&&t.name.StartsWith("Audience_")&&int.TryParse(t.name.Substring(9),out _)),Is.False,"Unscheduled placeholder NPCs must not remain in the DayWorld map.");
            var stand = GameObject.Find("DJStand_PF_Club_DJStand");
            float platformTop = GameObject.Find("Stage").GetComponent<Renderer>().bounds.max.y;
            Assert.That(stand.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y), Is.EqualTo(platformTop).Within(.015f), "DJ stand must rest on the existing platform");
            Assert.That(Beat, Is.EqualTo("MorningConversation"), "Viewing venue assets must not advance the day");
        }

        IEnumerator LookAt(Vector3 point)
        {
            Vector3 angles = Quaternion.LookRotation(point - Camera.main.transform.position).eulerAngles;
            Call(Find("KevinFirstPersonCameraRig"), "RestoreStageEntryView", angles.y - Actor.transform.eulerAngles.y, angles.x);
            yield return null; yield return null;
        }
        IEnumerator Walk(Vector3 destination)
        {
            var actor=Actor;var controller=actor.GetComponent<CharacterController>();var rig=Find("KevinFirstPersonCameraRig");Call(rig,"SetUiBlocked",true);
            float until=Time.realtimeSinceStartup+30f;Vector3 delta;int steps=0;float simulatedSeconds=0f;
            do
            {
                delta=destination-actor.transform.position;delta.y=0f;
                // This verifies the collision route, not speed. Bound each real controller move independently of Editor frame timing.
                controller.Move(Vector3.ClampMagnitude(delta,.10f)+Vector3.down*.015f);
                steps++;simulatedSeconds+=Time.deltaTime;
                yield return null;
            } while(delta.magnitude>.15f && Time.realtimeSinceStartup<until);
            Assert.That(delta.magnitude,Is.LessThan(.2f),"World collision blocked the walking route from "+actor.transform.position+" to "+destination+
                "; steps="+steps+" simulationSeconds="+simulatedSeconds+" flags="+controller.collisionFlags+
                "; nearby="+string.Join(",",Physics.OverlapSphere(actor.transform.position,.75f).Select(c=>c.name)));
            Call(rig,"SetUiBlocked",false);yield return null;
        }

        IEnumerator Day2Ready()
        {
            // Explicit precondition fixture: retained Day1 inventory and the old pre-Stage2 arrival save.
            // Stage2 itself is always entered and judged through the real game components below.
            string json="{\"version\":1,\"unlockedStageIds\":[\"STAGE_01_SALMON\",\"STAGE_02_ROCKFISH\"],\"clearedStageIds\":[\"STAGE_01_SALMON\"],\"ownedEquipmentIds\":[\"SamplePackDrumKit\"],\"fishPlates\":[],\"dayWorld\":{\"active\":true,\"day\":2,\"beat\":2,\"checkpointScene\":\"FishShopDialogue\",\"checkpointSpawn\":\"Entry\",\"placedEquipment\":[\"SamplePackDrumKit\"],\"practicedDays\":[1],\"completedDialogues\":[\"1:misuk\",\"1:cheolsu\",\"2:seongho\"],\"nightsSlept\":1,\"reachedStageTwoBoundary\":true}}";
            RuntimeReflection.SetField(save,"current",JsonUtility.FromJson(json,RuntimeReflection.RuntimeType("SashimiBoy.SaveData")));
            Call(flow,"LoadWorld","FishShopDialogue","Entry");yield return WaitScene("FishShopDialogue");
            Assert.That(Call(Find("StageStarterInteractable"),"get_IsAvailable"),Is.EqualTo(true));
            Call(Find("StageStarterInteractable"),"Interact",Actor);yield return WaitScene("Stage02_Rockfish");
        }

        [UnityTest]
        public IEnumerator Stage2_ActualRockfish_VisibleSixPhases_AndCompletedPlate()
        {
            yield return Day2Ready();var timing=Find("Stage01SalmonTimingScaffold");var clock=Field(timing,"audioClock");Call(clock,"Stop");
            Assert.That(((AudioClip)Field(timing,"musicClip")).name,Is.EqualTo("rockfish1"));
            Assert.That(Field(timing,"stageId"),Is.EqualTo("STAGE_02_ROCKFISH"));
            var notes=(IList)Field(Field(timing,"notePatternProvider"),"runtimeNotes");
            var chart=Field(Field(timing,"semanticBeatmap"),"chart");var gates=(IList)Field(chart,"phases");
            var view=Find("Stage01ButcheryPresenter");
            for(int p=0;p<6;p++)
            {
                yield return new WaitForSecondsRealtime(.75f);
                Assert.That(Convert.ToInt32(Call(view,"get_VisiblePhase")),Is.EqualTo(p));
                Capture("stage2-visual-phase-"+p);
                var piece=p<2?(Component)Field(Field(view,"assembly"),"body"):p<4?(Component)Field(Field(view,"assembly"),"fillet"):((GameObject)Field(view,"filletHalf")).transform;
                var renderers=piece.GetComponentsInChildren<Renderer>().Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
                Assert.That(renderers,Is.Not.Empty);Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                Assert.That(bounds.size.x,Is.InRange(.25f,1.6f));Assert.That(bounds.size.y,Is.LessThan(.5f),"Opened fish must stay flat on the board.");
                Assert.That(bounds.min.y,Is.InRange(.82f,.97f));
                var board=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>()).Single(t=>t.name=="Stage01_BoardRoot");
                Assert.That(bounds.min.y,Is.GreaterThan(board.GetComponentsInChildren<Renderer>().Max(r=>r.bounds.max.y)),"Thin owner fillets must sit above the board and its grain strips.");
                for(int id=(int)Field(gates[p],"firstNoteId");id<=(int)Field(gates[p],"lastNoteId");id++)
                {
                    double time=(double)Field(notes[id],"songTimeSeconds");RuntimeReflection.SetField(clock,"frozenSongTimeMs",time*1000d);
                    ((AudioSource)Field(timing,"audioSource")).time=(float)time;
                    Call(timing,"ResolveGameplayInput",time);yield return null;
                }
            }
            Call(timing,"AdvanceStageTimeline",129d,129d);yield return new WaitForSecondsRealtime(.8f);
            Assert.That(Call(view,"get_PlateComplete"),Is.EqualTo(true));Assert.That(Call(view,"get_SliceCount"),Is.EqualTo(((Array)Field(view,"plateSlots")).Length));
            Assert.That(Beat,Is.EqualTo("AfterWorkConversation"));Capture("stage2-visual-complete-plate");
        }

        [UnityTest]
        public IEnumerator Stage2_RealDsp_FailureReturnRetry_Clear_ThenDay2PurchasePracticeAndSleep()
        {
            yield return Day2Ready();var timing=Find("Stage01SalmonTimingScaffold");
            Call(Field(timing,"audioClock"),"Stop");Call(Field(timing,"activeNoteTracker"),"ResolveRemainingNotesAsMissed");
            yield return new WaitForSecondsRealtime(.5f);Capture("stage2-failure-return");yield return WaitScene("FishShopDialogue");
            Assert.That(Beat,Is.EqualTo("Work"));Assert.That(((IList)Field(Current,"clearedStageIds")).Contains("STAGE_02_ROCKFISH"),Is.False);
            Assert.That(All("DayWorldNpc"),Is.Empty);yield return ContinueSaved("FishShopDialogue");
            Call(Find("StageStarterInteractable"),"Interact",Actor);yield return WaitScene("Stage02_Rockfish");
            timing=Find("Stage01SalmonTimingScaffold");var clock=Field(timing,"audioClock");
            Assert.That(Call(clock,"get_IsRunning"),Is.EqualTo(true));
            Capture("stage2-real-dsp-demo");
            var notes=(IList)Field(Field(timing,"notePatternProvider"),"runtimeNotes");var view=Find("Stage01ButcheryPresenter");
            int cursor=0,lastPhase=-1,pending=-1;float captureAt=0f,until=Time.realtimeSinceStartup+165f;
            double previous=-1d;
            while(!(bool)Call(timing,"get_IsResultShown") && Time.realtimeSinceStartup<until)
            {
                Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo("Stage02_Rockfish"),"The judged run must not fail or leave the stage.");
                double song=(double)Call(timing,"get_SongTimeSeconds");Assert.That(song,Is.GreaterThanOrEqualTo(previous));previous=song;
                if(cursor<notes.Count && song>=(double)Field(notes[cursor],"songTimeSeconds")-.010d)
                {
                    // Current DSP time enters the same timing/input owner as Space. No clock writes or direct clear calls.
                    Call(timing,"HandleTimingInput");cursor++;
                }
                int phase=Convert.ToInt32(Call(view,"get_VisiblePhase"));
                if(phase!=lastPhase){lastPhase=phase;pending=phase;captureAt=Time.realtimeSinceStartup+.70f;}
                if(pending>=0&&Time.realtimeSinceStartup>=captureAt){Capture("stage2-real-dsp-phase-"+pending);pending=-1;}
                yield return null;
            }
            Assert.That(Call(timing,"get_IsResultShown"),Is.EqualTo(true));Assert.That(cursor,Is.EqualTo(notes.Count));
            yield return new WaitForSecondsRealtime(.8f);Capture("stage2-real-dsp-clear");
            Assert.That(Call(view,"get_PlateComplete"),Is.EqualTo(true));Assert.That(Beat,Is.EqualTo("AfterWorkConversation"));
            Assert.That(((IList)Field(Current,"clearedStageIds")).Cast<string>().Count(s=>s=="STAGE_02_ROCKFISH"),Is.EqualTo(1));
            string once=JsonUtility.ToJson(Current);Call(timing,"AdvanceStageTimeline",129d,129d);Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(once));
            Call(Find("Stage01PlayableFlow"),"ReturnToShop");yield return WaitScene("FishShopDialogue");
            yield return Door("Street");yield return Talk("minjae");yield return BuyAndGoHome("DawSoftware");
            var oldStation=All("HomeEquipmentStation").Single(c=>Field(c,"equipmentId").ToString()=="SamplePackDrumKit");
            Assert.That(((GameObject)Field(oldStation,"equipmentVisual")).activeSelf,Is.True);
            yield return PlaceAndPractice("DawSoftware");Assert.That(Beat,Is.EqualTo("Complete"));Assert.That(Field(Progress,"nightsSlept"),Is.EqualTo(2));
            Assert.That(((IList)Field(Progress,"placedEquipment")).Count,Is.EqualTo(2));Capture("day2-complete-after-real-stage2");
        }

        void Capture(string name)
        {
            Directory.CreateDirectory("Logs/DayWorld/CameraEvidence");Camera camera=Camera.main;Assert.That(camera,Is.Not.Null);
            var overlays=Object.FindObjectsByType<Canvas>().Where(c=>c.isActiveAndEnabled&&c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
            var previousCameras=overlays.Select(c=>c.worldCamera).ToArray();var distances=overlays.Select(c=>c.planeDistance).ToArray();
            var target=new RenderTexture(1600,900,24);var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            try
            {
                camera.targetTexture=target;
                // Bootstrap's camera has a wider near clip than the first-person cameras.
                foreach(var c in overlays){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=Mathf.Max(.15f,camera.nearClipPlane+.05f);}
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1600,900),0,0);texture.Apply();
                File.WriteAllBytes("Logs/DayWorld/CameraEvidence/"+name+".png",texture.EncodeToPNG());Object.DestroyImmediate(texture);
                File.WriteAllText("Logs/DayWorld/CameraEvidence/"+name+".json",JsonUtility.ToJson(Current,true));
            }
            finally
            {
                for(int i=0;i<overlays.Length;i++){overlays[i].renderMode=RenderMode.ScreenSpaceOverlay;overlays[i].worldCamera=previousCameras[i];overlays[i].planeDistance=distances[i];}
                camera.targetTexture=oldTarget;RenderTexture.active=oldActive;Object.DestroyImmediate(target);Canvas.ForceUpdateCanvases();
            }
        }
    }
}
