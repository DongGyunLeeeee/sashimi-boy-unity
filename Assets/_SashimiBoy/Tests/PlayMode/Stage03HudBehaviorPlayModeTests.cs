using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SashimiBoy.Tests
{
    // Exercise visible HUD behavior, not just the cloned hierarchy. All inputs
    // and clock seeks here are synthetic and the default save is preserved.
    public sealed class Stage03HudBehaviorPlayModeTests
    {
        Component save,flow,controller,view,shared;
        object originalSave,originalAuto,originalPath;
        static object Get(object target,string field)=>RuntimeReflection.GetField(target,field);
        static void Set(object target,string field,object value)=>RuntimeReflection.SetField(target,field,value);
        static object Call(object target,string method,params object[] arguments)=>RuntimeReflection.Invoke(target,method,arguments);
        static T Value<T>(object target,string property)=>(T)Call(target,"get_"+property);
        static Component Find(string type)=>RuntimeReflection.FindActiveComponent("SashimiBoy."+type);
        object Run=>Value<object>(controller,"Run");
        object Clock=>Get(controller,"clock");
        object Chart=>Get(controller,"chart");
        object Phase=>((IList)Get(Chart,"phases"))[Value<int>(Run,"Phase")];
        double[] Notes=>(double[])Get(Phase,"noteTimesMs");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap");yield return null;
            save=Find("SaveManager");flow=Find("DayWorldFlow");
            originalSave=Get(save,"current");originalAuto=Get(save,"autoSaveOnChange");originalPath=Get(save,"validationSavePath");
            Call(save,"ConfigureValidationProfile","stage3-hud-"+Guid.NewGuid().ToString("N"));
            Set(save,"autoSaveOnChange",false);
            var current=RuntimeReflection.InvokeStatic("SashimiBoy.SaveData","CreateNew");Set(save,"current",current);
            var progress=Get(current,"dayWorld");Set(progress,"active",true);Set(progress,"day",3);Set(progress,"nightsSlept",2);
            Set(progress,"beat",Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"),"Work"));
            Call(current,"UnlockStage","STAGE_03_SOLE");Call(flow,"SetBusy",false);
            Call(Find("GameFlowManager"),"RequestStage","STAGE_03_SOLE");
            yield return SceneManager.LoadSceneAsync("Stage03_Flounder");yield return new WaitForSecondsRealtime(.2f);
            controller=Find("Stage03GameController");Assert.That(controller,Is.Not.Null);
            Call(controller,"ResetRun");Assert.That(Call(controller,"StartRun"),Is.EqualTo(true));
            view=(Component)Get(controller,"hudView");shared=(Component)Get(view,"shared");
        }

        [TearDown]
        public void TearDown()
        {
            if(controller!=null)Call(Clock,"Stop");
            if(save!=null){Set(save,"current",originalSave);Set(save,"autoSaveOnChange",originalAuto);Set(save,"validationSavePath",originalPath);}
            if(flow!=null)Call(flow,"SetBusy",false);
        }

        void SeekAndRefresh(double milliseconds)
        {
            Assert.That(Call(Clock,"PlayFromMilliseconds",Math.Max(0d,milliseconds)),Is.EqualTo(true));
            Call(view,"Refresh");
        }

        [UnityTest]
        public IEnumerator HitGauge_UsesBaselinePointLabelsWithoutChangingSeventyPercentHitGate()
        {
            double[] notes=Notes;int required=Value<int>(Run,"RequiredHits");
            Assert.That(required,Is.EqualTo((notes.Length*70+99)/100));
            Assert.That(Call(controller,"SubmitAt",notes[0]),Is.EqualTo(true));Call(view,"Refresh");
            Assert.That(((Text)Get(shared,"phaseQualityText")).text,Is.EqualTo("현재 1.00점"));
            Assert.That(((Text)Get(shared,"phaseRequirementText")).text,Is.EqualTo($"통과 {required:0.00} · 70%"));
            Assert.That(((Text)Get(shared,"phaseRemainingText")).text,Is.EqualTo("남은 노트 "+(notes.Length-1)));
            Assert.That(((Image)Get(shared,"phaseQualityFill")).fillAmount,Is.EqualTo(1f/notes.Length).Within(.0001f));
            Assert.That(((Text)Get(shared,"dialogueText")).text,Is.EqualTo("예고 노트가 절단선에 닿을 때 Space"));
            Assert.That(Value<bool>(Run,"PhaseSatisfied"),Is.False);yield return null;
        }

        [UnityTest]
        public IEnumerator ConsumedFinalNote_KeepsBaselineRestLaneVisibleWhileSectionWaits()
        {
            double[] notes=Notes;
            foreach(double note in notes)Assert.That(Call(controller,"SubmitAt",note),Is.EqualTo(true));
            SeekAndRefresh(notes[notes.Length-1]+200d);
            Assert.That(Value<int>(Run,"Phase"),Is.Zero,"HUD refresh cannot advance the musical section.");
            Assert.That(((GameObject)Get(shared,"focusHudRoot")).activeSelf,Is.True);
            Assert.That(((RectTransform)Get(view,"rhythmLane")).gameObject.activeSelf,Is.True);
            Assert.That(((Text)Get(view,"restPrompt")).gameObject.activeSelf,Is.True);
            Assert.That(((Text)Get(view,"restPrompt")).text,Is.EqualTo("휴식"));
            Assert.That(((GameObject)Get(view,"worldCue")).activeSelf,Is.False);
            foreach(Image dot in (Image[])Get(view,"noteDots"))Assert.That(dot.gameObject.activeSelf,Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpaceTutorial_EndsAtTwelfthSongNote_AndCursorSettlesBetweenBeats()
        {
            double[] notes=Notes;Assert.That(notes.Length,Is.GreaterThan(12));
            for(int i=0;i<11;i++)Assert.That(Call(controller,"SubmitAt",notes[i]),Is.EqualTo(true));
            SeekAndRefresh(notes[11]-80d);
            Assert.That(((Text)Get(view,"spacePrompt")).gameObject.activeSelf,Is.True);
            Assert.That(Call(controller,"SubmitAt",notes[11]),Is.EqualTo(true));SeekAndRefresh(notes[12]-80d);
            Assert.That(((Text)Get(view,"spacePrompt")).gameObject.activeSelf,Is.False);
            var beats=(double[])Get(Chart,"beatTimesMs");double beat=beats[10],period=beats[11]-beat;
            var cursor=((Image)Get(view,"hitCursor")).rectTransform;
            Call(view,"RefreshLanePresentation",beat,false);
            Assert.That(cursor.localScale.x,Is.EqualTo(1.18f).Within(.0001f));
            Call(view,"RefreshLanePresentation",beat+period*.25d,false);
            Assert.That(cursor.localScale,Is.EqualTo(Vector3.one),"Baseline cursor rests outside the .18-beat pulse window.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GhostForecast_FadesAndTapersLikeBaseline_AndCannotInventUpcomingNotes()
        {
            while(Value<int>(Run,"Phase")<6)
            {
                foreach(double note in Notes){Assert.That(Call(controller,"SubmitAt",note),Is.EqualTo(true));Call(controller,"AdvancePresentation",.3f);}
                double deadline=(double)Get(Phase,"endTimeMs");int previous=Value<int>(Run,"Phase");
                for(int attempt=0;attempt<4&&Value<int>(Run,"Phase")==previous;attempt++)
                {
                    Call(controller,"AdvancePresentation",60f);Call(Get(controller,"assembly"),"AdvancePresentation",1f);
                    Call(controller,"TickAt",deadline+1d);
                }
                Assert.That(Value<int>(Run,"Phase"),Is.EqualTo(previous+1));
            }
            var ghosts=(Transform[])Get(view,"ghostLines");var renderers=(Renderer[])Get(view,"ghostRenderers");
            var bases=(Vector3[])Get(view,"ghostBaseScales");double[] notes=Notes;
            SeekAndRefresh(notes[0]-80d);
            Assert.That(ghosts[0].gameObject.activeSelf,Is.True);
            Assert.That(ghosts[0].localScale,Is.EqualTo(bases[0]*.92f));
            Assert.That(ghosts[ghosts.Length-1].localScale,Is.EqualTo(bases[ghosts.Length-1]*.66f));
            var block=new MaterialPropertyBlock();renderers[0].GetPropertyBlock(block);Color color=block.GetColor("_BaseColor");
            Assert.That(color.b,Is.EqualTo(.78f).Within(.0001f));Assert.That(color.a,Is.EqualTo(1f));
            // Keep all physical fillet cuts available but leave only one real
            // musical note: forecasting three further cuts would be misleading.
            Set(controller,"<NoteCursor>k__BackingField",notes.Length-1);SeekAndRefresh(notes[notes.Length-1]-80d);
            foreach(Transform ghost in ghosts)Assert.That(ghost.gameObject.activeSelf,Is.False);
            Assert.That(((GameObject)Get(view,"worldCue")).activeSelf,Is.True);yield return null;
        }
    }
}
