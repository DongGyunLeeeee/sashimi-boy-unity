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
        }

        [TearDown]
        public void TearDown()
        {
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
            do
            {
                // Use real scene frames through the entire approach/contact/follow-through,
                // rather than a fixed fixture or only the former 0.28-second vertical pulse.
                yield return null;
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
                Assert.That(Vector3.Distance(((Transform)Get(left,"palm")).position,Value<Vector3>(cooking,"SafeSupportTarget")),Is.LessThanOrEqualTo(.015f),context);
                if (Time.realtimeSinceStartup >= deadline &&
                    (Value<bool>(cooking, "IsStriking") || Value<int>(controller, "PendingVisualCuts") > 0))
                    Assert.Fail("The accepted stroke did not complete its actual presentation: " + context);
            }
            while (Value<bool>(cooking, "IsStriking") || Value<int>(controller, "PendingVisualCuts") > 0);
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
