using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    // Synthetic timestamps exercise the actual controller and running AudioClock.
    // They do not constitute physical-input, listening, or visual approval.
    public sealed class Stage03GameplayPlayModeTests
    {
        Component save,flow,controller;
        object originalSave,originalAuto,originalPath;
        static object Get(object target,string name)=>RuntimeReflection.GetField(target,name);
        static void Set(object target,string name,object value)=>RuntimeReflection.SetField(target,name,value);
        static object Call(object target,string method,params object[] args)=>RuntimeReflection.Invoke(target,method,args);
        static T Value<T>(object target,string property)=>(T)Call(target,"get_"+property);
        static Component Find(string name)=>RuntimeReflection.FindActiveComponent("SashimiBoy."+name);
        object Current=>Get(save,"current");
        object Progress=>Get(Current,"dayWorld");
        object Run=>Call(controller,"get_Run");
        object Clock=>Get(controller,"clock");
        object Assembly=>Get(controller,"assembly");
        IList Surfaces=>(IList)Get(controller,"fillets");
        object PhaseChart=>((IList)Get(Get(controller,"chart"),"phases"))[Value<int>(Run,"Phase")];
        double[] Notes=>(double[])Get(PhaseChart,"noteTimesMs");
        double Deadline=>(double)Get(PhaseChart,"endTimeMs");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            yield return null;
            save=Find("SaveManager");flow=Find("DayWorldFlow");
            Assert.That(save,Is.Not.Null);Assert.That(flow,Is.Not.Null);
            originalSave=Current;originalAuto=Get(save,"autoSaveOnChange");originalPath=Get(save,"validationSavePath");
            Call(save,"ConfigureValidationProfile","stage3-gameplay-"+Guid.NewGuid().ToString("N"));
            Set(save,"autoSaveOnChange",true);
            Set(save,"current",RuntimeReflection.InvokeStatic("SashimiBoy.SaveData","CreateNew"));
            Set(Progress,"active",true);Set(Progress,"day",3);Set(Progress,"nightsSlept",2);
            Set(Progress,"beat",Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"),"Work"));
            Set(Progress,"checkpointScene","FishShopDialogue");Set(Progress,"checkpointSpawn","StageReturn");
            Call(Current,"UnlockStage","STAGE_03_SOLE");Call(flow,"SetBusy",false);
            Call(Find("GameFlowManager"),"RequestStage","STAGE_03_SOLE");
            Assert.That(Application.CanStreamedLevelBeLoaded("Stage03_Flounder"),Is.True);
            yield return SceneManager.LoadSceneAsync("Stage03_Flounder");
            yield return new WaitForSecondsRealtime(.4f);
            controller=Find("Stage03GameController");Assert.That(controller,Is.Not.Null);
            Assert.That(Value<bool>(controller,"Started"),Is.True,"Normal scene entry starts like Stage1/2.");
            Call(controller,"ResetRun");Assert.That(Value<bool>(controller,"Started"),Is.False);
        }

        [TearDown]
        public void TearDown()
        {
            if(controller!=null)Call(Clock,"Stop");
            if(save!=null){Set(save,"current",originalSave);Set(save,"autoSaveOnChange",originalAuto);Set(save,"validationSavePath",originalPath);}
            if(flow!=null)Call(flow,"SetBusy",false);
        }

        void StartRun()
        {
            Assert.That(Call(controller,"StartRun"),Is.EqualTo(true));
            Assert.That(Value<bool>(Clock,"IsRunning"),Is.True);
            Assert.That(Call(controller,"StartRun"),Is.EqualTo(false));
        }

        void HitCurrentPhase()
        {
            double[] notes=Notes;
            for(int i=0;i<notes.Length;i++)
            {
                Assert.That(Call(controller,"SubmitAt",notes[i]),Is.EqualTo(true),"phase "+Value<int>(Run,"Phase")+", note "+i);
                Call(controller,"AdvancePresentation",.30f);
            }
            FinishVisuals();
        }

        void HitOnlyQuota()
        {
            int required=Value<int>(Run,"RequiredHits");double[] notes=Notes;
            for(int i=0;i<required;i++)
            {
                Assert.That(Call(controller,"SubmitAt",notes[i]),Is.EqualTo(true));
                Call(controller,"AdvancePresentation",.30f);
            }
            FinishVisuals();
        }

        void FinishVisuals()
        {
            Call(controller,"AdvancePresentation",60f);
            Assert.That(Value<int>(controller,"PendingVisualCuts"),Is.Zero);
            Call(Assembly,"AdvancePresentation",1f);
        }

        IEnumerator AdvanceCurrentPhase()
        {
            int previous=Value<int>(Run,"Phase");double deadline=Deadline;
            FinishVisuals();Call(controller,"TickAt",deadline+1d);
            Assert.That(Value<int>(Run,"Phase"),Is.EqualTo(previous+1));
            Assert.That(Value<bool>(Run,"WaitingForTurn"),Is.False);
            Assert.That(Value<bool>(Clock,"IsRunning"),Is.True,"A successful musical boundary never pauses playback.");
            yield return null;
        }

        IEnumerator ReachPhase(int target)
        {
            while(Value<int>(Run,"Phase")<target){HitCurrentPhase();yield return AdvanceCurrentPhase();}
        }

        static IEnumerator WaitScene(string scene)
        {
            float deadline=Time.realtimeSinceStartup+25f;
            while(SceneManager.GetActiveScene().name!=scene&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo(scene));yield return new WaitForSecondsRealtime(.4f);
        }

        static Bounds WorldBoundsOfMesh(MeshFilter filter)
        {
            Bounds local=filter.sharedMesh.bounds;Vector3 min=local.min,max=local.max;
            Bounds world=new Bounds(filter.transform.TransformPoint(min),Vector3.zero);
            for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
                world.Encapsulate(filter.transform.TransformPoint(new Vector3(x==0?min.x:max.x,y==0?min.y:max.y,z==0?min.z:max.z)));
            return world;
        }

        [UnityTest]
        public IEnumerator AuthoredScene_LoadsActualBodyMeshes_TwoTwelveSliceSurfaces_And24PlateSlots()
        {
            var states=(GameObject[])Get(Assembly,"bodyStates");Assert.That(states.Length,Is.EqualTo(6));
            foreach(var state in states)
            {
                Assert.That(state,Is.Not.Null);
                Assert.That(state.GetComponentsInChildren<MeshFilter>(true).Any(filter=>filter.sharedMesh!=null&&filter.sharedMesh.vertexCount>0),Is.True,state.name);
            }
            Assert.That(Surfaces.Count,Is.EqualTo(2));Assert.That(((IList)Get(controller,"plateSlots")).Count,Is.EqualTo(24));
            for(int i=0;i<2;i++)
            {
                var cache=Get(Surfaces[i],"cutCache");Assert.That(Call(cache,"get_IsValid"),Is.EqualTo(true));
                Assert.That(Get(cache,"modelRole"),Is.EqualTo(i==0?"FrontFillet":"BackFillet"));
                Assert.That(((Mesh[])Get(cache,"remainingMeshes")).Length,Is.EqualTo(13));
                Assert.That(Value<int>(Surfaces[i],"CutCount"),Is.Zero);
            }
            Assert.That(ReferenceEquals(Surfaces[0],Surfaces[1]),Is.False);
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.Zero);
            Assert.That(((IList)Get(Current,"clearedStageIds")).Count,Is.Zero);yield return null;
        }

        [UnityTest]
        public IEnumerator DuplicateEarlyPausedAndStoppedInputs_CannotAdvanceOrExpireTheRun()
        {
            double first=Notes[0];Assert.That(Call(controller,"SubmitAt",first),Is.EqualTo(false));StartRun();
            Assert.That(Call(controller,"SubmitAt",first-5000d),Is.EqualTo(false));
            Assert.That(Call(controller,"SubmitAt",first),Is.EqualTo(true));
            Assert.That(Call(controller,"SubmitAt",first),Is.EqualTo(false));
            Assert.That(Value<int>(Run,"Hits"),Is.EqualTo(1));int cursor=Value<int>(controller,"NoteCursor");
            Call(controller,"TogglePause");Assert.That(Value<bool>(controller,"Paused"),Is.True);
            var cooking=Get(controller,"cooking");float frozen=Value<float>(cooking,"MotionProgress01");
            Call(controller,"AdvancePresentation",5f);Assert.That(Value<float>(cooking,"MotionProgress01"),Is.EqualTo(frozen));
            Assert.That(Call(controller,"SubmitAt",Notes[1]),Is.EqualTo(false));Call(controller,"TickAt",Deadline+1d);
            Assert.That(Value<int>(controller,"NoteCursor"),Is.EqualTo(cursor));Assert.That(Value<bool>(Run,"Failed"),Is.False);
            Call(controller,"TogglePause");Assert.That(Value<bool>(Clock,"IsRunning"),Is.True);Call(Clock,"Stop");
            Assert.That(Call(controller,"SubmitAt",Notes[1]),Is.EqualTo(false));Call(controller,"TickAt",Deadline+1d);
            Assert.That(Value<int>(controller,"NoteCursor"),Is.EqualTo(cursor));Assert.That(Value<bool>(Run,"Failed"),Is.False);
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.Zero);yield return null;
        }

        [UnityTest]
        public IEnumerator QuotaReachedEarly_AllLaterNotesStillScoreAndMiss_WithoutWaitingOrPausing()
        {
            StartRun();int required=Value<int>(Run,"RequiredHits");double[] notes=Notes;double deadline=Deadline;
            HitOnlyQuota();Assert.That(Value<bool>(Run,"PhaseSatisfied"),Is.True);
            Assert.That(Value<int>(Run,"Phase"),Is.Zero);
            Assert.That(Value<double>(controller,"NextNoteMs"),Is.EqualTo(notes[required]));
            int score=Value<int>(controller,"Score");
            Assert.That(Call(controller,"SubmitAt",notes[required]),Is.EqualTo(true));
            Assert.That(Value<int>(controller,"Score"),Is.GreaterThan(score));
            FinishVisuals();Call(controller,"TickAt",deadline+1d);
            Assert.That(Value<int>(Run,"Phase"),Is.EqualTo(1));
            Assert.That(Value<int>(controller,"MissCount"),Is.EqualTo(notes.Length-required-1));
            Assert.That(Value<bool>(Clock,"IsRunning"),Is.True);yield return null;
        }

        [UnityTest]
        public IEnumerator MissingPreparationPhase_FailsWithoutReward_AndRetryStartsWholeSongAndFish()
        {
            StartRun();Call(controller,"TickAt",Deadline+1d);
            Assert.That(Value<bool>(Run,"Failed"),Is.True);Assert.That(Value<int>(Run,"Phase"),Is.Zero);
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.Zero);Assert.That(Call(Assembly,"get_BodyState").ToString(),Is.EqualTo("WholeFish"));
            Assert.That(((IList)Get(Current,"clearedStageIds")).Count,Is.Zero);Assert.That(((IList)Get(Current,"fishPlates")).Count,Is.Zero);
            Assert.That(Call(controller,"RetryPhase"),Is.EqualTo(true));Assert.That(Value<bool>(Clock,"IsRunning"),Is.True);
            Assert.That(Value<bool>(Run,"Failed"),Is.False);Assert.That(Value<int>(Run,"Hits"),Is.Zero);
            Assert.That(Value<double>(Clock,"SongTimeMs"),Is.LessThan(500d));
            Assert.That(Call(controller,"RetryPhase"),Is.EqualTo(false));HitCurrentPhase();yield return AdvanceCurrentPhase();
            Assert.That(Call(Assembly,"get_BodyState").ToString(),Is.EqualTo("HeadRemovedFish"));
        }

        [UnityTest]
        public IEnumerator TurnoverNote_KeepsTheSongRunning_AndBackWorkLockedUntilThePhysicalFlip()
        {
            StartRun();yield return ReachPhase(3);double deadline=Deadline;
            Assert.That(Call(Assembly,"TryRemoveSkin",1),Is.EqualTo(false));
            Assert.That(Call(controller,"SubmitAt",Notes[0]),Is.EqualTo(true));
            Assert.That(Value<bool>(Clock,"IsRunning"),Is.True);Call(controller,"AdvancePresentation",.30f);
            Assert.That(Value<bool>(Assembly,"IsTurningOver"),Is.True);
            Assert.That(Call(Assembly,"TryRemoveSkin",1),Is.EqualTo(false));
            Assert.That(Value<bool>(Clock,"IsRunning"),Is.True);
            FinishVisuals();Call(controller,"TickAt",deadline+1d);
            Assert.That(Value<int>(Run,"Phase"),Is.EqualTo(4));Assert.That(Value<bool>(Run,"Turned"),Is.True);
            Assert.That(Value<bool>(Assembly,"IsBackFacing"),Is.True);Assert.That(Value<bool>(Clock,"IsRunning"),Is.True);
        }

        [UnityTest]
        public IEnumerator MissedTurnover_FailsWholeStage_WithoutAutomaticFlipOrReward()
        {
            StartRun();yield return ReachPhase(3);Call(controller,"TickAt",Deadline+1d);
            Assert.That(Value<bool>(Run,"Failed"),Is.True);Assert.That(Value<bool>(Assembly,"IsBackFacing"),Is.False);
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.Zero);Assert.That(((IList)Get(Current,"clearedStageIds")).Count,Is.Zero);
            Assert.That(Call(controller,"RetryPhase"),Is.EqualTo(true));Assert.That(Value<int>(Run,"Phase"),Is.Zero);
            Assert.That(Value<bool>(Run,"Turned"),Is.False);Assert.That(Call(Assembly,"get_BodyState").ToString(),Is.EqualTo("WholeFish"));
        }

        [UnityTest]
        public IEnumerator FirstRealSlice_CommitsAtKnifeContact_AndSupportsTheRemainingMesh()
        {
            StartRun();yield return ReachPhase(6);
            var surface=Surfaces[0];var cooking=Get(controller,"cooking");var filter=(MeshFilter)Get(surface,"meshFilter");
            var meshes=(Mesh[])Get(Get(surface,"cutCache"),"remainingMeshes");
            int firstBoundary=(Value<int>(Run,"RequiredHits")+11)/12;double[] notes=Notes;
            for(int i=0;i<firstBoundary-1;i++)
            {Assert.That(Call(controller,"SubmitAt",notes[i]),Is.EqualTo(true));Call(controller,"AdvancePresentation",.30f);}
            Assert.That(Value<int>(surface,"CutCount"),Is.Zero,"Earlier successful beats work the same real cut; they do not fabricate slices.");
            Vector3 acceptedPlane=Value<Vector3>(surface,"NextCutPosition");double note=notes[firstBoundary-1];
            Assert.That(Call(controller,"SubmitAt",note),Is.EqualTo(true));
            Assert.That(Value<int>(surface,"CutCount"),Is.Zero);Assert.That(filter.sharedMesh,Is.SameAs(meshes[0]));
            Call(controller,"AdvancePresentation",.04f);Assert.That(Value<int>(surface,"CutCount"),Is.Zero);
            Call(controller,"TogglePause");var pausedContact=(Action)Get(cooking,"contactAction");
            pausedContact();Call(controller,"AdvancePresentation",3f);Assert.That(Value<int>(surface,"CutCount"),Is.Zero);
            Call(controller,"TogglePause");Call(controller,"AdvancePresentation",.06f);
            Assert.That(Value<int>(surface,"CutCount"),Is.EqualTo(1));Assert.That(filter.sharedMesh,Is.SameAs(meshes[1]));
            Assert.That(Value<bool>(cooking,"IsStriking"),Is.True);
            Vector3 strike=Value<Vector3>(cooking,"CurrentContactPoint");
            Assert.That(Vector3.Distance(strike,acceptedPlane),Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(strike,Value<Vector3>(surface,"NextCutPosition")),Is.GreaterThan(.0001f));
            Assert.That(Get(cooking,"hasSupportPoint"),Is.EqualTo(true));Vector3 support=(Vector3)Get(cooking,"supportPoint");
            Bounds remaining=WorldBoundsOfMesh(filter);
            Assert.That(support.x,Is.InRange(remaining.min.x-.0001f,remaining.max.x+.0001f));
            Assert.That(support.z,Is.InRange(remaining.min.z-.0001f,remaining.max.z+.0001f));
            Assert.That(support.y,Is.EqualTo(remaining.max.y+.012f).Within(.0001f));
            Assert.That(Call(surface,"TryCut",0),Is.EqualTo(false));Assert.That(Call(controller,"SubmitAt",note),Is.EqualTo(false));
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FaultAfterHeadSeparated_RetryRestoresWholeFishAndZeroScore()
        {
            StartRun();HitCurrentPhase();Assert.That(Call(Assembly,"get_BodyState").ToString(),Is.EqualTo("HeadRemovedFish"));
            Assert.That(Value<int>(controller,"Score"),Is.GreaterThan(0));Call(controller,"Fail","fixture audio failure");
            Assert.That(Call(controller,"RetryPhase"),Is.EqualTo(true));Assert.That(Value<int>(controller,"Score"),Is.Zero);
            Assert.That(Value<int>(Run,"Phase"),Is.Zero);Assert.That(Value<bool>(Run,"PhaseSatisfied"),Is.False);
            Assert.That(Call(Assembly,"get_BodyState").ToString(),Is.EqualTo("WholeFish"));
            Assert.That(Call(controller,"SubmitAt",Notes[0]),Is.EqualTo(true));yield return null;
        }

        [UnityTest]
        public IEnumerator BelowQuotaOnSecondFillet_FailsAndRetryRemovesEveryPriorSliceAndReward()
        {
            StartRun();yield return ReachPhase(7);Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(12));
            double[] notes=Notes;int partial=Value<int>(Run,"RequiredHits")/3;
            for(int i=0;i<partial;i++){Assert.That(Call(controller,"SubmitAt",notes[i]),Is.EqualTo(true));Call(controller,"AdvancePresentation",.30f);}
            FinishVisuals();Assert.That(Value<int>(controller,"SpawnedSlices"),Is.InRange(13,23));
            Call(controller,"TickAt",Deadline+1d);Assert.That(Value<bool>(Run,"Failed"),Is.True);
            Assert.That(Value<bool>(controller,"ResultShown"),Is.False);Assert.That(Call(controller,"RetryPhase"),Is.EqualTo(true));
            yield return null;Assert.That(Value<int>(Run,"Phase"),Is.Zero);
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.Zero);Assert.That(Value<int[]>(Run,"Slices"),Is.EqualTo(new[]{0,0}));
            foreach(var surface in Surfaces)Assert.That(Value<int>(surface,"CutCount"),Is.Zero);
            Assert.That(Value<int>(controller,"Score"),Is.Zero);Assert.That(((IList)Get(Current,"clearedStageIds")).Count,Is.Zero);
            Assert.That(((IList)Get(Current,"fishPlates")).Count,Is.Zero);
        }

        [UnityTest]
        public IEnumerator QuotaOnlyActualJudgedPath_Completes24RealSlices_AndRecordsOnePlateOnlyOnce()
        {
            var cooking=Get(controller,"cooking");var body=(Component)Get(cooking,"body");
            var bodyRenderers=body.GetComponentsInChildren<Renderer>(true);var rendererStates=bodyRenderers.Select(renderer=>renderer.enabled).ToArray();
            var knife=(Transform)Get(cooking,"knifeRoot");StartRun();
            while(Value<int>(Run,"Phase")<7){HitOnlyQuota();yield return AdvanceCurrentPhase();}
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(12));
            double[] finalNotes=Notes;double deadline=Deadline;int required=Value<int>(Run,"RequiredHits");
            for(int i=0;i<required-1;i++){Assert.That(Call(controller,"SubmitAt",finalNotes[i]),Is.EqualTo(true));Call(controller,"AdvancePresentation",.30f);}
            FinishVisuals();Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(23));
            Assert.That(Value<bool>(Run,"Completed"),Is.False);Assert.That(((IList)Get(Current,"clearedStageIds")).Count,Is.Zero);
            Assert.That(Call(controller,"SubmitAt",finalNotes[required-1]),Is.EqualTo(true));
            Assert.That(Call(controller,"SubmitAt",finalNotes[required-1]),Is.EqualTo(false));
            Call(controller,"TickAt",deadline+1d);Assert.That(Value<bool>(Run,"Completed"),Is.False,"The last accepted work hit cannot complete a plate before its contact.");
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(23));
            FinishVisuals();Call(controller,"TickAt",deadline+1d);
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(24));Assert.That(Value<int>(Run,"TotalSlices"),Is.EqualTo(24));
            foreach(var surface in Surfaces)Assert.That(Value<bool>(surface,"IsEmpty"),Is.True);
            yield return new WaitForSecondsRealtime(2.25f);
            Assert.That(Value<bool>(controller,"ResultShown"),Is.True);Assert.That(Value<bool>(controller,"ClearRecorded"),Is.True);
            var camera=(Camera)Get(cooking,"gameCamera");var slots=(Transform[])Get(controller,"plateSlots");
            var resultRect=((GameObject)Get(controller,"resultPanel")).GetComponent<RectTransform>();var corners=new Vector3[4];resultRect.GetWorldCorners(corners);
            Rect card=Rect.MinMaxRect(corners[0].x-16,corners[0].y-16,corners[2].x+16,corners[2].y+16);
            foreach(var slot in slots)
            {
                Vector3 viewport=camera.WorldToViewportPoint(slot.position);Assert.That(viewport.z,Is.GreaterThan(0));
                Assert.That(viewport.x,Is.InRange(.03f,.97f));Assert.That(viewport.y,Is.InRange(.03f,.97f));
                Assert.That(card.Contains(camera.WorldToScreenPoint(slot.position)),Is.False,"The result card must leave every serving slot visible.");
            }
            Assert.That(bodyRenderers.All(renderer=>!renderer.enabled),Is.True);Assert.That(knife.gameObject.activeSelf,Is.False);
            Assert.That(((IList)Get(Current,"clearedStageIds")).Cast<string>().Count(id=>id=="STAGE_03_SOLE"),Is.EqualTo(1));
            Assert.That(Get(Progress,"beat").ToString(),Is.EqualTo("AfterWorkConversation"));
            var sole=Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.FishType"),"Sole");Assert.That(Call(Current,"GetPlates",sole),Is.EqualTo(1));
            string once=JsonUtility.ToJson(Current);Call(controller,"ShowResult");Call(controller,"TickAt",230000d);
            Assert.That(Call(controller,"SubmitAt",230000d),Is.EqualTo(false));Assert.That(JsonUtility.ToJson(Current),Is.EqualTo(once));
            Call(controller,"ResetRun");Assert.That(bodyRenderers.Select(renderer=>renderer.enabled).ToArray(),Is.EqualTo(rendererStates));Assert.That(knife.gameObject.activeSelf,Is.True);
        }

        [UnityTest]
        public IEnumerator EveryExtraSlicingNote_ScoresWithoutGeneratingAThirteenthPiece()
        {
            StartRun();yield return ReachPhase(6);int previousScore=Value<int>(controller,"Score"),count=Notes.Length;
            HitCurrentPhase();Assert.That(Value<int>(Run,"Hits"),Is.EqualTo(count));
            Assert.That(Value<int>(controller,"Score"),Is.EqualTo(previousScore+count*1000));
            Assert.That(Value<int>(Surfaces[0],"CutCount"),Is.EqualTo(12));Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(12));
            Assert.That(Value<bool>(Clock,"IsRunning"),Is.True);Assert.That(Value<int>(Run,"Phase"),Is.EqualTo(6));
            yield return AdvanceCurrentPhase();Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(12));
        }

        [UnityTest]
        public IEnumerator AbortDuringCutting_ReturnsToShopWithoutClearOrReward()
        {
            StartRun();yield return ReachPhase(6);Assert.That(Call(controller,"SubmitAt",Notes[0]),Is.EqualTo(true));
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.Zero);var cooking=Get(controller,"cooking");var lateContact=(Action)Get(cooking,"contactAction");
            Call(controller,"ReturnToShop");lateContact();yield return WaitScene("FishShopDialogue");
            Assert.That(Get(Progress,"beat").ToString(),Is.EqualTo("Work"));Assert.That(((IList)Get(Current,"clearedStageIds")).Count,Is.Zero);
            Assert.That(((IList)Get(Current,"fishPlates")).Count,Is.Zero);Assert.That(Get(Progress,"checkpointScene"),Is.EqualTo("FishShopDialogue"));
        }

        [UnityTest]
        public IEnumerator QueuedNotes_JudgeImmediately_ButTwelveActualSlicesCommitOnlyAtOrderedContacts()
        {
            StartRun();yield return ReachPhase(6);var surface=Surfaces[0];double[] notes=Notes;
            int required=Value<int>(Run,"RequiredHits");
            for(int i=0;i<required;i++)Assert.That(Call(controller,"SubmitAt",notes[i]),Is.EqualTo(true));
            Assert.That(Value<int>(Run,"Hits"),Is.EqualTo(required));Assert.That(Value<int>(controller,"PendingVisualCuts"),Is.EqualTo(required));
            Assert.That(Value<int>(controller,"SpawnedSlices"),Is.Zero);Assert.That(Value<int>(surface,"CutCount"),Is.Zero);
            Call(controller,"TickAt",Deadline+1d);Assert.That(Value<int>(Run,"Phase"),Is.EqualTo(6));
            int firstBoundary=(required+11)/12;
            float duration=(float)RuntimeReflection.InvokeStatic("SashimiBoy.Stage03CookingView","DurationForPhase",6);
            Call(controller,"AdvancePresentation",duration*((firstBoundary-1)+.45f)+.001f);
            Assert.That(Value<int>(surface,"CutCount"),Is.EqualTo(1));Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(1));
            FinishVisuals();Assert.That(Value<int>(surface,"CutCount"),Is.EqualTo(12));Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(12));
            Call(controller,"AdvancePresentation",5f);Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(12));
        }

        [UnityTest]
        public IEnumerator ResetAndRetryBeforeContact_CancelOldCallbacksAndRequireHeadWorkAgain()
        {
            StartRun();double[] notes=Notes;int required=Value<int>(Run,"RequiredHits");
            for(int i=0;i<required-1;i++){Assert.That(Call(controller,"SubmitAt",notes[i]),Is.EqualTo(true));Call(controller,"AdvancePresentation",.30f);}
            Assert.That(Call(controller,"SubmitAt",notes[required-1]),Is.EqualTo(true));
            var cooking=Get(controller,"cooking");var stale=(Action)Get(cooking,"contactAction");
            Assert.That(Value<bool>(Run,"PhaseSatisfied"),Is.True);Assert.That(Call(Assembly,"get_BodyState").ToString(),Is.EqualTo("WholeFish"));
            Call(controller,"Fail","fixture interrupted pre-contact head stroke");Assert.That(Call(controller,"RetryPhase"),Is.EqualTo(true));
            Assert.That(Value<int>(Run,"Hits"),Is.Zero);stale();Call(controller,"AdvancePresentation",5f);
            Assert.That(Call(Assembly,"get_BodyState").ToString(),Is.EqualTo("WholeFish"));Assert.That(Value<int>(controller,"PendingVisualCuts"),Is.Zero);
            Assert.That(Call(controller,"SubmitAt",Notes[0]),Is.EqualTo(true));stale=(Action)Get(cooking,"contactAction");
            Call(controller,"ResetRun");stale();Call(cooking,"AdvancePresentation",5f);
            Assert.That(Value<int>(Run,"Hits"),Is.Zero);Assert.That(Value<int>(controller,"SpawnedSlices"),Is.Zero);
            Assert.That(Call(Assembly,"get_BodyState").ToString(),Is.EqualTo("WholeFish"));Assert.That(Value<bool>(cooking,"IsStriking"),Is.False);yield return null;
        }
    }
}
