using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class Stage03GameplayEditModeTests
    {
        static object NewRun()
        {
            var run=Activator.CreateInstance(RuntimeReflection.RuntimeType("SashimiBoy.Stage03RunState"));
            Call(run,"Configure",new[]{20,20,20,1,20,20,20,20});
            return run;
        }
        static object Call(object target, string method, params object[] args) => RuntimeReflection.Invoke(target, method, args);
        static T Value<T>(object target, string property) => (T)Call(target, "get_" + property);
        static void ResolvePhase(object run,bool allHits=false)
        {
            int needed = Value<int>(run, "RequiredHits");
            int count=Value<int>(run,"NoteCount");
            for(int i=0;i<count;i++)
            {
                Assert.That(Call(run,i<needed||allHits?"TryHit":"TryMiss"),Is.EqualTo(true));
                if(i<count-1)Assert.That(Call(run,"TryAdvance"),Is.EqualTo(false),"The musical section must resolve every note, even after its work quota.");
            }
            Assert.That(Call(run,"TryHit"),Is.EqualTo(false));
            Assert.That(Call(run,"TryMiss"),Is.EqualTo(false));
        }
        static void ReachFirstSlicingPhase(object run)
        {
            for (int phase = 0; phase < 6; phase++)
            {
                Assert.That(Value<int>(run, "Phase"), Is.EqualTo(phase));
                ResolvePhase(run);
                Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true));
                Assert.That(Value<bool>(run,"Turned"),Is.EqualTo(phase>=3));
                Assert.That(Value<bool>(run,"WaitingForTurn"),Is.False,"Turnover is resolved by its rhythm note, never an E wait state.");
            }
        }

        [Test]
        public void Run_ResolvesEightSections_WithRhythmicTurnAndExactlyTwoTimesTwelveSlices()
        {
            var run = NewRun();
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            ReachFirstSlicingPhase(run);
            Assert.That(Value<int>(run, "TotalSlices"), Is.Zero);
            for (int fillet = 0; fillet < 2; fillet++)
            {
                ResolvePhase(run);
                Assert.That(Value<int[]>(run,"Slices")[fillet],Is.EqualTo(12));
                Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true));
            }
            Assert.That(Value<bool>(run, "Completed"), Is.True);
            Assert.That(Value<int>(run, "TotalSlices"), Is.EqualTo(24));
            Assert.That(Value<int[]>(run, "Slices"), Is.EqualTo(new[] { 12, 12 }));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Call(run, "Fail");
            Assert.That(Value<bool>(run, "Failed"), Is.False, "A finished plate cannot be converted into failure.");
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(false));
        }

        [Test]
        public void FailedSecondFillet_RetryResetsWholeStageIncludingEarlierSlicesAndTurn()
        {
            var run = NewRun();
            ReachFirstSlicingPhase(run);
            ResolvePhase(run);
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true));
            for (int i = 0; i < 3; i++) Assert.That(Call(run, "TryHit"), Is.EqualTo(true));
            Call(run, "Fail");
            Assert.That(Value<int[]>(run, "Slices"), Is.EqualTo(new[] { 12, 2 }));
            Assert.That(Call(run, "TryHit"), Is.EqualTo(false));
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(true));
            Assert.That(Value<int[]>(run, "Slices"), Is.EqualTo(new[] { 0, 0 }));
            Assert.That(Value<int>(run, "Phase"), Is.Zero);
            Assert.That(Value<bool>(run, "Turned"), Is.False);
            Assert.That(Call(run, "RetryPhase"), Is.EqualTo(false));
            for (int i = 0; i < 8; i++) { ResolvePhase(run); Assert.That(Call(run, "TryAdvance"), Is.EqualTo(true)); }
            Assert.That(Value<int>(run, "TotalSlices"), Is.EqualTo(24));
            Call(run, "Reset");
            Assert.That(Value<int>(run, "Phase"), Is.Zero);
            Assert.That(Value<int>(run, "TotalSlices"), Is.Zero);
            Assert.That(Value<bool>(run, "Turned"), Is.False);
            Assert.That(Value<bool>(run, "WaitingForTurn"), Is.False);
        }

        [Test]
        public void QuotaReachedEarly_StillResolvesAndAcceptsEveryRemainingNote()
        {
            var run = NewRun();
            for(int i=0;i<14;i++)Assert.That(Call(run,"TryHit"),Is.EqualTo(true));
            Assert.That(Value<bool>(run,"PhaseSatisfied"),Is.True);
            Assert.That(Call(run, "TryAdvance"), Is.EqualTo(false));
            for(int i=0;i<6;i++)Assert.That(Call(run,"TryHit"),Is.EqualTo(true));
            Assert.That(Value<int>(run,"Hits"),Is.EqualTo(20));
            Assert.That(Value<int>(run,"ResolvedNotes"),Is.EqualTo(20));
            Assert.That(Call(run,"TryAdvance"),Is.EqualTo(true));
        }

        [Test]
        public void BelowSeventyPercent_CannotAdvance_AndMissedTurnCannotBecomeCompleted()
        {
            var run=NewRun();
            for(int i=0;i<13;i++)Call(run,"TryHit");
            for(int i=0;i<7;i++)Call(run,"TryMiss");
            Assert.That(Value<int>(run,"ResolvedNotes"),Is.EqualTo(20));
            Assert.That(Call(run,"TryAdvance"),Is.EqualTo(false));
            Call(run,"Fail");Call(run,"RetryPhase");
            for(int p=0;p<3;p++){ResolvePhase(run);Call(run,"TryAdvance");}
            Assert.That(Call(run,"TryMiss"),Is.EqualTo(true));
            Assert.That(Call(run,"TryAdvance"),Is.EqualTo(false));
            Assert.That(Value<bool>(run,"Turned"),Is.False);
        }

        [Test]
        public void CookingStroke_UsesThreePullingPaths_AndMeetsTheCutPlaneAtContact()
        {
            string type="SashimiBoy.Stage03CookingView";
            Vector3[] approaches=new Vector3[3];int[] phases={0,1,6};
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
                Call(cooking,"AdvancePresentation",.04f);Assert.That(contacts,Is.Zero);
                float progress=Value<float>(cooking,"MotionProgress01");
                Call(cooking,"SetPaused",true);Call(cooking,"AdvancePresentation",20f);
                Assert.That(Value<float>(cooking,"MotionProgress01"),Is.EqualTo(progress));
                Assert.That(contacts,Is.Zero);Assert.That(finishes,Is.Zero);
                Call(cooking,"SetPaused",false);Call(cooking,"AdvancePresentation",1f);
                Assert.That(contacts,Is.EqualTo(1));Assert.That(finishes,Is.EqualTo(1));
                Call(cooking,"AdvancePresentation",1f);
                Assert.That(contacts,Is.EqualTo(1));Assert.That(finishes,Is.EqualTo(1));
                Assert.That(Call(cooking,"BeginStroke",6,Vector3.zero,Vector3.right,contact,finished),Is.EqualTo(true));
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
