using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    public sealed class Stage03CookingStancePlayModeTests
    {
        Component save, flow, controller;
        object originalSave, originalAuto, originalPath;
        static object Get(object target, string name) => RuntimeReflection.GetField(target, name);
        static void Set(object target, string name, object value) => RuntimeReflection.SetField(target, name, value);
        static object Call(object target, string method, params object[] args) => RuntimeReflection.Invoke(target, method, args);
        static T Value<T>(object target, string property) => (T)Call(target, "get_" + property);
        static Component Find(string name) => RuntimeReflection.FindActiveComponent("SashimiBoy." + name);
        object Run => Call(controller, "get_Run");
        object Assembly => Get(controller, "assembly");
        object PhaseChart=>((IList)Get(Get(controller,"chart"),"phases"))[Value<int>(Run,"Phase")];
        double[] Notes=>(double[])Get(PhaseChart,"noteTimesMs");
        bool observedCamera;
        Vector3 cameraPosition;
        Quaternion cameraRotation;
        SkinnedMeshRenderer supportRenderer;
        int[] supportIndices;
        Mesh supportBake;
        Mesh supportUncompensatedBake;
        readonly List<Vector3> supportBakedVertices=new List<Vector3>();
        readonly List<Vector3> supportUncompensatedVertices=new List<Vector3>();
        Renderer[] knifeRenderers;
        Stage03StanceLateFrameProbe lateFrameProbe;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            yield return null;
            save = Find("SaveManager");
            flow = Find("DayWorldFlow");
            originalSave = Get(save, "current");
            originalAuto = Get(save, "autoSaveOnChange");
            originalPath = Get(save, "validationSavePath");
            Call(save, "ConfigureValidationProfile", "stage3-stance-" + Guid.NewGuid().ToString("N"));
            Set(save, "autoSaveOnChange", false);
            Set(save, "current", RuntimeReflection.InvokeStatic("SashimiBoy.SaveData", "CreateNew"));
            Call(flow, "SetBusy", false);
            yield return SceneManager.LoadSceneAsync("Stage03_Flounder");
            yield return null;
            controller = Find("Stage03GameController");
            Assert.That(controller, Is.Not.Null);
            Call(controller,"ResetRun"); // Normal entry autoplays; this fixture drives controlled input timestamps.
            observedCamera=false;
            supportRenderer=null;supportIndices=null;knifeRenderers=null;
            lateFrameProbe=new GameObject("Stage03_IndependentLateFrameProbe").AddComponent<Stage03StanceLateFrameProbe>();
        }

        [TearDown]
        public void TearDown()
        {
            if(lateFrameProbe!=null)UnityEngine.Object.DestroyImmediate(lateFrameProbe.gameObject);
            lateFrameProbe=null;
            if(supportBake!=null)UnityEngine.Object.DestroyImmediate(supportBake);
            if(supportUncompensatedBake!=null)UnityEngine.Object.DestroyImmediate(supportUncompensatedBake);
            supportBake=null;supportUncompensatedBake=null;supportBakedVertices.Clear();supportUncompensatedVertices.Clear();
            if (controller != null) Call(Get(controller, "clock"), "Stop");
            if (save != null)
            {
                Set(save, "current", originalSave);
                Set(save, "autoSaveOnChange", originalAuto);
                Set(save, "validationSavePath", originalPath);
            }
            if (flow != null) Call(flow, "SetBusy", false);
        }

        [UnityTest]
        public IEnumerator FirstPersonSurfacesShareTheActualRig_AndRestoreFullBodyRenderingWhenDisabled()
        {
            var cooking=(Behaviour)Get(controller,"cooking");
            var main=(SkinnedMeshRenderer)Get(cooking,"firstPersonBody");
            var arms=(SkinnedMeshRenderer)Get(cooking,"firstPersonArms");
            Assert.That(main,Is.Not.Null);Assert.That(arms,Is.Not.Null);
            Assert.That(main,Is.Not.SameAs(arms));Assert.That(main.sharedMesh.name,Is.EqualTo("MS_RiggedBody_WithoutHead"));
            Assert.That(arms.sharedMesh.name,Is.EqualTo("MS_Stage03_FirstPersonArms"));
            Assert.That(arms.bones,Is.EqualTo(main.bones));Assert.That(arms.rootBone,Is.SameAs(main.rootBone));
            Assert.That(main.shadowCastingMode,Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly));
            Assert.That(arms.enabled,Is.True);
            cooking.enabled=false;yield return null;
            Assert.That(main.shadowCastingMode,Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.On));
            Assert.That(arms.enabled,Is.False);
            cooking.enabled=true;yield return null;
            Assert.That(main.shadowCastingMode,Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly));
            Assert.That(arms.enabled,Is.True);Assert.That(arms.bones,Is.EqualTo(main.bones));
        }

        [UnityTest]
        public IEnumerator All24Cuts_KeepPalmsClearOfBladeAndCameraFixed_WithoutStretchingTheSuppliedBody()
        {
            var cooking = Get(controller, "cooking");
            var body = (Component)Get(cooking, "body");
            var right = Get(body, "right");
            var left = Get(body, "left");
            float rootHeight = ((Vector3)Get(cooking, "standingPosition")).y;
            Vector3 bodyScale = body.transform.lossyScale;
            float[] originalLengths = { Upper(right), Lower(right), Upper(left), Lower(left) };
            var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(skins.Any(s => s.sharedMesh != null && s.sharedMesh.name == "MS_RiggedBody_WithoutHead"),
                Is.True, "The real Stage3 scene must use the supplied body, rather than passing reach checks with the previous model.");
            var bones = skins.SelectMany(s => s.bones).Distinct().ToArray();
            Assert.That(bones.All(b => b != null), Is.True);
            var jointPositions = bones.ToDictionary(b => b, b => b.localPosition);
            var jointScales = bones.ToDictionary(b => b, b => b.localScale);
            Assert.That(Call(controller, "StartRun"), Is.EqualTo(true));

            // Every note uses the actual input/presentation path. Observe real
            // rendered frames on each first/quota/final preparation stroke and
            // all 24 actual slice contacts; remaining beats are advanced through
            // that same presentation API, without waiting a whole song in tests.
            for(int phase=0;phase<8;phase++)
            {
                Assert.That(Value<int>(Run,"Phase"),Is.EqualTo(phase));
                double[] notes=Notes;double deadline=(double)Get(PhaseChart,"endTimeMs");
                int required=Value<int>(Run,"RequiredHits");
                for(int i=0;i<notes.Length;i++)
                {
                    int before=Value<int>(controller,"SpawnedSlices");
                    Assert.That(Call(controller,"SubmitAt",notes[i]),Is.EqualTo(true));
                    int wanted=Value<int>(Run,"TotalSlices");
                    Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(before),"Mesh exchange cannot precede contact.");
                    bool observe=phase<6?(i==0||i==required-1||i==notes.Length-1):wanted>before||i==notes.Length-1;
                    if(observe)
                        yield return ObserveActualStroke(cooking,body,right,left,rootHeight,bodyScale,
                            originalLengths,jointPositions,jointScales,"phase "+phase+", note "+i);
                    else {Call(controller,"AdvancePresentation",.30f);yield return null;}
                    Assert.That(Value<int>(controller,"SpawnedSlices"),Is.EqualTo(wanted));
                    Assert.That(Value<bool>(Get(controller,"clock"),"IsRunning"),Is.True,"Quotas and the turnover never stop the song.");
                }
                Call(controller,"AdvancePresentation",2f);
                Assert.That(Value<int>(controller,"PendingVisualCuts"),Is.Zero);
                Call(controller,"TickAt",deadline+1d);
                Assert.That(Value<int>(Run,"Phase"),Is.EqualTo(phase+1));
                if(phase<7)yield return null;
            }
            Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(24));
        }

        IEnumerator ObserveActualStroke(object cooking, Component body, object right, object left,
            float rootHeight, Vector3 bodyScale, float[] originalLengths,
            Dictionary<Transform, Vector3> jointPositions, Dictionary<Transform, Vector3> jointScales, string context)
        {
            float deadline = Time.realtimeSinceStartup + 4f;
            Assert.That(Value<bool>(cooking, "IsStriking"), Is.True, "A judged hit must begin an actual tool motion: " + context);
            lateFrameProbe.Capture=()=>
            {
                Assert.That(Value<bool>(Run, "Failed"), Is.False, context);
                AssertPalm(right, (Vector3)Get(body, "rightTarget"), "knife: " + context);
                AssertPalm(left, (Vector3)Get(body, "leftTarget"), "support: " + context);
                Assert.That(body.transform.position.y, Is.EqualTo(rootHeight).Within(.0001f), context);
                Assert.That(Vector3.Distance(body.transform.lossyScale, bodyScale), Is.LessThan(.0001f), context);
                Assert.That(Upper(right), Is.EqualTo(originalLengths[0]).Within(.0001f), context);
                Assert.That(Lower(right), Is.EqualTo(originalLengths[1]).Within(.0001f), context);
                Assert.That(Upper(left), Is.EqualTo(originalLengths[2]).Within(.0001f), context);
                Assert.That(Lower(left), Is.EqualTo(originalLengths[3]).Within(.0001f), context);
                Assert.That(jointPositions.All(p => p.Key.localPosition == p.Value && p.Key.localScale == jointScales[p.Key]),
                    Is.True, "Only the grounded root may move; joints must not translate or stretch: " + context);
                var camera=(Camera)Get(cooking,"gameCamera");
                if(!observedCamera)
                {observedCamera=true;cameraPosition=camera.transform.position;cameraRotation=camera.transform.rotation;}
                Assert.That(Vector3.Distance(camera.transform.position,cameraPosition),Is.LessThan(.0001f),"Work points and grip IK cannot move the gameplay camera: "+context);
                var rotation=camera.transform.rotation;
                Assert.That(Vector4.Distance(new Vector4(rotation.x,rotation.y,rotation.z,rotation.w),new Vector4(cameraRotation.x,cameraRotation.y,cameraRotation.z,cameraRotation.w)),Is.LessThan(.00001f),"Cut direction cannot rotate the gameplay camera: "+context);
                Assert.That(Value<float>(cooking,"SupportBladeClearance"),Is.GreaterThanOrEqualTo(.0749f),"Actual posed support-hand and finger joints must remain clear of the swept blade plane: "+context);
                AssertSupportSurface(cooking,body,left,context);
                Assert.That(Vector3.Distance(((Transform)Get(left,"palm")).position,Value<Vector3>(cooking,"SafeSupportTarget")),Is.LessThanOrEqualTo(.015f),context);
            };
            try
            {
                // Use real frames throughout approach/contact/follow-through.
                // Do not manually invoke the production pose/measurement code,
                // and do not depend on WaitForEndOfFrame in batch-mode tests.
                while(true)
                {
                    int before=lateFrameProbe.Samples;
                    yield return null;
                    AssertLateFrame(before,context);
                    bool moving=Value<bool>(cooking,"IsStriking")||Value<int>(controller,"PendingVisualCuts")>0;
                    if(!moving)
                    {
                        // The stroke can end in this Update, before this coroutine
                        // resumes. Include that frame's final LateUpdate as well.
                        before=lateFrameProbe.Samples;
                        yield return null;
                        AssertLateFrame(before,context);
                        break;
                    }
                    if(Time.realtimeSinceStartup>=deadline)
                        Assert.Fail("The accepted stroke did not complete its actual presentation: "+context);
                }
            }
            finally {if(lateFrameProbe!=null)lateFrameProbe.Capture=null;}
        }

        void AssertLateFrame(int before,string context)
        {
            Assert.That(lateFrameProbe.Samples,Is.GreaterThan(before),"An actual post-rig LateUpdate sample is required: "+context);
            if(lateFrameProbe.Failure!=null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(lateFrameProbe.Failure).Throw();
        }

        void AssertSupportSurface(object cooking,Component body,object left,string context)
        {
            if(supportRenderer==null)
            {
                supportRenderer=(SkinnedMeshRenderer)Get(cooking,"firstPersonArms");
                Assert.That(supportRenderer,Is.Not.Null,"Validate the actual rendered first-person hand surface.");
                Assert.That(supportRenderer.enabled,Is.True);
                Assert.That(supportRenderer.shadowCastingMode,Is.Not.EqualTo(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly));
                var mesh=supportRenderer.sharedMesh;var weights=mesh.boneWeights;var bones=supportRenderer.bones;
                var hand=(Transform)Get(left,"hand");var selected=new bool[mesh.vertexCount];
                bool IsHand(int index)=>index>=0&&index<bones.Length&&bones[index]!=null&&(bones[index]==hand||bones[index].IsChildOf(hand));
                for(int i=0;i<weights.Length;i++)
                {
                    var w=weights[i];
                    selected[i]=(IsHand(w.boneIndex0)&&w.weight0>0)||(IsHand(w.boneIndex1)&&w.weight1>0)||
                        (IsHand(w.boneIndex2)&&w.weight2>0)||(IsHand(w.boneIndex3)&&w.weight3>0);
                }
                var expanded=(bool[])selected.Clone();var triangles=mesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                    if(selected[triangles[i]]||selected[triangles[i+1]]||selected[triangles[i+2]])
                        expanded[triangles[i]]=expanded[triangles[i+1]]=expanded[triangles[i+2]]=true;
                supportIndices=Enumerable.Range(0,expanded.Length).Where(i=>expanded[i]).ToArray();
                Assert.That(supportIndices.Length,Is.EqualTo(1190),"Validate the complete provided support hand and its incident triangles.");
                supportBake=new Mesh{name="Stage03IndependentSupportSkinValidation"};
                supportUncompensatedBake=new Mesh{name="Stage03IndependentUncompensatedSkinValidation"};
                knifeRenderers=((Transform)Get(cooking,"knifeRoot")).GetComponentsInChildren<Renderer>(true);
            }
            Transform knife=(Transform)Get(cooking,"knifeRoot");
            Vector3 axis=Vector3.ProjectOnPlane(knife.forward,Vector3.up).normalized;
            Vector3 normal=Vector3.Cross(Vector3.up,axis).normalized;
            if(Vector3.Dot(normal,Vector3.left)<-.01f)normal=-normal;
            float toolMax=float.NegativeInfinity;
            Vector3 absolute=new Vector3(Mathf.Abs(normal.x),Mathf.Abs(normal.y),Mathf.Abs(normal.z));
            foreach(var renderer in knifeRenderers)
                toolMax=Mathf.Max(toolMax,Vector3.Dot(renderer.bounds.center,normal)+Vector3.Dot(renderer.bounds.extents,absolute));
            // Independent native skinning: do not reproduce the runtime's
            // weighted bone/bindpose multiplication in this regression.
            // true compensates the renderer scale, so its local vertices use
            // the complete renderer TRS exactly once. false already includes
            // that scale and must use only translation+rotation to reach world.
            supportRenderer.BakeMesh(supportBake,true);supportBake.GetVertices(supportBakedVertices);
            supportRenderer.BakeMesh(supportUncompensatedBake,false);supportUncompensatedBake.GetVertices(supportUncompensatedVertices);
            Matrix4x4 unscaledToWorld=Matrix4x4.TRS(supportRenderer.transform.position,supportRenderer.transform.rotation,Vector3.one);
            float handMin=float.PositiveInfinity,uncompensatedMin=float.PositiveInfinity,maxPathDifference=0f;
            foreach(int index in supportIndices)
            {
                Vector3 world=supportRenderer.transform.TransformPoint(supportBakedVertices[index]);
                Vector3 fromUncompensated=unscaledToWorld.MultiplyPoint3x4(supportUncompensatedVertices[index]);
                maxPathDifference=Mathf.Max(maxPathDifference,Vector3.Distance(world,fromUncompensated));
                handMin=Mathf.Min(handMin,Vector3.Dot(world,normal));
                uncompensatedMin=Mathf.Min(uncompensatedMin,Vector3.Dot(fromUncompensated,normal));
            }
            Assert.That(maxPathDifference,Is.LessThan(.00005f),"Compensated and uncompensated native skin paths must agree without applying the renderer scale twice: "+context);
            float actualGap=handMin-toolMax;
            Assert.That(actualGap,Is.GreaterThanOrEqualTo(.0199f),"Actual native-baked hand surface must clear the conservative knife bounds by 2cm: "+context);
            Assert.That(uncompensatedMin-toolMax,Is.GreaterThanOrEqualTo(.0199f),context);
            Assert.That(Value<int>(cooking,"SupportMeshVertexCount"),Is.EqualTo(supportIndices.Length));
            Assert.That(Value<float>(cooking,"SupportSurfaceGap"),Is.GreaterThanOrEqualTo(.0199f),context);
            Assert.That(Value<float>(cooking,"SupportSurfaceGap"),Is.EqualTo(actualGap).Within(.00005f),
                "Cached CPU skinning must agree with independent BakeMesh(true) plus full TRS in world space: "+context);
        }

        static void AssertPalm(object arm, Vector3 target, string context)
        {
            var palm = (Transform)Get(arm, "palm");
            Assert.That(Vector3.Distance(palm.position, target), Is.LessThanOrEqualTo(.015f), context);
        }
        static float Upper(object arm) => Vector3.Distance(((Transform)Get(arm, "upper")).position, ((Transform)Get(arm, "lower")).position);
        static float Lower(object arm) => Vector3.Distance(((Transform)Get(arm, "lower")).position, ((Transform)Get(arm, "hand")).position);
    }
}
