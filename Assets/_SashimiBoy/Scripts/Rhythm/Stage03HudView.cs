using System;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    // Reads Stage3 state into the exact Stage1/2 visual hierarchy. Judgement and
    // progression remain owned by Stage03GameController, never the legacy timing.
    [DefaultExecutionOrder(500)]
    public sealed class Stage03HudView : MonoBehaviour
    {
        public Stage03GameController controller;
        public Stage01SalmonHUD shared;
        public RectTransform rhythmLane;
        public Image hitCursor;
        public Image[] noteDots=Array.Empty<Image>();
        public Text spacePrompt,nowPrompt,restPrompt;
        public GameObject startRoot,pauseRoot,failureRoot;
        public Text startText,pauseText,failureText;
        public GameObject worldCue;
        public Renderer cutGuide;
        public Transform leftBracket,rightBracket,forecastLeftBracket,forecastRightBracket;
        public Transform[] ghostLines=Array.Empty<Transform>();
        public Renderer[] ghostRenderers=Array.Empty<Renderer>();
        public float wideBracketDistance=.72f,closedBracketDistance=.08f;
        public float laneHorizonBeats=4f,nowDuration=.12f,restThresholdBeats=1.35f;
        private Renderer[] cueRenderers=Array.Empty<Renderer>();
        private Vector3 guideScale;
        private MaterialPropertyBlock colors;
        private float flashUntil;
        private void Awake()
        {
            if(shared!=null){shared.enabled=false;shared.Bind(null,null);}
            colors=new MaterialPropertyBlock();
            if(worldCue!=null)cueRenderers=worldCue.GetComponentsInChildren<Renderer>(true);
            if(cutGuide!=null)guideScale=cutGuide.transform.localScale;
        }
        private void LateUpdate()=>Refresh();
        public void ResetForRetry()
        {
            if(shared==null)return;
            shared.ResetForRetry();flashUntil=0;
            if(shared.screenFlash!=null)shared.screenFlash.color=Color.clear;
            HideCues();
        }
        public void ShowJudgement(JudgeGrade grade,double offsetMs)
        {
            string word=grade==JudgeGrade.Nasty?"NASTY":grade==JudgeGrade.Smooth?"CLEAN":grade==JudgeGrade.Slipped?"SLIPPED":"WHACK";
            Color color=grade==JudgeGrade.Nasty?new Color(.45f,1f,.73f):grade==JudgeGrade.Smooth?new Color(.5f,.9f,1f):new Color(1f,.63f,.35f);
            shared.ShowReadableJudgement(word+"  "+offsetMs.ToString("+0;-0;0")+"ms",color);
            Flash(grade);
        }
        public void ShowStatus(string label,Color color)
        {
            shared.ShowReadableJudgement(label=="놓침"?"MISS · 놓침":label,color);
            if(label=="놓침")Flash(JudgeGrade.Whack);
        }
        public void ShowFailure(string message)
        {Set(failureText,message+"\n\n완료한 부위는 유지됩니다");}
        private void Flash(JudgeGrade grade)
        {
            if(shared.screenFlash==null)return;
            shared.screenFlash.color=grade==JudgeGrade.Nasty?new Color(1,1,1,.18f):
                grade==JudgeGrade.Smooth?new Color(.55f,.92f,1,.10f):
                grade==JudgeGrade.Slipped?new Color(1,.65f,.2f,.1f):new Color(1,.04f,.02f,.2f);
            flashUntil=Time.unscaledTime+(grade==JudgeGrade.Whack ? .16f : .1f);
        }
        public void Refresh()
        {
            if(controller==null||shared==null||controller.chart==null)return;
            if(shared.screenFlash!=null&&Time.unscaledTime>=flashUntil)shared.screenFlash.color=Color.clear;
            var run=controller.Run;
            bool hidden=run.Completed||controller.ResultShown||controller.Paused||run.Failed;
            if(shared.focusHudRoot!=null)shared.focusHudRoot.SetActive(!hidden);
            if(hidden)
            {
                HideCues();Set(shared.countdownText,"");Set(shared.dialogueText,"");return;
            }
            var phase=controller.chart.phases[run.Phase];
            Set(shared.fishProgressText,phase.label+"  |  회 "+controller.SpawnedSlices+" / 24");
            Set(shared.scoreText,$"점수  {controller.Score:N0}");
            Set(shared.comboText,$"콤보  {controller.Combo}");
            Set(shared.phaseQualityText,$"현재 {run.Hits:0.00}점");
            float threshold=run.RequiredHits/(float)phase.noteTimesMs.Length;
            Set(shared.phaseRequirementText,$"통과 {run.RequiredHits:0.00} · {threshold*100:0}%");
            Set(shared.phaseRemainingText,run.PhaseSatisfied?"손질 완료":"남은 노트 "+Mathf.Max(0,phase.noteTimesMs.Length-controller.NoteCursor));
            if(shared.phaseQualityFill!=null)
            {
                shared.phaseQualityFill.fillAmount=run.Hits/(float)phase.noteTimesMs.Length;
                shared.phaseQualityFill.color=run.PhaseSatisfied?new Color(.32f,.9f,.67f):new Color(1f,.69f,.23f);
            }
            if(shared.phaseThresholdMarker!=null)
            {
                shared.phaseThresholdMarker.anchorMin=shared.phaseThresholdMarker.anchorMax=new Vector2(threshold,.5f);
                shared.phaseThresholdMarker.anchoredPosition=Vector2.zero;
            }
            double now=controller.clock.SongTimeMs;
            double beat=60000d/controller.chart.bpm;
            double first=controller.chart.phases[0].noteTimesMs[0];
            string countdown="";
            if(controller.Started&&run.Phase==0&&now>=first-beat*4&&now<first)
                countdown=Mathf.Clamp(Mathf.CeilToInt((float)((first-now)/beat)),1,4).ToString();
            else if(run.Phase==0&&run.Hits==0&&now>=first&&now<first+320)countdown="SLICE!";
            Set(shared.countdownText,countdown);
            if(run.WaitingForTurn)
            {
                HideCues();Show(spacePrompt,controller.assembly.IsTurningOver?"":"[ E ]",!controller.assembly.IsTurningOver);
                Set(shared.dialogueText,controller.assembly.IsTurningOver?"생선을 뒤집고 있습니다":"앞면 필렛 분리 완료 · E를 눌러 뒤집기");return;
            }
            Set(shared.dialogueText,run.PhaseSatisfied?"이 구간 완료 · 다음 손질을 준비하세요":"예고 노트가 절단선에 올 때 Space");
            double next=controller.NextNoteMs;
            bool running=controller.Started&&controller.clock.IsRunning;
            if(!running||next<0){HideCues();return;}
            double delta=next-now,horizon=beat*laneHorizonBeats;
            bool resting=delta>beat*restThresholdBeats;
            rhythmLane.gameObject.SetActive(true);
            int count=0;
            float travel=Mathf.Max(120,rhythmLane.rect.width*.43f);
            for(int i=0;i<noteDots.Length;i++)
            {
                int ordinal=controller.NoteCursor+i;
                double ahead=ordinal<phase.noteTimesMs.Length?phase.noteTimesMs[ordinal]-now:double.MaxValue;
                bool visible=ahead>=-nowDuration*1000&&ahead<=horizon;
                var dot=noteDots[i];if(dot==null)continue;dot.gameObject.SetActive(visible);
                if(!visible)continue;
                dot.rectTransform.anchoredPosition=new Vector2(Mathf.Clamp01((float)(ahead/horizon))*travel,0);
                dot.rectTransform.localScale=Vector3.one*(i==0?1.35f:Mathf.Max(.72f,1-i*.1f));
                float alpha=(i==0?1:Mathf.Max(.2f,.62f-i*.14f))*(resting ? .58f : 1);
                dot.color=i==0?new Color(1,.82f,.24f,alpha):new Color(.36f,.92f,1,alpha);count++;
            }
            float beatPulse=Mathf.Pow(1-Mathf.Clamp01((float)Math.Abs(now/beat-Math.Round(now/beat))*2),3);
            if(hitCursor!=null)
            {
                hitCursor.color=resting?new Color(.35f,.82f,.92f,Mathf.Lerp(.2f,.5f,beatPulse)):
                    new Color(1,.78f,.24f,Mathf.Lerp(.72f,1,beatPulse));
                hitCursor.rectTransform.localScale=new Vector3(1+beatPulse*.18f,1+beatPulse*.35f,1);
            }
            var background=rhythmLane.GetComponent<Image>();
            if(background!=null)background.color=resting?new Color(.025f,.04f,.05f,.62f):new Color(.035f,.055f,.065f,.92f);
            bool pulse=delta<=0&&delta>=-nowDuration*1000;
            Show(spacePrompt,"[ SPACE ]",!resting);Show(nowPrompt,"지금!",!resting&&pulse);Show(restPrompt,"휴식",resting);
            int following=controller.NoteCursor+1;
            double followingDelta=following<phase.noteTimesMs.Length?phase.noteTimesMs[following]-now:delta+beat;
            RefreshWorld(!resting&&count>0,Mathf.Clamp01(1-(float)(delta/(beat*2))),
                Mathf.Clamp01(1-(float)(followingDelta/(beat*2))),pulse);
        }
        private void RefreshWorld(bool visible,float approach,float followingApproach,bool pulse)
        {
            if(worldCue==null)return;
            worldCue.SetActive(visible);if(!visible)return;
            worldCue.transform.position=CutPoint(0);
            float smooth=approach*approach*(3-2*approach);
            PoseBracket(leftBracket,rightBracket,smooth,1);
            float followingSmooth=followingApproach*followingApproach*(3-2*followingApproach);
            PoseBracket(forecastLeftBracket,forecastRightBracket,followingSmooth,1.16f);
            if(cutGuide!=null)
            {
                float emphasis=1+smooth*.18f+(pulse ? .52f : 0);
                cutGuide.transform.localScale=new Vector3(guideScale.x*emphasis,guideScale.y*emphasis,guideScale.z);
            }
            Color color=pulse?new Color(1,.95f,.48f):new Color(.35f,.92f,1);
            foreach(var renderer in cueRenderers)Paint(renderer,color);
            for(int i=0;i<ghostLines.Length;i++)
            {
                bool show=controller.Run.Phase>=5&&controller.fillets[controller.Run.Phase-5].CutCount+i+1<6;
                if(ghostLines[i]==null)continue;ghostLines[i].gameObject.SetActive(show);
                if(show)ghostLines[i].position=CutPoint(i+1);
                if(show&&i<ghostRenderers.Length)Paint(ghostRenderers[i],new Color(.35f,.92f,1,Mathf.Max(.2f,.62f-i*.14f)));
            }
        }
        private Vector3 CutPoint(int offset)
        {
            if(controller.Run.Phase<5)return controller.PreparationWorkPoint(controller.Run.Phase)+Vector3.up*.004f;
            var surface=controller.fillets[controller.Run.Phase-5];
            var bounds=surface.cutCache.remainingMeshes[0].bounds;
            float z=surface.cutCache.cutPositions[Mathf.Clamp(surface.CutCount+offset,0,5)];
            return surface.meshFilter.transform.TransformPoint(new Vector3(bounds.center.x,bounds.max.y,z))+Vector3.up*.004f;
        }
        private void PoseBracket(Transform left,Transform right,float approach,float multiplier)
        {
            float distance=Mathf.Lerp(wideBracketDistance,closedBracketDistance,approach)*multiplier;
            if(left!=null)left.localPosition=new Vector3(-distance,0,0);
            if(right!=null)right.localPosition=new Vector3(distance,0,0);
        }
        private void Paint(Renderer renderer,Color value)
        {
            if(renderer==null)return;
            colors.SetColor("_Color",value);colors.SetColor("_BaseColor",value);renderer.SetPropertyBlock(colors);
        }
        private void HideCues()
        {
            if(worldCue!=null)worldCue.SetActive(false);
            if(rhythmLane!=null)rhythmLane.gameObject.SetActive(false);
            foreach(var dot in noteDots)if(dot!=null)dot.gameObject.SetActive(false);
            Show(spacePrompt,"",false);Show(nowPrompt,"",false);Show(restPrompt,"",false);
        }
        private static void Set(Text text,string value){if(text!=null)text.text=value;}
        private static void Show(Text text,string value,bool visible){if(text!=null){text.text=value;text.enabled=visible;}}
    }
}
