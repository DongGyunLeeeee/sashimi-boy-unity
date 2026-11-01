using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class Stage03GameplayEditModeTests
    {
        static object NewRun() => Activator.CreateInstance(RuntimeReflection.RuntimeType("SashimiBoy.Stage03RunState"));
        static object Call(object target, string method, params object[] args) => RuntimeReflection.Invoke(target, method, args);
        static T Value<T>(object target, string property) => (T)Call(target, "get_" + property);
        static void Satisfy(object run)
        {
            int needed = Value<int>(run, "RequiredHits");
            for (int i = 0; i < needed; i++) Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false), "Extra resolved input cannot add another cut.");
        }
        static void ReachFirstSlicingPhase(object run)
        {
            for (int phase = 0; phase < 5; phase++)
            {
                Assert.That(Value<int>(run, "Phase"), Is.EqualTo(phase));
                Satisfy(run);
                Assert.That(Call(run, "TryAdvance"), Is.EqualTo(phase != 2));
                if (phase == 2)
                {
                    Assert.That(Value<bool>(run, "WaitingForTurn"), Is.True);
                    Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
                    Assert.That(Call(run, "FinishTurn"), Is.EqualTo(true));
                    Assert.That(Call(run, "FinishTurn"), Is.EqualTo(false));
                }
            }
        }

        [Test]
        public void Run_RequiresHeadAndFrontTwoBeforeTurn_AndExactlyFourTimesSixSlices()
        {
            var run = NewRun();
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Assert.That(Call(run, "FinishTurn"), Is.EqualTo(false));
            ReachFirstSlicingPhase(run);
            Assert.That(Value<int>(run, "TotalSlices"), Is.Zero);
            for (int fillet = 0; fillet < 4; fillet++)
            {
                for (int cut = 0; cut < 5; cut++) Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
                Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false), "Five cuts cannot finish a six-piece fillet.");
                Assert.That(Value<bool>(run, "Completed"), Is.False);
                Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
                Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
                Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true));
            }
            Assert.That(Value<bool>(run, "Completed"), Is.True);
            Assert.That(Value<int>(run, "TotalSlices"), Is.EqualTo(24));
            Assert.That(Value<int[]>(run, "Slices"), Is.EqualTo(new[] { 6, 6, 6, 6 }));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Call(run, "Fail");
            Assert.That(Value<bool>(run, "Failed"), Is.False, "A finished plate cannot be converted into failure.");
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(false));
        }

        [Test]
        public void FailedFilletRetry_RetainsEarlierFillets_AndResetsOnlyCurrentCuts()
        {
            var run = NewRun();
            ReachFirstSlicingPhase(run);
            Satisfy(run);
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true));
            for (int i = 0; i < 3; i++) Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
            Call(run, "Fail");
            Assert.That(Value<int[]>(run, "Slices"), Is.EqualTo(new[] { 6, 3, 0, 0 }));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(true));
            Assert.That(Value<int[]>(run, "Slices"), Is.EqualTo(new[] { 6, 0, 0, 0 }));
            Assert.That(Value<int>(run, "Phase"), Is.EqualTo(6));
            Assert.That(Value<bool>(run, "Turned"), Is.True);
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(false));
            for (int i = 0; i < 3; i++) { Satisfy(run); Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true)); }
            Assert.That(Value<int>(run, "TotalSlices"), Is.EqualTo(24));
            Call(run, "Reset");
            Assert.That(Value<int>(run, "Phase"), Is.Zero);
            Assert.That(Value<int>(run, "TotalSlices"), Is.Zero);
            Assert.That(Value<bool>(run, "Turned"), Is.False);
            Assert.That(Value<bool>(run, "WaitingForTurn"), Is.False);
        }

        [Test]
        public void FailureDuringFrontWork_RetryDoesNotSkipTheRequiredTurn()
        {
            var run = NewRun();
            for (int i = 0; i < 2; i++) { Satisfy(run); Call(run, "TryAdvance"); }
            Assert.That(Value<int>(run, "Phase"), Is.EqualTo(2));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
            Call(run, "Fail");
            Assert.That(Call(run, "FinishTurn"), Is.EqualTo(false));
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(true));
            Satisfy(run);
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Assert.That(Value<bool>(run, "WaitingForTurn"), Is.True);
            Assert.That(Value<bool>(run, "Turned"), Is.False);
            Assert.That(Value<int>(run, "Phase"), Is.EqualTo(2));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
        }

        [Test]
        public void CookingStroke_UsesThreePullingPaths_AndMeetsTheCutPlaneAtContact()
        {
            string type="SashimiBoy.Stage03CookingView";
            Vector3[] approaches=new Vector3[3];int[] phases={0,1,5};
            for(int i=0;i<phases.Length;i++)
            {
                approaches[i]=(Vector3)RuntimeReflection.InvokeStatic(type,"StrokeOffset",phases[i],.15f);
                var contact=(Vector3)RuntimeReflection.InvokeStatic(type,"StrokeOffset",phases[i],.45f);
                var follow=(Vector3)RuntimeReflection.InvokeStatic(type,"StrokeOffset",phases[i],.7f);
                Assert.That(approaches[i].x,Is.LessThan(0),"The blade approaches along the cutting direction.");
                Assert.That(follow.x,Is.GreaterThan(0),"The blade draws through instead of only stabbing downward.");
                Assert.That(contact.magnitude,Is.LessThan(.00001f),"Geometry commits exactly at the physical cut plane.");
            }
            Assert.That(approaches[0],Is.Not.EqualTo(approaches[1]));
            Assert.That(approaches[1],Is.Not.EqualTo(approaches[2]));
            Assert.That(approaches[0],Is.Not.EqualTo(approaches[2]));
        }

        [Test]
        public void OppositeCutAxis_KeepsTheHandleOnTheAuthoredSide_WithoutReversingThePull()
        {
            var host=new GameObject("Undirected neck cut fixture");
            try
            {
                var cooking=host.AddComponent(RuntimeReflection.RuntimeType("SashimiBoy.Stage03CookingView"));
                Quaternion rest=Quaternion.Euler(0,31,0);
                Vector3 forward=rest*Vector3.forward;
                var knife=new GameObject("Blade fixture").transform;knife.SetParent(host.transform,false);knife.rotation=rest;
                var grip=new GameObject("Hilt fixture").transform;grip.SetParent(knife,false);grip.localPosition=new Vector3(0,.05f,-.18f);
                var contact=new GameObject("Edge fixture").transform;contact.SetParent(knife,false);
                RuntimeReflection.SetField(cooking,"knifeRoot",knife);
                RuntimeReflection.SetField(cooking,"grip",grip);
                RuntimeReflection.SetField(cooking,"contact",contact);
                Call(cooking,"Awake");
                Assert.That(Call(cooking,"BeginStroke",0,Vector3.zero,forward,(Action)null,(Action)null),Is.EqualTo(true));
                Quaternion first=(Quaternion)RuntimeReflection.GetField(cooking,"strokeRotation");
                Call(cooking,"PoseKnife",.45f);
                Assert.That(grip.position.y,Is.GreaterThan(contact.position.y),"The hilt stays above the board during a forward cut.");
                Call(cooking,"CancelStroke");
                Assert.That(Call(cooking,"BeginStroke",0,Vector3.zero,-forward,(Action)null,(Action)null),Is.EqualTo(true));
                Quaternion reverse=(Quaternion)RuntimeReflection.GetField(cooking,"strokeRotation");
                Assert.That(Quaternion.Angle(first,reverse),Is.LessThan(.001f),
                    "A neck-cut line has no facing: reversing its mathematical axis must not put the handle across the fish and move Kevin behind his own shoulder.");
                Assert.That(Vector3.Distance((Vector3)RuntimeReflection.GetField(cooking,"sweepAxis"),-forward),Is.LessThan(.0001f),
                    "Keep the accepted draw direction; only the blade's facing is selected on the same cut line.");
                Call(cooking,"PoseKnife",.45f);
                Assert.That(grip.position.y,Is.GreaterThan(contact.position.y),"Drawing backwards must also keep the hilt above the cut plane.");
            }
            finally {UnityEngine.Object.DestroyImmediate(host);}
        }

        [Test]
        public void CookingStroke_ContactIsSingle_FreezeAndCancelDoNotDeliverLateActions()
        {
            var host=new GameObject("Stroke lifecycle fixture");
            try
            {
                var cooking=host.AddComponent(RuntimeReflection.RuntimeType("SashimiBoy.Stage03CookingView"));
                int contacts=0,finishes=0;
                Action contact=()=>contacts++,finished=()=>finishes++;
                Assert.That(Call(cooking,"BeginStroke",0,Vector3.zero,Vector3.right,contact,finished),Is.EqualTo(true));
                Assert.That(Call(cooking,"BeginStroke",1,Vector3.zero,Vector3.right,contact,finished),Is.EqualTo(false));
                Call(cooking,"AdvancePresentation",.18f);Assert.That(contacts,Is.Zero);
                float progress=Value<float>(cooking,"MotionProgress01");
                Call(cooking,"SetPaused",true);Call(cooking,"AdvancePresentation",20f);
                Assert.That(Value<float>(cooking,"MotionProgress01"),Is.EqualTo(progress));
                Assert.That(contacts,Is.Zero);Assert.That(finishes,Is.Zero);
                Call(cooking,"SetPaused",false);Call(cooking,"AdvancePresentation",1f);
                Assert.That(contacts,Is.EqualTo(1));Assert.That(finishes,Is.EqualTo(1));
                Call(cooking,"AdvancePresentation",1f);
                Assert.That(contacts,Is.EqualTo(1));Assert.That(finishes,Is.EqualTo(1));
                Assert.That(Call(cooking,"BeginStroke",5,Vector3.zero,Vector3.right,contact,finished),Is.EqualTo(true));
                Call(cooking,"ResetPresentation");Call(cooking,"AdvancePresentation",20f);
                Assert.That(Value<bool>(cooking,"IsStriking"),Is.False);
                Assert.That(contacts,Is.EqualTo(1));Assert.That(finishes,Is.EqualTo(1));
            }
            finally {UnityEngine.Object.DestroyImmediate(host);}
        }

        [Test]
        public void AnalyzedBeatPhase_UsesActualPulseTimes_AndRetainsOffsetInLegacyFallback()
        {
            var chart=ScriptableObject.CreateInstance(RuntimeReflection.RuntimeType("SashimiBoy.Stage03Chart"));
            try
            {
                RuntimeReflection.SetField(chart,"bpm",120f);
                RuntimeReflection.SetField(chart,"firstGridBeatSeconds",.2d);
                Assert.That(Call(chart,"BeatPhase01",200d),Is.EqualTo(0f));
                Assert.That((float)Call(chart,"BeatPhase01",450d),Is.EqualTo(.5f).Within(.0001f));
                RuntimeReflection.SetField(chart,"beatTimesMs",new[]{100d,720d,1360d});
                Assert.That(Call(chart,"BeatPhase01",720d),Is.EqualTo(0f));
                Assert.That((float)Call(chart,"BeatPhase01",1040d),Is.EqualTo(.5f).Within(.0001f));
                Assert.That((float)Call(chart,"BeatPhase01",1610d),Is.EqualTo(.5f).Within(.0001f));
            }
            finally {UnityEngine.Object.DestroyImmediate(chart);}
        }
    }
}
