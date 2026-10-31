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
        double[] Notes => (double[])Get(((IList)Get(Get(controller, "chart"), "phases"))[Value<int>(Run, "Phase")], "noteTimesMs");

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
        public IEnumerator All24Cuts_KeepBothActualPalmsInReach_WithoutChangingBodyHeightScaleOrBoneLengths()
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

            // Enter the actual separated-fillets path, including the real turn
            // gate. Only note timestamps are synthetic; the body, knife, work
            // transforms, cut caches and support targets are the authored ones.
            while (Value<int>(Run, "Phase") < 5)
            {
                int phase = Value<int>(Run, "Phase");
                double[] notes = Notes;
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(Call(controller, "SubmitAt", notes[i]), Is.EqualTo(true));
                    yield return ObserveActualStroke(cooking, body, right, left, rootHeight, bodyScale,
                        originalLengths, jointPositions, jointScales, "preparation " + phase + ", stroke " + i);
                }
                // The controller owns its motion queue and contact callbacks.
                // Flush only the remaining detached-part presentation before the phase tick.
                Call(controller, "AdvancePresentation", 4f);
                Call(controller, "TickAt", notes.Last() + 1000d);
                if (phase == 2)
                {
                    Assert.That(Value<bool>(Run, "WaitingForTurn"), Is.True);
                    Assert.That(Call(Assembly, "TryTurnOver"), Is.EqualTo(true));
                    Call(Assembly, "AdvancePresentation", 1f);
                }
                yield return null;
            }

            for (int fillet = 0; fillet < 4; fillet++)
            {
                Assert.That(Value<int>(Run, "Phase"), Is.EqualTo(5 + fillet));
                double[] notes = Notes;
                for (int cut = 0; cut < 6; cut++)
                {
                    int before = Value<int>(controller, "SpawnedSlices");
                    Assert.That(Call(controller, "SubmitAt", notes[cut]), Is.EqualTo(true));
                    Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(before),
                        "Accepting a rhythm hit must not detach a slice before the blade's contact cue.");
                    yield return ObserveActualStroke(cooking, body, right, left, rootHeight, bodyScale,
                        originalLengths, jointPositions, jointScales, "fillet " + fillet + ", cut " + cut);
                    Assert.That(Value<int>(controller, "SpawnedSlices"), Is.EqualTo(before + 1),
                        "Each completed actual blade motion must produce exactly one slice.");
                }
                if (fillet < 3)
                {
                    // A next fillet resets the cutting plane across the whole
                    // work area. The first frame must also keep the palms attached.
                    Call(controller, "TickAt", notes.Last() + 1000d);
                    yield return null;
                    AssertPalm(right, (Vector3)Get(body, "rightTarget"), "next-fillet knife");
                    AssertPalm(left, (Vector3)Get(body, "leftTarget"), "next-fillet support");
                }
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
