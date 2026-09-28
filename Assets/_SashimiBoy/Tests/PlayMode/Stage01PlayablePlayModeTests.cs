using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SashimiBoy.Semantics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SashimiBoy.Tests
{
    // Judged-input integration tests; these are not evidence of physical keyboard play.
    public sealed class Stage01PlayablePlayModeTests
    {
        private Component timing, view, save;
        private object originalSave;
        private bool originalAutoSave;
        private IList notes;
        private PhasePerformanceTracker Performance => (PhasePerformanceTracker)RuntimeReflection.Invoke(timing, "get_PhasePerformance");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Stage01_Salmon", LoadSceneMode.Single);
            yield return null;
            // Match the 1280x720 validation player and 1600x900 camera evidence.
            // The batch Editor Game view otherwise defaults to 4:3 even when rendering a 16:9 capture.
            Camera.main.aspect = 16f/9f;
            timing = RuntimeReflection.FindActiveComponent("SashimiBoy.Stage01SalmonTimingScaffold");
            view = RuntimeReflection.FindActiveComponent("SashimiBoy.Stage01ButcheryPresenter");
            Assert.That(view, Is.Not.Null, "Playable authoring must be applied to the actual Stage01 scene.");
            Assert.That(((SemanticValidationResult)RuntimeReflection.Invoke(timing, "get_SemanticValidation")).IsValid, Is.True);
            RuntimeReflection.Invoke(RuntimeReflection.GetField(timing, "audioClock"), "Stop");
            var provider = RuntimeReflection.GetField(timing, "notePatternProvider");
            notes = (IList)RuntimeReflection.GetField(provider, "runtimeNotes");
            save = RuntimeReflection.FindActiveComponent("SashimiBoy.SaveManager");
            originalSave = RuntimeReflection.GetField(save, "current");
            originalAutoSave = (bool)RuntimeReflection.GetField(save, "autoSaveOnChange");
            RuntimeReflection.SetField(save, "autoSaveOnChange", false);
            RuntimeReflection.SetField(save, "current", RuntimeReflection.InvokeStatic("SashimiBoy.SaveData", "CreateNew"));
        }

        [TearDown]
        public void TearDown()
        {
            if(Camera.main != null) Camera.main.ResetAspect();
            if (timing != null) RuntimeReflection.Invoke(RuntimeReflection.GetField(timing, "audioClock"), "Stop");
            var flow = RuntimeReflection.FindActiveComponent("SashimiBoy.Stage01PlayableFlow") as Behaviour;
            if (flow != null) flow.enabled = false;
            if (save != null && originalSave != null)
            {
                RuntimeReflection.SetField(save, "current", originalSave);
                RuntimeReflection.SetField(save, "autoSaveOnChange", originalAutoSave);
            }
        }

        private int IntProperty(object target, string name) => (int)RuntimeReflection.Invoke(target, "get_" + name);
        private bool BoolProperty(object target, string name) => (bool)RuntimeReflection.Invoke(target, "get_" + name);
        private void Hit(int index) => RuntimeReflection.Invoke(timing, "ResolveGameplayInput", (double)RuntimeReflection.GetField(notes[index], "songTimeSeconds"));
        private SemanticChart Chart => (SemanticChart)RuntimeReflection.GetField(RuntimeReflection.GetField(timing, "semanticBeatmap"), "chart");

        private void FreezeVisualClockAt(int index) => RuntimeReflection.SetField(
            RuntimeReflection.GetField(timing, "audioClock"), "frozenSongTimeMs",
            (double)RuntimeReflection.GetField(notes[index], "songTimeSeconds") * 1000d);

        [UnityTest]
        public IEnumerator CookingView_UsesConnectedBodyAndKeepsFeedbackBesideTheNotes()
        {
            FreezeVisualClockAt(0);
            yield return new WaitForSecondsRealtime(.2f);
            var cooking = RuntimeReflection.FindActiveComponent("SashimiBoy.Stage01CookingView");
            var body = (Component)RuntimeReflection.GetField(cooking,"body");
            Assert.That(body,Is.Not.Null);
            Assert.That(body.GetComponentInChildren<Animator>().isHuman,Is.True);
            Assert.That(body.GetComponentsInChildren<SkinnedMeshRenderer>().Length,Is.GreaterThanOrEqualTo(3));
            float gripError = (float)RuntimeReflection.Invoke(body,"get_MaximumGripError");
            if (gripError >= .045f) Debug.Log("[KevinPose] " + body.transform.position + " right target=" + RuntimeReflection.GetField(body,"rightTarget") + " left target=" + RuntimeReflection.GetField(body,"leftTarget"));
            Assert.That(gripError,Is.LessThan(.045f),"The real skinned palms must reach their tool/fish targets.");
            foreach (string side in new[] { "right","left" })
            {
                object arm = RuntimeReflection.GetField(body,side);
                Transform upper = (Transform)RuntimeReflection.GetField(arm,"upper"), lower = (Transform)RuntimeReflection.GetField(arm,"lower"), hand = (Transform)RuntimeReflection.GetField(arm,"hand");
                Assert.That(lower.IsChildOf(upper),Is.True); Assert.That(hand.IsChildOf(lower),Is.True);
                Assert.That(Vector3.Distance(upper.position,lower.position),Is.EqualTo((float)RuntimeReflection.GetField(arm,"upperLength")).Within(.005f));
            }
            Assert.That(Camera.main.transform.position.y-body.transform.position.y,Is.InRange(1.55f,1.72f));
            Assert.That(Camera.main.transform.eulerAngles.x,Is.InRange(42f,54f));
            Assert.That(((Transform)RuntimeReflection.GetField(view,"handRoot")).GetComponentsInChildren<Renderer>().Length,Is.Zero,"The old floating hand mesh must be absent.");
            Hit(0); yield return null;
            var hud = (Component)RuntimeReflection.GetField(RuntimeReflection.GetField(timing,"presentationController"),"hud");
            var label = (UnityEngine.UI.Text)RuntimeReflection.GetField(hud,"lastJudgementText");
            Assert.That(label.text,Does.Contain("NASTY"));
            var score = (UnityEngine.UI.Text)RuntimeReflection.GetField(hud,"scoreText");
            Assert.That(score.transform.parent,Is.EqualTo(label.transform.parent));
            Assert.That(score.text,Does.Contain("1,000"));
            CaptureCamera("00-connected-hands-judged-test");
            var camera = Camera.main; Vector3 cameraPosition = camera.transform.position; Quaternion cameraRotation = camera.transform.rotation;
            RuntimeReflection.Invoke(body,"SetHeadHidden",false);
            camera.transform.position = new Vector3(2.7f,2.2f,-2.8f); camera.transform.LookAt(body.transform.position+Vector3.up*.85f);
            CaptureCamera("00-complete-kevin-external-judged-test");
            camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation); RuntimeReflection.Invoke(body,"SetHeadHidden",true);
        }

        [UnityTest]
        public IEnumerator EmptyInput_DeductsScoreAndVisiblePhaseQualityWithoutMovingTheFish()
        {
            Hit(0);
            int cursor = Performance.NoteCursor;
            double first = (double)RuntimeReflection.GetField(notes[0], "songTimeSeconds");
            double second = (double)RuntimeReflection.GetField(notes[1], "songTimeSeconds");
            Assert.That(second - first, Is.GreaterThan(.28d));
            RuntimeReflection.Invoke(timing, "ResolveGameplayInput", (first + second) / 2d);
            Assert.That(IntProperty(timing, "Score"), Is.EqualTo(900));
            Assert.That(Performance.PhasePoints, Is.EqualTo(.65d).Within(1e-6));
            Assert.That(Performance.NoteCursor, Is.EqualTo(cursor));
            var assembly = RuntimeReflection.GetField(view, "assembly");
            Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "head"), "IsAttached"), Is.True);
            Assert.That(Performance.Failed, Is.False);
            yield return null;
            var hud = RuntimeReflection.GetField(RuntimeReflection.GetField(timing, "presentationController"), "hud");
            var text = (UnityEngine.UI.Text)RuntimeReflection.GetField(hud, "phaseQualityText");
            Assert.That(text.text, Does.Contain("0.65"));
        }

        [UnityTest]
        public IEnumerator FinRemoval_KeepsOpenFishFlatAndFramed_ThroughSpineRemovalAndRetry()
        {
            var assembly = (Component)RuntimeReflection.GetField(view, "assembly");
            var body = (Component)RuntimeReflection.GetField(assembly, "body");
            var spine = (Component)RuntimeReflection.GetField(assembly, "spine");
            var fillet = (Component)RuntimeReflection.GetField(assembly, "fillet");
            Bounds bodyBounds = MeshWorldBounds(body);
            int finEnd = Chart.phases[2].firstNoteId;
            for (int i=0; i<finEnd-1; i++) Hit(i);
            FreezeVisualClockAt(finEnd-1);
            yield return new WaitForSecondsRealtime(.65f);
            CaptureCamera("transition-00-before-fin-removal");
            Hit(finEnd-1);
            yield return null;
            CaptureCamera("transition-01-open-fish-first-frame");
            Bounds spineBounds = MeshWorldBounds(spine);
            Assert.That(spineBounds.size.y, Is.LessThan(bodyBounds.size.y*.35f), "The exposed spine must lie flat, not rise toward Kevin's eyes.");
            Assert.That(spineBounds.size.x, Is.LessThan(bodyBounds.size.x), "Spine length fits the original fish.");
            Assert.That(spineBounds.size.z, Is.LessThan(bodyBounds.size.z), "Spine fits the fillet width.");
            Assert.That(Vector2.Distance(new Vector2(spineBounds.center.x,spineBounds.center.z),
                new Vector2(bodyBounds.center.x,bodyBounds.center.z)), Is.LessThan(.08f));
            Vector3 firstPosition=spine.transform.position, firstScale=spine.transform.localScale;
            Quaternion firstRotation=spine.transform.rotation;
            float transitionStart=Time.realtimeSinceStartup;
            bool capturedMovingFins=false;
            while(Time.realtimeSinceStartup-transitionStart<.70f)
            {
                Assert.That(MeshWorldBounds(spine).max.y,Is.LessThan(Camera.main.transform.position.y-.30f));
                AssertFishInFrame(fillet);
                yield return null;
                if(!capturedMovingFins && Time.realtimeSinceStartup-transitionStart>.20f)
                { CaptureCamera("transition-02-fins-moving-to-tray");capturedMovingFins=true; }
            }
            CaptureCamera("transition-03-spine-flat-on-fillet");
            Camera.main.aspect=4f/3f;
            yield return null;
            AssertFishInFrame(fillet);
            CaptureCamera("transition-03b-open-fish-4x3");
            Camera.main.aspect=16f/9f;
            yield return null;
            for(int i=finEnd;i<Chart.phases[3].firstNoteId;i++) Hit(i);
            FreezeVisualClockAt(Chart.phases[3].firstNoteId);
            yield return new WaitForSecondsRealtime(.7f);
            AssertFishInFrame(fillet);
            CaptureCamera("transition-04-spine-removed-full-fillet");
            RuntimeReflection.Invoke(timing,"RetryStage");
            RuntimeReflection.Invoke(RuntimeReflection.GetField(timing,"audioClock"),"Stop");
            for(int i=0;i<finEnd;i++) Hit(i);
            FreezeVisualClockAt(finEnd);
            yield return null;
            Assert.That(Vector3.Distance(spine.transform.position,firstPosition),Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(spine.transform.localScale,firstScale),Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(spine.transform.rotation,firstRotation),Is.LessThan(.01f));
            CaptureCamera("transition-05-retry-open-fish");
        }

        private static Bounds MeshWorldBounds(Component piece)
        {
            // Bounds are available in players without changing source Read/Write import settings.
            // Project every corner, conservatively covering the rendered geometry.
            var points=piece.GetComponentsInChildren<MeshFilter>(true).SelectMany(filter=>Enumerable.Range(0,8).Select(i=>
                filter.transform.TransformPoint(filter.sharedMesh.bounds.center+Vector3.Scale(filter.sharedMesh.bounds.extents,
                    new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))))).ToArray();
            var bounds=new Bounds(points[0],Vector3.zero);
            foreach(var point in points) bounds.Encapsulate(point);
            return bounds;
        }

        private static void AssertFishInFrame(Component piece)
        {
            Bounds bounds=MeshWorldBounds(piece);
            for(int i=0;i<8;i++)
            {
                Vector3 point=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                Vector3 viewport=Camera.main.WorldToViewportPoint(point);
                string framing=" Bounds="+bounds+" camera="+Camera.main.transform.position+" angles="+Camera.main.transform.eulerAngles+" FOV="+Camera.main.fieldOfView+" aspect="+Camera.main.aspect;
                if(viewport.x<.02f || viewport.x>.98f || viewport.y<.02f || viewport.y>.98f) CaptureCamera("transition-framing-failure");
                Assert.That(viewport.z,Is.GreaterThan(Camera.main.nearClipPlane),"Fish crosses the near plane.");
                Assert.That(viewport.x,Is.InRange(.02f,.98f),"Fish must remain horizontally visible during the phase transition."+framing);
                Assert.That(viewport.y,Is.InRange(.02f,.98f),"Fish must remain vertically visible during the phase transition."+framing);
            }
        }

        [UnityTest]
        public IEnumerator SavedChart_RealPartsPlateClearAndRetryResetTogether()
        {
            AssertInitial();
            Assert.That(Chart.notes[0].action, Is.EqualTo(FishAction.CutHead));
            Assert.That(Camera.main.orthographic, Is.False);
            FreezeVisualClockAt(0);
            yield return null;
            var contact = (Transform)RuntimeReflection.GetField(view, "knifeContactAnchor");
            var cue = (Vector3)RuntimeReflection.Invoke(view, "GetCueWorldPosition", 0);
            Assert.That(Vector3.Distance(contact.position, cue), Is.LessThan(.13f), "Actual blade contact must meet the neck cue.");
            var firstGate = Chart.phases[0];
            Assert.That((Vector3)RuntimeReflection.Invoke(view, "GetCueWorldPosition", firstGate.lastNoteId), Is.EqualTo(cue), "The first phase must stay at the neck.");
            CaptureCamera("01-start-judged-test");
            var assembly = RuntimeReflection.GetField(view, "assembly");
            for (int phase = 0; phase < 6; phase++)
            {
                var gate = Chart.phases[phase];
                for (int i = gate.firstNoteId; i <= gate.lastNoteId; i++) Hit(i);
                FreezeVisualClockAt(gate.lastNoteId);
                yield return new WaitForSecondsRealtime(.7f);
                Assert.That(Performance.Failed, Is.False);
                Assert.That(Performance.PhaseIndex, Is.EqualTo(phase + 1));
                var cooking = RuntimeReflection.FindActiveComponent("SashimiBoy.Stage01CookingView");
                var body = (Component)RuntimeReflection.GetField(cooking,"body");
                Debug.Log("[KevinPhase] phase=" + (phase+1) + " camera=" + Camera.main.transform.position + " body=" + body.transform.position + " target=" + RuntimeReflection.GetField(body,"rightTarget") + " working=" + RuntimeReflection.GetField(body,"working") + " error=" + RuntimeReflection.Invoke(body,"get_MaximumGripError"));
                if (phase<5) Assert.That((float)RuntimeReflection.Invoke(body,"get_MaximumGripError"),Is.LessThan(.055f),"Connected hands must reach throughout every cooking phase.");
                if (phase == 0)
                {
                    Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "head"), "IsAttached"), Is.False);
                    CaptureCamera("02-head-separated-judged-test");
                }
                if (phase == 1)
                {
                    Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "body"), "IsVisible"), Is.False, "Body contains embedded fins and must leave the cutting state.");
                    Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "fillet"), "IsVisible"), Is.True);
                    CaptureCamera("03-open-fillet-and-spine-judged-test");
                }
                if (phase == 2)
                {
                    Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "spine"), "IsAttached"), Is.False);
                    Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "fillet"), "IsVisible"), Is.True);
                    CaptureCamera("04-full-fillet-split-judged-test");
                }
                if (phase == 3)
                {
                    Assert.That(((GameObject)RuntimeReflection.GetField(view, "filletHalf")).activeInHierarchy, Is.True);
                    Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "fillet"), "IsVisible"), Is.False);
                    Assert.That(IntProperty(view, "RemovedBoneCount"), Is.Zero);
                    var caps = (GameObject[])RuntimeReflection.GetField(RuntimeReflection.GetField(view, "filletSurface"), "cutCaps");
                    foreach (var cap in caps) Assert.That(cap.activeSelf, Is.False, "Cross-section image must not cover an uncut half.");
                    CaptureCamera("05-half-pin-bones-ready-judged-test");
                }
                if (phase == 4)
                {
                    Assert.That(IntProperty(view, "RemovedBoneCount"), Is.EqualTo(8));
                    Assert.That(((GameObject)RuntimeReflection.GetField(view, "filletHalf")).activeInHierarchy, Is.True);
                    CaptureCamera("06-half-before-slicing-judged-test");
                }
            }
            Assert.That(IntProperty(view, "SliceCount"), Is.EqualTo(12));
            var cutCaps = (GameObject[])RuntimeReflection.GetField(RuntimeReflection.GetField(view, "filletSurface"), "cutCaps");
            int visibleCaps = 0; foreach (var cap in cutCaps) if (cap.activeSelf) visibleCaps++;
            Assert.That(visibleCaps, Is.EqualTo(1));
            Assert.That(BoolProperty(view, "PlateComplete"), Is.True);
            Assert.That(Performance.Completed, Is.True);
            RuntimeReflection.Invoke(timing, "AdvanceStageTimeline", 121d, 121d);
            Assert.That(BoolProperty(timing, "IsResultShown"), Is.True);
            var current = RuntimeReflection.GetField(save, "current");
            Assert.That(RuntimeReflection.Invoke(current, "IsStageCleared", "STAGE_01_SALMON"), Is.EqualTo(true));
            object salmonType = Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.FishType"), "Salmon");
            int awarded = (int)RuntimeReflection.Invoke(current, "GetPlates", salmonType);
            Assert.That(awarded, Is.GreaterThan(0));
            RuntimeReflection.Invoke(timing, "FinalizeStageResultOnce");
            Assert.That(RuntimeReflection.Invoke(current, "GetPlates", salmonType), Is.EqualTo(awarded));
            RuntimeReflection.Invoke(RuntimeReflection.GetField(timing, "audioClock"), "Stop");
            Assert.That(RuntimeReflection.Invoke(timing, "get_CurrentSection").ToString(), Is.EqualTo("Result"),
                "A finished/stopped clip must not return the clear screen's HUD to the boss demo.");
            yield return null;
            CaptureCamera("07-completed-plate-judged-test");
            Assert.That(RuntimeReflection.Invoke(timing, "RetryStage"), Is.EqualTo(true));
            yield return null;
            AssertInitial();
            Assert.That(IntProperty(timing, "Score"), Is.Zero);
            Assert.That(IntProperty(timing, "Combo"), Is.Zero);
            Assert.That(Performance.NoteCursor, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LastGateFailure_ReturnsToShopAndReentryStartsFreshWithoutReward()
        {
            int lastStart = Chart.phases[5].firstNoteId;
            for (int i = 0; i < lastStart + 9; i++) Hit(i);
            var active = RuntimeReflection.GetField(timing, "activeNoteTracker");
            RuntimeReflection.Invoke(active, "ResolveRemainingNotesAsMissed");
            Assert.That(Performance.Failed, Is.True);
            Assert.That(IntProperty(view, "SliceCount"), Is.GreaterThan(0));
            Assert.That(BoolProperty(view, "PlateComplete"), Is.False);
            Assert.That(BoolProperty(RuntimeReflection.GetField(timing, "audioClock"), "IsRunning"), Is.False);
            var current = RuntimeReflection.GetField(save, "current");
            Assert.That(RuntimeReflection.Invoke(current, "IsStageCleared", "STAGE_01_SALMON"), Is.EqualTo(false));
            Assert.That(RuntimeReflection.Invoke(current, "IsStageUnlocked", "STAGE_02_ROCKFISH"), Is.EqualTo(false));
            yield return new WaitForSecondsRealtime(.5f);
            CaptureCamera("08-failure-blur-judged-test");
            float deadline = Time.realtimeSinceStartup + 15f;
            while (SceneManager.GetActiveScene().name != "FishShopDialogue" && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("FishShopDialogue"));
            yield return null; yield return null;
            var player = RuntimeReflection.FindActiveComponent("SashimiBoy.SimpleTopDownPlayerController");
            var rig = RuntimeReflection.FindActiveComponent("SashimiBoy.KevinFirstPersonCameraRig");
            Assert.That(BoolProperty(player, "InputEnabled"), Is.True);
            Assert.That(BoolProperty(rig, "IsInputBlocked"), Is.False);
            var shopBody = RuntimeReflection.FindActiveComponent("SashimiBoy.KevinBodyRig");
            Assert.That(shopBody,Is.Not.Null,"The complete Kevin body must also load in the shop.");
            Assert.That(((Transform)RuntimeReflection.GetField(rig,"pitchRoot")).localPosition.z,Is.GreaterThan(.12f));
            CaptureCamera("09-shop-body-view-judged-test");
            Assert.That(Object.FindObjectsByType<AudioListener>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>().Length, Is.EqualTo(1));
            var starter = RuntimeReflection.FindActiveComponent("SashimiBoy.StageStarterInteractable");
            RuntimeReflection.Invoke(starter, "Interact", player.gameObject);
            deadline = Time.realtimeSinceStartup + 15f;
            while (SceneManager.GetActiveScene().name != "Stage01_Salmon" && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Stage01_Salmon"));
            yield return null;
            timing = RuntimeReflection.FindActiveComponent("SashimiBoy.Stage01SalmonTimingScaffold");
            view = RuntimeReflection.FindActiveComponent("SashimiBoy.Stage01ButcheryPresenter");
            AssertInitial();
            Assert.That(Performance.NoteCursor, Is.Zero);
            Assert.That(IntProperty(timing, "Score"), Is.Zero);
            Assert.That(BoolProperty(RuntimeReflection.GetField(timing, "audioClock"), "IsRunning"), Is.True);
        }

        private void AssertInitial()
        {
            var assembly = RuntimeReflection.GetField(view, "assembly");
            Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "head"), "IsAttached"), Is.True);
            Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "head"), "IsVisible"), Is.True);
            Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "body"), "IsVisible"), Is.True);
            Assert.That(BoolProperty(RuntimeReflection.GetField(assembly, "spine"), "IsVisible"), Is.False);
            Assert.That(IntProperty(view, "RemovedBoneCount"), Is.Zero);
            Assert.That(IntProperty(view, "SliceCount"), Is.Zero);
            Assert.That(BoolProperty(view, "PlateComplete"), Is.False);
            var presentation = RuntimeReflection.GetField(timing, "presentationController");
            var procedural = (Component)RuntimeReflection.GetField(presentation, "salmon");
            Assert.That(procedural.gameObject.activeInHierarchy, Is.False);
        }

        private static void CaptureCamera(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            int width=1600, height=Mathf.RoundToInt(width/camera.aspect);
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var overlays=Object.FindObjectsByType<Canvas>().Where(c=>c.renderMode==RenderMode.ScreenSpaceOverlay)
                .Select(c=>(canvas:c,camera:c.worldCamera,distance:c.planeDistance)).ToArray();
            try
            {
                foreach(var item in overlays) { item.canvas.renderMode=RenderMode.ScreenSpaceCamera;item.canvas.worldCamera=camera;item.canvas.planeDistance=.15f; }
                Canvas.ForceUpdateCanvases();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                string directory = Path.Combine(Application.dataPath, "../Logs/Stage1Embodiment/CameraEvidence");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                foreach(var item in overlays) { item.canvas.renderMode=RenderMode.ScreenSpaceOverlay;item.canvas.worldCamera=item.camera;item.canvas.planeDistance=item.distance; }
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                Object.Destroy(pixels); Object.Destroy(target);
            }
        }
    }
}
