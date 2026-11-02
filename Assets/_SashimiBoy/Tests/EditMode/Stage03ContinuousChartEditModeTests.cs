using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class Stage03ContinuousChartEditModeTests
    {
        const string JsonPath="Docs/Development/STAGE03_RHYTHM_ANALYSIS.json";
        const string ChartPath="Assets/_SashimiBoy/Data/Generated/Stage03PlayableChart.asset";
        const string MusicPath="Assets/_SashimiBoy/Audio/Music/Stage_03_Flounder/flounder1.wav";
        ScriptableObject chart;
        static object Get(object target,string field)=>RuntimeReflection.GetField(target,field);
        static void Set(object target,string field,object value)=>RuntimeReflection.SetField(target,field,value);
        static object Call(object target,string method,params object[] args)=>RuntimeReflection.Invoke(target,method,args);
        IList Phases=>(IList)Get(chart,"phases");
        static double[] Notes(object phase)=>(double[])Get(phase,"noteTimesMs");

        [SetUp]
        public void SetUp()
        {
            chart=ScriptableObject.CreateInstance(RuntimeReflection.RuntimeType("SashimiBoy.Stage03Chart"));
            Set(chart,"music",AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath));
            string root=Directory.GetParent(Application.dataPath).FullName;
            JsonUtility.FromJsonOverwrite(File.ReadAllText(Path.Combine(root,JsonPath)),chart);
        }

        [TearDown]
        public void TearDown()=>UnityEngine.Object.DestroyImmediate(chart);

        void Invalid()
        {
            var error=Assert.Throws<TargetInvocationException>(()=>Call(chart,"Validate"));
            Assert.That(error.InnerException,Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void GeneratedChart_UsesTheAnalyzedDenseSchedule_AndPhysicalTransitionDeadlines()
        {
            Assert.DoesNotThrow(()=>Call(chart,"Validate"));
            var generated=AssetDatabase.LoadAssetAtPath(ChartPath,RuntimeReflection.RuntimeType("SashimiBoy.Stage03Chart"));
            Assert.That(generated,Is.Not.Null,"Run the authoritative Stage03PlayableAuthoring generator.");
            Assert.DoesNotThrow(()=>Call(generated,"Validate"));
            var saved=(IList)Get(generated,"phases");
            Assert.That(saved.Count,Is.EqualTo(8));
            var all=Phases.Cast<object>().SelectMany(Notes).ToArray();
            Assert.That(all.Length,Is.InRange(280,350),"The prior sparse 62-note chart is not the requested hard chart.");
            var gaps=all.Skip(1).Zip(all,(next,previous)=>next-previous).ToArray();
            Assert.That(gaps.Min(),Is.GreaterThanOrEqualTo(280d));
            Assert.That(gaps.Count(gap=>gap<400d),Is.GreaterThanOrEqualTo(35),"Measured eighth-note phrases must be playable, not just quarter pulses.");
            Assert.That(gaps.Count(gap=>gap>500d&&gap<750d),Is.GreaterThanOrEqualTo(100));
            for(int phase=0;phase<8;phase++)
            {
                Assert.That(Notes(saved[phase]),Is.EqualTo(Notes(Phases[phase])),"phase "+phase);
                Assert.That(Get(saved[phase],"requiredHits"),Is.EqualTo(Get(Phases[phase],"requiredHits")));
                Assert.That(Get(saved[phase],"endTimeMs"),Is.EqualTo(Get(Phases[phase],"endTimeMs")));
            }
            Assert.That(Get(generated,"music"),Is.SameAs(Get(chart,"music")));
            Assert.That(Get(generated,"sourceSha256"),Is.EqualTo(Stage03AudioRevisionContract.CurrentHash));
        }

        [Test]
        public void Turnover_IsOneScoredPulse_AndTwoFilletsCanFinishTwelveSlicesFromActualSuccessfulNotes()
        {
            Assert.That(Phases.Count,Is.EqualTo(8));
            Assert.That(Notes(Phases[3]).Length,Is.EqualTo(1));
            Assert.That(Get(Phases[3],"requiredHits"),Is.EqualTo(1));
            Assert.That(Notes(Phases[3])[0],Is.GreaterThan((double)Get(Phases[2],"endTimeMs")+250d));
            Assert.That(Notes(Phases[4])[0],Is.GreaterThan((double)Get(Phases[3],"endTimeMs")+250d));
            foreach(int phase in new[]{6,7})
            {
                int count=Notes(Phases[phase]).Length;
                int required=(int)Get(Phases[phase],"requiredHits");
                Assert.That(required,Is.EqualTo((count*7+9)/10));
                Assert.That(required,Is.GreaterThanOrEqualTo(12));
            }
        }

        [TestCase(1,1)]
        [TestCase(10,7)]
        [TestCase(11,8)]
        [TestCase(17,12)]
        [TestCase(30,21)]
        public void SeventyPercentGate_RoundsUpWithoutFloatingPointBoundaryErrors(int notes,int required)
        {
            Assert.That(RuntimeReflection.InvokeStatic("SashimiBoy.Stage03Chart","RequiredHitsFor",notes),Is.EqualTo(required));
        }

        [Test]
        public void Validator_RejectsDenseOverlappingWindows_AndMissingPhysicalContactTail()
        {
            var notes=Notes(Phases[0]);
            double old=notes[1];
            notes[1]=notes[0]+249d;
            Invalid();
            notes[1]=old;
            Set(Phases[0],"endTimeMs",notes.Last()+500d);
            Invalid();
        }

        [Test]
        public void Validator_RejectsOldThresholdOrSource_AndAnExtraUnscoredTurnOpportunity()
        {
            int required=(int)Get(Phases[0],"requiredHits");
            Set(Phases[0],"requiredHits",4);
            Invalid();
            Set(Phases[0],"requiredHits",required);
            string source=(string)Get(chart,"sourceSha256");
            Set(chart,"sourceSha256",new string('0',64));
            Invalid();
            Set(chart,"sourceSha256",source);
            double turn=Notes(Phases[3])[0];
            Set(Phases[3],"noteTimesMs",new[]{turn,turn+600d});
            Set(Phases[3],"requiredHits",2);
            Set(Phases[3],"endTimeMs",turn+1600d);
            Invalid();
        }
    }
}
