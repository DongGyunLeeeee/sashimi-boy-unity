using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    // Synthetic note timestamps enter the actual controller input/tick path while
    // its real AudioClock is running. This is not physical input or music-sync evidence.
    public sealed class Stage03GameplayPlayModeTests
    {
        Component save, flow, controller;
        object originalSave, originalAuto, originalPath;
        static object Get(object target, string name) => RuntimeReflection.GetField(target, name);
        static void Set(object target, string name, object value) => RuntimeReflection.SetField(target, name, value);
        static object Call(object target, string method, params object[] args) => RuntimeReflection.Invoke(target, method, args);
        static T Value<T>(object target, string property) => (T)Call(target, "get_" + property);
        static Component Find(string name) => RuntimeReflection.FindActiveComponent("SashimiBoy." + name);
        object Current => Get(save, "current");
        object Progress => Get(Current, "dayWorld");
        object Run => Call(controller, "get_Run");
        object Clock => Get(controller, "clock");
        object Assembly => Get(controller, "assembly");
        IList Surfaces => (IList)Get(controller, "fillets");
        object PhaseChart => ((IList)Get(Get(controller, "chart"), "phases"))[Value<int>(Run, "Phase")];
        double[] Notes => (double[])Get(PhaseChart, "noteTimesMs");

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
            Call(save, "ConfigureValidationProfile", "stage3-gameplay-" + Guid.NewGuid().ToString("N"));
            Set(save, "autoSaveOnChange", true);
            Set(save, "current", RuntimeReflection.InvokeStatic("SashimiBoy.SaveData", "CreateNew"));
            Set(Progress, "active", true);
            Set(Progress, "day", 3);
            Set(Progress, "nightsSlept", 2);
            Set(Progress, "beat", Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"), "Work"));
            Set(Progress, "checkpointScene", "FishShopDialogue");
            Set(Progress, "checkpointSpawn", "StageReturn");
            Call(Current, "UnlockStage", "STAGE_03_SOLE");
            Call(flow, "SetBusy", false);
            Call(Find("GameFlowManager"), "RequestStage", "STAGE_03_SOLE");
            Assert.That(Application.CanStreamedLevelBeLoaded("Stage03_Flounder"), Is.True, "Generate the committed Stage3 scene and build entry before running this suite.");
            yield return SceneManager.LoadSceneAsync("Stage03_Flounder");
            yield return new WaitForSecondsRealtime(.4f);
            controller = Find("Stage03GameController");
            Assert.That(controller, Is.Not.Null);
            Assert.That(Value<bool>(controller, "Started"), Is.False);
        }

        [TearDown]
        public void TearDown()
        {
            if (controller != null) Call(Clock, "Stop");
            if (save != null)
            {
                Set(save, "current", originalSave);
                Set(save, "autoSaveOnChange", originalAuto);
                Set(save, "validationSavePath", originalPath);
            }
            if (flow != null) Call(flow, "SetBusy", false);
        }

        void StartRun()
        {
            Assert.That(Call(controller, "StartRun"), Is.EqualTo(true));
            Assert.That(Value<bool>(Clock, "IsRunning"), Is.True);
            Assert.That(Call(controller, "StartRun"), Is.EqualTo(false));
        }

        void HitCurrentPhase()
        {
            int needed = Value<int>(Run, "RequiredHits");
            double[] notes = Notes;
            for (int i = 0; i < needed; i++)
                Assert.That(Call(controller, "SubmitAt", notes[i]), Is.EqualTo(true), "phase " + Value<int>(Run, "Phase") + ", note " + i);
        }

        IEnumerator AdvanceCurrentPhase()
        {
            int previous = Value<int>(Run, "Phase");
            Call(Assembly, "AdvancePresentation", 1f);
            Call(controller, "TickAt", Notes.Last() + 1000d);
            if (previous == 2)
            {
                Assert.That(Value<bool>(Run, "WaitingForTurn"), Is.True);
                Assert.That(Value<bool>(Clock, "IsRunning"), Is.False);
                Assert.That(Call(controller, "SubmitAt", Notes[0]), Is.EqualTo(false));
                Assert.That(Call(Assembly, "TrySeparateFillet", 2), Is.EqualTo(false), "Back work is locked before the actual turnover completes.");
                Assert.That(Call(Assembly, "TryTurnOver"), Is.EqualTo(true));
                Assert.That(Call(Assembly, "TrySeparateFillet", 2), Is.EqualTo(false), "Back work stays locked during turnover.");
                Call(Assembly, "AdvancePresentation", 1f);
                yield return null; // The controller observes the presentation completion itself.
                Assert.That(Value<bool>(Run, "Turned"), Is.True);
                Assert.That(Value<bool>(Run, "WaitingForTurn"), Is.False);
                Assert.That(Value<bool>(Clock, "IsRunning"), Is.True);
            }
            Assert.That(Value<int>(Run, "Phase"), Is.EqualTo(previous + 1));
        }

        IEnumerator ReachPhase(int target)
        {
            while (Value<int>(Run, "Phase") < target)
            {
                HitCurrentPhase();
                yield return AdvanceCurrentPhase();
            }
        }

        static IEnumerator WaitScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(scene));
            yield return new WaitForSecondsRealtime(.4f);
        }

        static Bounds WorldBoundsOfMesh(MeshFilter filter)
        {
            Bounds local = filter.sharedMesh.bounds;
            Vector3 min = local.min, max = local.max;
            Bounds world = new Bounds(filter.transform.TransformPoint(min), Vector3.zero);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        world.Encapsulate(filter.transform.TransformPoint(new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z)));
            return world;
        }

        [UnityTest]
        public IEnumerator AuthoredScene_LoadsActualBodyMeshes_FourIndependentCutSurfaces_And24PlateSlots()
        {
            var states = (GameObject[])Get(Assembly, "bodyStates");
            Assert.That(states.Length, Is.EqualTo(6));
            foreach (var state in states)
            {
                Assert.That(state, Is.Not.Null);
                Assert.That(state.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 0), Is.True, state.name);
            }
            Assert.That(Surfaces.Count, Is.EqualTo(4));
            Assert.That(((IList)Get(controller, "plateSlots")).Count, Is.EqualTo(24));
            for (int i = 0; i < 4; i++)
            {
                var cache = Get(Surfaces[i], "cutCache");
                Assert.That(Call(cache, "get_IsValid"), Is.EqualTo(true));
                Assert.That(Get(cache, "modelRole"), Is.EqualTo(i < 2 ? "FrontFillet" : "BackFillet"));
                Assert.That(Value<int>(Surfaces[i], "CutCount"), Is.Zero);
            }
            Assert.That(ReferenceEquals(Surfaces[0], Surfaces[1]), Is.False);
            Assert.That(ReferenceEquals(Surfaces[2], Surfaces[3]), Is.False);
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.Zero);
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DuplicateEarlyPausedAndStoppedInputs_CannotAdvanceOrExpireTheRun()
        {
            double first = Notes[0];
            Assert.That(Call(controller, "SubmitAt", first), Is.EqualTo(false), "Start is explicit.");
            StartRun();
            Assert.That(Call(controller, "SubmitAt", first - 5000d), Is.EqualTo(false));
            Assert.That(Call(controller, "SubmitAt", first), Is.EqualTo(true));
            Assert.That(Call(controller, "SubmitAt", first), Is.EqualTo(false), "The same input timestamp does not consume the next note.");
            Assert.That(Value<int>(Run, "Hits"), Is.EqualTo(1));
            int cursor = Value<int>(controller, "NoteCursor");
            Call(controller, "TogglePause");
            Assert.That(Value<bool>(controller, "Paused"), Is.True);
            Assert.That(Call(controller, "SubmitAt", Notes[1]), Is.EqualTo(false));
            Call(controller, "TickAt", Notes.Last() + 1000d);
            Assert.That(Value<int>(controller, "NoteCursor"), Is.EqualTo(cursor));
            Assert.That(Value<bool>(Run, "Failed"), Is.False);
            Call(controller, "TogglePause");
            Assert.That(Value<bool>(Clock, "IsRunning"), Is.True);
            Call(Clock, "Stop");
            Assert.That(Call(controller, "SubmitAt", Notes[1]), Is.EqualTo(false));
            Call(controller, "TickAt", Notes.Last() + 1000d);
            Assert.That(Value<int>(controller, "NoteCursor"), Is.EqualTo(cursor));
            Assert.That(Value<bool>(Run, "Failed"), Is.False);
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator MissingPreparationPhase_FailsWithoutReward_AndRetryKeepsWholeFish()
        {
            StartRun();
            Call(controller, "TickAt", Notes.Last() + 1000d);
            Assert.That(Value<bool>(Run, "Failed"), Is.True);
            Assert.That(Value<int>(Run, "Phase"), Is.Zero);
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.Zero);
            Assert.That(Call(Assembly, "get_BodyState").ToString(), Is.EqualTo("WholeFish"));
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.Zero);
            Assert.That(((IList)Get(Current, "fishPlates")).Count, Is.Zero);
            Assert.That(Call(controller, "RetryPhase"), Is.EqualTo(true));
            Assert.That(Value<bool>(Clock, "IsRunning"), Is.True);
            Assert.That(Value<bool>(Run, "Failed"), Is.False);
            Assert.That(Value<int>(Run, "Hits"), Is.Zero);
            Assert.That(Call(controller, "RetryPhase"), Is.EqualTo(false));
            HitCurrentPhase();
            yield return AdvanceCurrentPhase();
            Assert.That(Call(Assembly, "get_BodyState").ToString(), Is.EqualTo("HeadRemovedFish"));
        }

        [UnityTest]
        public IEnumerator FirstFilletCut_StrikesTheAcceptedPlane_AndSupportsTheRemainingMesh()
        {
            StartRun();
            yield return ReachPhase(5);
            var surface = Surfaces[0];
            var cooking = Get(controller, "cooking");
            var filter = (MeshFilter)Get(surface, "meshFilter");
            var cache = Get(surface, "cutCache");
            var meshes = (Mesh[])Get(cache, "remainingMeshes");
            Assert.That(Value<int>(surface, "CutCount"), Is.Zero);
            Assert.That(filter.sharedMesh, Is.SameAs(meshes[0]));
            Vector3 acceptedPlane = Value<Vector3>(surface, "NextCutPosition");
            double note = Notes[0];
            Assert.That(Call(controller, "SubmitAt", note), Is.EqualTo(true));
            Assert.That(Value<int>(surface, "CutCount"), Is.EqualTo(1));
            Assert.That(filter.sharedMesh, Is.SameAs(meshes[1]), "The accepted cut removes real mesh geometry.");
            Assert.That(Value<bool>(cooking, "IsStriking"), Is.True);
            Vector3 strike = (Vector3)Get(cooking, "workPoint");
            Vector3 nextPlane = Value<Vector3>(surface, "NextCutPosition");
            Assert.That(Vector3.Distance(strike, acceptedPlane), Is.LessThan(.0001f), "The current stroke uses the plane accepted before the mesh advances.");
            Assert.That(Vector3.Distance(strike, nextPlane), Is.GreaterThan(.0001f), "Advancing the remaining mesh must not move an in-flight stroke to the next plane.");
            Assert.That(Get(cooking, "hasSupportPoint"), Is.EqualTo(true));
            Vector3 support = (Vector3)Get(cooking, "supportPoint");
            Bounds remaining = WorldBoundsOfMesh(filter);
            Assert.That(support.x, Is.InRange(remaining.min.x - .0001f, remaining.max.x + .0001f));
            Assert.That(support.z, Is.InRange(remaining.min.z - .0001f, remaining.max.z + .0001f));
            Assert.That(support.y, Is.EqualTo(remaining.max.y + .012f).Within(.0001f), "The support hand follows the top of the mesh still on the board.");
            Assert.That(Call(surface, "TryCut", 0), Is.EqualTo(false), "Repeating the accepted ordinal cannot remove another part.");
            Assert.That(Call(controller, "SubmitAt", note), Is.EqualTo(false));
            Assert.That(Value<int>(surface, "CutCount"), Is.EqualTo(1));
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FaultAfterHeadAlreadySeparated_RetryPreservesAcceptedCutAndAdvancesWithoutDoubleRemoval()
        {
            StartRun();
            HitCurrentPhase();
            Assert.That(Value<bool>(Run, "PhaseSatisfied"), Is.True);
            Assert.That(Call(Assembly, "get_BodyState").ToString(), Is.EqualTo("HeadRemovedFish"));
            int acceptedScore = Value<int>(controller, "Score");
            // Models the same failure path as an audio fault while the completed
            // preparation phase is waiting for the rest of its music segment.
            Call(controller, "Fail", "fixture audio recovery");
            Assert.That(Call(controller, "RetryPhase"), Is.EqualTo(true));
            Assert.That(Value<bool>(Run, "PhaseSatisfied"), Is.True, "A body part that already separated is not cut a second time.");
            Assert.That(Value<int>(controller, "Score"), Is.EqualTo(acceptedScore));
            Assert.That(Call(controller, "SubmitAt", Notes[0]), Is.EqualTo(false));
            yield return AdvanceCurrentPhase();
            Assert.That(Value<int>(Run, "Phase"), Is.EqualTo(1));
            Assert.That(Call(Assembly, "get_BodyState").ToString(), Is.EqualTo("HeadRemovedFish"));
            Assert.That(Call(controller, "SubmitAt", Notes[0]), Is.EqualTo(true));
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FailedSecondFillet_RemovesOnlyItsPartialSlices_AndPreservesFirstFilletAndScore()
        {
            StartRun();
            yield return ReachPhase(6);
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(6));
            int completedScore = Value<int>(controller, "Score");
            double[] notes = Notes;
            for (int i = 0; i < 3; i++) Assert.That(Call(controller, "SubmitAt", notes[i]), Is.EqualTo(true));
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(9));
            Call(controller, "TickAt", notes.Last() + 1000d);
            Assert.That(Value<bool>(Run, "Failed"), Is.True);
            Assert.That(Value<bool>(controller, "ResultShown"), Is.False);
            Assert.That(Call(controller, "RetryPhase"), Is.EqualTo(true));
            yield return null;
            Assert.That(Value<int>(Run, "Phase"), Is.EqualTo(6));
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(6));
            Assert.That(Value<int[]>(Run, "Slices"), Is.EqualTo(new[] { 6, 0, 0, 0 }));
            Assert.That(Value<int>(Surfaces[0], "CutCount"), Is.EqualTo(6));
            Assert.That(Value<int>(Surfaces[1], "CutCount"), Is.Zero);
            Assert.That(Value<int>(controller, "Score"), Is.EqualTo(completedScore));
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.Zero);
            HitCurrentPhase();
            yield return AdvanceCurrentPhase();
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(12));
        }

        [UnityTest]
        public IEnumerator ActualJudgedPath_Completes24RealSlices_AndRecordsOnePlateOnlyOnce()
        {
            var cooking=Get(controller,"cooking");
            var body=(Component)Get(cooking,"body");
            var bodyRenderers=body.GetComponentsInChildren<Renderer>(true);
            var rendererStates=bodyRenderers.Select(r=>r.enabled).ToArray();
            var knife=(Transform)Get(cooking,"knifeRoot");
            StartRun();
            yield return ReachPhase(8);
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(18));
            double[] finalNotes = Notes;
            for (int i = 0; i < 5; i++) Assert.That(Call(controller, "SubmitAt", finalNotes[i]), Is.EqualTo(true));
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(23));
            Assert.That(Value<bool>(Run, "Completed"), Is.False);
            Assert.That(Value<bool>(controller, "ResultShown"), Is.False);
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.Zero);
            Assert.That(Call(controller, "SubmitAt", finalNotes[5]), Is.EqualTo(true));
            Assert.That(Call(controller, "SubmitAt", finalNotes[5]), Is.EqualTo(false));
            yield return AdvanceCurrentPhase();
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(24));
            Assert.That(Value<int>(Run, "TotalSlices"), Is.EqualTo(24));
            foreach (var surface in Surfaces) Assert.That(Value<bool>(surface, "IsEmpty"), Is.True);
            yield return new WaitForSecondsRealtime(2.25f);
            Assert.That(Value<bool>(controller, "ResultShown"), Is.True);
            Assert.That(Value<bool>(controller, "ClearRecorded"), Is.True);
            var camera=(Camera)Get(cooking,"gameCamera");
            var slots=(Transform[])Get(controller,"plateSlots");
            foreach(var slot in slots)
            {
                Vector3 viewport=camera.WorldToViewportPoint(slot.position);
                Assert.That(viewport.z,Is.GreaterThan(0));
                Assert.That(viewport.x,Is.InRange(.03f,.97f),"Every serving slot stays inside the completion shot.");
                Assert.That(viewport.y,Is.InRange(.32f,.78f),"The completed dish stays between the header and result actions.");
            }
            Assert.That(bodyRenderers.All(r=>!r.enabled),Is.True,"Hands must not obscure the served dish.");
            Assert.That(knife.gameObject.activeSelf,Is.False);
            Assert.That(((IList)Get(Current, "clearedStageIds")).Cast<string>().Count(id => id == "STAGE_03_SOLE"), Is.EqualTo(1));
            Assert.That(Get(Progress, "beat").ToString(), Is.EqualTo("AfterWorkConversation"));
            var sole = Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.FishType"), "Sole");
            Assert.That(Call(Current, "GetPlates", sole), Is.EqualTo(1));
            string once = JsonUtility.ToJson(Current);
            Call(controller, "ShowResult");
            Call(controller, "TickAt", 230000d);
            Assert.That(Call(controller, "SubmitAt", 230000d), Is.EqualTo(false));
            Assert.That(JsonUtility.ToJson(Current), Is.EqualTo(once));
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(24));
            Call(controller,"ResetRun");
            Assert.That(bodyRenderers.Select(r=>r.enabled).ToArray(),Is.EqualTo(rendererStates),"A fresh run restores the authored connected body.");
            Assert.That(knife.gameObject.activeSelf,Is.True);
        }

        [UnityTest]
        public IEnumerator AbortDuringCutting_ReturnsToShopWithoutClearOrReward()
        {
            StartRun();
            yield return ReachPhase(5);
            Assert.That(Call(controller, "SubmitAt", Notes[0]), Is.EqualTo(true));
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(1));
            Call(controller, "ReturnToShop");
            yield return WaitScene("FishShopDialogue");
            Assert.That(Get(Progress, "beat").ToString(), Is.EqualTo("Work"));
            Assert.That(((IList)Get(Current, "clearedStageIds")).Count, Is.Zero);
            Assert.That(((IList)Get(Current, "fishPlates")).Count, Is.Zero);
            Assert.That(Get(Progress, "checkpointScene"), Is.EqualTo("FishShopDialogue"));
        }
    }
}
