using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace SashimiBoy
{
    public sealed class Stage03GameController : MonoBehaviour
    {
        public Stage03Chart chart;
        public AudioClock clock;
        public FlounderAssemblyView assembly;
        public Stage03FilletSurface[] fillets;
        public Stage03CookingView cooking;
        public Stage03HudView hudView;
        public bool autoStart=true;
        public Transform sliceRoot;
        public Transform[] plateSlots;
        public float boardTop;
        public float sliceUniformScale = .6f;
        public Text titleText,statusText,noteText,judgementText;
        public RectTransform noteMarker,noteLane;
        public GameObject startPanel,pausePanel,failurePanel,resultPanel;
        public Text resultText;
        public Image songFill;
        public Stage03RunState Run {get;}=new Stage03RunState();
        public int Score {get;private set;}
        public int Combo {get;private set;}
        public int MaxCombo {get;private set;}
        public int MissCount {get;private set;}
        public float Accuracy01=>Mathf.Clamp01(Score/(1000f*Mathf.Max(1,hitAttempts+MissCount)));
        public bool Started {get;private set;}
        public bool Paused {get;private set;}
        public bool ResultShown {get;private set;}
        public bool ClearRecorded {get;private set;}
        public int NoteCursor {get;private set;}
        public int SpawnedSlices => slices.Count;
        public int PendingVisualCuts=>visualCuts.Count+(activeVisualCut!=null?1:0);
        public Vector3 NextWorkContactPoint=>Run.Completed?Vector3.zero:CutContactPoint(Run.Phase,Run.Hits);
        public double NextNoteMs => !Started||Run.Completed||Run.Failed||
            NoteCursor>=chart.phases[Run.Phase].noteTimesMs.Length ? -1d : chart.phases[Run.Phase].noteTimesMs[NoteCursor];
        private readonly List<GameObject> slices=new List<GameObject>();
        private readonly List<int> sliceOwners=new List<int>();
        private sealed class VisualCut
        {
            public int generation,phase,ordinal,sliceTarget;
            public bool changesPreparation,finishing,completesPhase;
            public Vector3 point,direction;
            public bool committed;
        }
        private readonly Queue<VisualCut> visualCuts=new Queue<VisualCut>();
        private VisualCut activeVisualCut;
        private int presentationGeneration;
        private bool phaseCompletionQueued,phaseCompletionCommitted;
        private Vector3 fishLongAxisLocal=Vector3.right;
        private Bounds lastPreparationBounds;
        private bool hasPreparationBounds;
        private int phaseBaseScore;
        private int hitAttempts;
        private int preparedFillet=-1;
        private bool returning;
        private bool clearApplied;
        private float resultDelay=-1f;
        private float judgeTimer;
        private float WindowMs=>RhythmJudge.GetMaxWindowMs(chart.bpm,RhythmDifficulty.Casual);
        private float Offset=>SaveManager.Instance!=null?SaveManager.Instance.Current.globalJudgeOffsetMs:0f;
        private void Start()
        {
            chart.Validate();Run.Configure(chart.phases.Select(p=>p.noteTimesMs.Length).ToArray());assembly.BeginTwoFilletPreparation();assembly.enabled=false;
            if(fillets==null||fillets.Length!=2||plateSlots==null||plateSlots.Length!=24)
                throw new InvalidOperationException("Stage3 needs two surfaces and twenty-four plate slots.");
            ResetRun();
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            if(GameFlowManager.Instance!=null)GameFlowManager.Instance.SetLocation(GameLocation.FishStage);
            if(autoStart)StartRun();
        }
        private void Update()
        {
            cooking.SetPaused(Paused||Run.Failed||!Started||returning||(!clock.IsRunning&&clock.State!=AudioClockState.Finished));
            if(!Started){if(Input.GetKeyDown(KeyCode.Return))StartRun();RefreshHUD();return;}
            if(ResultShown){if(Input.GetKeyDown(KeyCode.F))ReturnToShop();RefreshHUD();return;}
            if(Input.GetKeyDown(KeyCode.Escape))TogglePause();
            if(Paused){RefreshHUD();return;}
            if(Run.Failed){if(Input.GetKeyDown(KeyCode.R))RetryPhase();RefreshHUD();return;}
            if(resultDelay>=0){resultDelay-=Time.deltaTime;if(resultDelay<=0)ShowResult();RefreshHUD();return;}
            if(clock.IsRunning||clock.State==AudioClockState.Finished)AdvancePresentation(Time.deltaTime);
            if(clock.State==AudioClockState.Faulted){Fail("음원 재생을 확인한 뒤 스테이지를 다시 시작하세요.");RefreshHUD();return;}
            if(!clock.IsRunning&&clock.State!=AudioClockState.Finished){RefreshHUD();return;}
            TickAt(clock.SongTimeMs);
            if(clock.IsRunning&&Input.GetKeyDown(KeyCode.Space))SubmitAt(clock.SongTimeMs);
            RefreshHUD();
        }
        public bool StartRun()
        {
            if(Started||returning)return false;
            Started=true;startPanel.SetActive(false);
            if(!clock.Play()){Fail("음원을 재생하지 못했습니다.");return false;}
            cooking.SetPaused(false);
            return true;
        }
        public void StartFromButton()=>StartRun();
        public void RetryFromButton()=>RetryPhase();
        public void ResetRun()
        {
            CancelPendingCuts();
            cooking.ResetPresentation();
            clock.Stop();Run.Reset();Score=Combo=MaxCombo=NoteCursor=phaseBaseScore=MissCount=hitAttempts=0;
            phaseCompletionQueued=phaseCompletionCommitted=false;
            Started=Paused=ResultShown=ClearRecorded=returning=clearApplied=false;resultDelay=-1;preparedFillet=-1;
            for(int i=0;i<fillets.Length;i++){fillets[i].ResetCuts();fillets[i].SetCuttingEnabled(false);}
            assembly.BeginTwoFilletPreparation();hasPreparationBounds=false;
            GroundBody();
            Bounds whole=PreparationBodyBounds(0);
            Vector3 longAxis=whole.size.x>=whole.size.z?Vector3.right:Vector3.forward;
            fishLongAxisLocal=assembly.transform.InverseTransformDirection(longAxis);
            foreach(var slice in slices)if(slice!=null)Destroy(slice);
            slices.Clear();sliceOwners.Clear();
            startPanel.SetActive(true);pausePanel.SetActive(false);failurePanel.SetActive(false);resultPanel.SetActive(false);
            if(judgementText!=null)judgementText.text="";
            if(hudView!=null)hudView.ResetForRetry();
            PreparePhase();
        }
        public void TickAt(double nowMs)
        {
            if(!Started||Paused||Run.Failed||Run.Completed||resultDelay>=0||
               (!clock.IsRunning&&clock.State!=AudioClockState.Finished)||double.IsNaN(nowMs)||double.IsInfinity(nowMs))return;
            var phase=chart.phases[Run.Phase];
            while(NoteCursor<phase.noteTimesMs.Length&&nowMs-phase.noteTimesMs[NoteCursor]-Offset>WindowMs)
            {
                Run.TryMiss();MissCount++;Combo=0;ShowJudge("놓침",Color.red);NoteCursor++;
            }
            if(nowMs<phase.endTimeMs+Offset)return;
            if(!Run.PhaseSatisfied){Fail("구간 성공률이 70% 미만입니다. R로 스테이지 전체를 다시 시작하세요.");return;}
            if(PendingVisualCuts>0||PartsAreMoving())return;
            // A quota earns completion; it never changes the fish before the
            // musical section ends. Use the authored transition gap for the
            // final contact, keeping audio and the remaining notes continuous.
            if(!phaseCompletionQueued)
            {
                phaseCompletionQueued=true;
                var completion=new VisualCut{generation=presentationGeneration,phase=Run.Phase,
                    ordinal=Mathf.Max(0,Run.Hits-1),sliceTarget=Run.Phase>=6?Run.Slices[Run.Phase-6]:0,
                    changesPreparation=Run.Phase<6,finishing=false,
                    point=CutContactPoint(Run.Phase,Mathf.Max(0,Run.Hits-1)),direction=CutDirection(Run.Phase)};
                completion.completesPhase=true;
                visualCuts.Enqueue(completion);StartNextStroke();return;
            }
            if(!phaseCompletionCommitted){Fail("구간 완료 동작을 확인해 주세요.");return;}
            if(Run.Phase==3&&!assembly.IsBackFacing){Fail("뒤집기 노트를 성공해야 합니다.");return;}
            if(!Run.TryAdvance()){Fail("손질 구간의 노트 처리를 확인해 주세요.");return;}
            phaseCompletionQueued=phaseCompletionCommitted=false;
            NoteCursor=0;phaseBaseScore=Score;
            if(Run.Completed)
            {
                SetSurfacesEnabled(false);
                Vector3 focus=Vector3.zero;foreach(var slot in plateSlots)focus+=slot.position;
                cooking.BeginPlatePresentation(focus/plateSlots.Length);resultDelay=2f;
            }
            else PreparePhase();
        }
        public bool SubmitAt(double inputMs)
        {
            if(!Started||Paused||ResultShown||Run.Failed||Run.Completed||!clock.IsRunning||
               double.IsNaN(inputMs)||double.IsInfinity(inputMs))return false;
            TickAt(inputMs); // Expire missed notes before judging the next one.
            if(Run.Failed||Run.Completed||NextNoteMs<0)return false;
            double adjusted=inputMs-NextNoteMs-Offset;
            if(adjusted < -WindowMs || adjusted>WindowMs)return false;
            var judged=RhythmJudge.Judge(chart.bpm,inputMs,NextNoteMs,Offset,RhythmDifficulty.Casual);
            if(!judged.IsHit)return false;
            int phase=Run.Phase,ordinal=Run.Hits;
            bool finishing=phase>=6&&Run.PhaseSatisfied;
            if(!Run.TryHit())throw new InvalidOperationException("Stage3 state rejected a validated rhythm note.");
            var cut=new VisualCut{generation=presentationGeneration,phase=phase,ordinal=ordinal,
                // Reserve the final physical slice until the section passes.
                // Its work credit still comes exclusively from successful notes.
                sliceTarget=phase>=6?Mathf.Min(11,Run.Slices[phase-6]):0,
                changesPreparation=false,finishing=finishing,
                point=CutContactPoint(phase,ordinal),direction=CutDirection(phase)};
            NoteCursor++;Combo++;MaxCombo=Mathf.Max(MaxCombo,Combo);hitAttempts++;Score+=(int)(1000*judged.ScoreCoefficient);
            if(hudView!=null)hudView.ShowJudgement(judged.grade,adjusted);
            else ShowJudge(judged.grade.ToString(),new Color(1,.78f,.25f));
            visualCuts.Enqueue(cut);StartNextStroke();return true;
        }
        public void AdvancePresentation(float deltaSeconds)
        {
            if(!Started||Paused||Run.Failed||ResultShown||returning||deltaSeconds<=0||
                float.IsNaN(deltaSeconds)||float.IsInfinity(deltaSeconds))return;
            if(!clock.IsRunning&&clock.State!=AudioClockState.Finished)
            {cooking.SetPaused(true);return;}
            cooking.SetPaused(false);
            float remaining=deltaSeconds;
            // Drain the finite chart queue in order, including synthetic large-delta tests.
            for(int i=0;i<1024&&remaining>0;i++)
            {
                StartNextStroke();
                if(activeVisualCut==null)break;
                float step=Mathf.Min(remaining,cooking.RemainingStrokeSeconds);
                if(step<=0)break;
                cooking.AdvancePresentation(step);remaining-=step;
                if(Run.Failed||returning)break;
            }
            if(!Run.Failed&&!returning){assembly.AdvancePresentation(deltaSeconds);GroundBody();}
        }
        private void StartNextStroke()
        {
            if(activeVisualCut!=null||visualCuts.Count==0||Paused||Run.Failed||returning)return;
            var cut=visualCuts.Dequeue();activeVisualCut=cut;
            if(!cooking.BeginStroke(cut.finishing?8:cut.phase,cut.point,cut.direction,()=>CommitVisualCut(cut),()=>FinishVisualCut(cut)))
                Fail("칼 동작을 다시 시도해 주세요.");
        }
        private void CommitVisualCut(VisualCut cut)
        {
            if(cut.generation!=presentationGeneration||cut!=activeVisualCut||cut.committed||Paused||Run.Failed||returning||cut.phase!=Run.Phase)return;
            cut.committed=true;
            if(cut.phase>=6)
            {
                int owner=cut.phase-6;
                // A successful rhythm note advances the real cut's work meter.
                // Only a contact that crosses the next slice boundary emits it.
                while(fillets[owner].CutCount<cut.sliceTarget)
                {
                    if(!fillets[owner].TryCut(fillets[owner].CutCount)){Fail("필렛 절단을 확인해 주세요.");return;}
                    SpawnSlice(owner);
                }
                UpdateFilletSupport(owner);
                if(!fillets[owner].IsEmpty)cooking.SetWorkPoint(fillets[owner].NextCutPosition);
            }
            else if(cut.changesPreparation)
            {
                bool changed=cut.phase==0?assembly.TryRemoveHead():
                    cut.phase==1?assembly.TryRemoveSkin(0):cut.phase==2?assembly.TrySeparateWholeFillet(0):
                    cut.phase==3?assembly.TryTurnOver():cut.phase==4?assembly.TryRemoveSkin(1):assembly.TrySeparateWholeFillet(1);
                if(!changed)Fail("생선 손질 순서를 확인해 주세요.");
            }
            if(cut.completesPhase&&!Run.Failed)phaseCompletionCommitted=true;
        }
        private void FinishVisualCut(VisualCut cut)
        {
            if(cut.generation!=presentationGeneration||cut!=activeVisualCut)return;
            activeVisualCut=null;
            if(cut.phase>=6&&cut.phase==Run.Phase&&!fillets[cut.phase-6].IsEmpty)
                cooking.SetWorkPoint(fillets[cut.phase-6].NextCutPosition);
        }
        private void CancelPendingCuts()
        {
            presentationGeneration++;visualCuts.Clear();activeVisualCut=null;
            if(cooking!=null)cooking.CancelStroke();
        }
        private bool PartsAreMoving()
        {
            if(assembly.head.IsMoving)return true;
            foreach(var part in assembly.wholeFillets)if(part.IsMoving)return true;
            foreach(var part in assembly.skins)if(part.IsMoving)return true;
            if(assembly.IsTurningOver)return true;
            return false;
        }
        private Vector3 CutDirection(int phase)
        {
            if(phase>=6)return fillets[phase-6].meshFilter.transform.right;
            Vector3 longitudinal=Vector3.ProjectOnPlane(assembly.transform.TransformDirection(fishLongAxisLocal),Vector3.up).normalized;
            return phase==0?Vector3.Cross(Vector3.up,longitudinal):longitudinal;
        }
        private Vector3 CutContactPoint(int phase,int ordinal)
        {
            if(phase>=6)
            {
                var surface=fillets[phase-6];var bounds=surface.cutCache.remainingMeshes[0].bounds;
                int index=Mathf.Clamp(ordinal*12/Mathf.Max(12,Run.RequiredHits),0,11);
                float z=surface.cutCache.cutPositions[index];
                return surface.meshFilter.transform.TransformPoint(new Vector3(bounds.center.x,bounds.max.y,z));
            }
            var boundsBody=PreparationBodyBounds(phase);
            var point=PreparationWorkPoint(phase);
            float progress=Mathf.Clamp01(ordinal/(float)Mathf.Max(1,Run.RequiredHits-1));
            if(phase!=3)point+=CutDirection(phase)*(progress-.5f)*(phase==0?.04f:.12f);
            point.x=Mathf.Clamp(point.x,boundsBody.min.x,boundsBody.max.x);
            point.z=Mathf.Clamp(point.z,boundsBody.min.z,boundsBody.max.z);return point;
        }
        private Bounds PreparationBodyBounds(int phase)
        {
            // Keep the final work frame while the boundary completion removes
            // the carcass, until the next fillet section has been prepared.
            if(assembly.ActiveBodyObject==null)
            {
                if(!hasPreparationBounds)throw new InvalidOperationException("No preparation work frame has been captured.");
                return lastPreparationBounds;
            }
            var renderers=assembly.ActiveBodyObject.GetComponentsInChildren<Renderer>(true);
            Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            lastPreparationBounds=bounds;hasPreparationBounds=true;return bounds;
        }
        private void PreparePhase()
        {
            SetSurfacesEnabled(false);
            if(Run.Phase>=6&&Run.Phase<8)
            {
                int index=Run.Phase-6;
                if(preparedFillet!=index)
                {
                    var part=assembly.GetStoredWholeFillet(index);
                    if(part==null)throw new InvalidOperationException("Stage3 whole-side fillet is not stored.");
                    part.transform.SetParent(assembly.filletWorkAnchor,false);
                    Bounds meshBounds=fillets[index].cutCache.remainingMeshes[0].bounds;
                    part.transform.localPosition=new Vector3(-meshBounds.center.x,-meshBounds.min.y,-meshBounds.center.z);
                    part.transform.localRotation=Quaternion.identity;
                    part.gameObject.SetActive(true);preparedFillet=index;
                }
                fillets[index].SetCuttingEnabled(true);
                cooking.SetWorkPoint(fillets[index].NextCutPosition);UpdateFilletSupport(index);
            }
            else {cooking.ClearSupportPoint();cooking.SetWorkPoint(PreparationWorkPoint(Run.Phase));}
        }
        public Vector3 PreparationWorkPoint(int phase)
        {
            Bounds bounds=PreparationBodyBounds(phase);Vector3 point=bounds.center;
            if(phase==0)
            {
                var region=assembly.head.GetComponentInChildren<Renderer>(true).bounds;
                point=Vector3.Lerp(region.center,bounds.center,.25f);
                point.x=Mathf.Clamp(point.x,bounds.min.x,bounds.max.x);
                point.z=Mathf.Clamp(point.z,bounds.min.z,bounds.max.z);
            }
            point.y=bounds.max.y;return point;
        }
        private void SetSurfacesEnabled(bool enabled)
        {foreach(var fillet in fillets)fillet.SetCuttingEnabled(enabled);}
        private void UpdateFilletSupport(int index)
        {
            var filter=fillets[index].meshFilter;
            if(filter.sharedMesh==null)return;
            Bounds bounds=filter.sharedMesh.bounds;
            cooking.SetSupportPoint(filter.transform.TransformPoint(new Vector3(bounds.center.x,bounds.max.y,bounds.center.z))+Vector3.up*.012f);
        }
        private void GroundBody()
        {
            if(!assembly.HasBody)return;
            var renderers=assembly.ActiveBodyObject.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0)return;
            float bottom=float.PositiveInfinity;
            foreach(var renderer in renderers)bottom=Mathf.Min(bottom,renderer.bounds.min.y);
            assembly.bodyPivot.position+=Vector3.up*(boardTop+.008f-bottom);
        }
        private void SpawnSlice(int owner)
        {
            if(slices.Count>=24)throw new InvalidOperationException("Too many Stage3 slices.");
            var go=Instantiate(assembly.sashimiSlicePrefab,sliceRoot);
            var slot=plateSlots[slices.Count];
            go.name="Stage03_Slice_"+slices.Count;
            go.transform.SetPositionAndRotation(slot.position,slot.rotation);
            go.transform.localScale=Vector3.one*sliceUniformScale;
            float bottom=float.PositiveInfinity;
            foreach(var renderer in go.GetComponentsInChildren<Renderer>(true))bottom=Mathf.Min(bottom,renderer.bounds.min.y);
            if(!float.IsInfinity(bottom))go.transform.position+=Vector3.up*(slot.position.y-bottom);
            slices.Add(go);sliceOwners.Add(owner);
        }
        private void Fail(string message)
        {
            CancelPendingCuts();cooking.SetPaused(true);
            Run.Fail();clock.Pause();SetSurfacesEnabled(false);failurePanel.SetActive(true);
            if(hudView!=null)hudView.ShowFailure(message);
            if(judgementText!=null)judgementText.text=message;
        }
        public bool RetryPhase()
        {
            if(!Run.Failed||Paused||ResultShown)return false;
            ResetRun();return StartRun();
        }
        public void TogglePause()
        {
            if(!Started||ResultShown||returning||Run.Completed||Run.Failed)return;
            Paused=!Paused;pausePanel.SetActive(Paused);
            cooking.SetPaused(Paused);
            if(Paused){clock.Pause();SetSurfacesEnabled(false);}
            else if(!Run.Failed){clock.Resume();if(Run.Phase>=6)fillets[Run.Phase-6].SetCuttingEnabled(true);}
        }
        private void ShowResult()
        {
            if(ResultShown||!Run.Completed||Run.TotalSlices!=24||slices.Count!=24||PendingVisualCuts>0)return;
            // Let the approved source's short outro finish beneath the result
            // instead of truncating its remaining vocals. Returning stops it.
            ResultShown=true;resultDelay=-1;resultPanel.SetActive(true);
            TrySaveClear();
        }
        private bool TrySaveClear()
        {
            if(ClearRecorded)return true;
            var manager=SaveManager.Instance;
            if(manager==null||GameFlowManager.Instance==null)
            {
                resultText.text="회 한 접시 완성\n진행 시스템을 확인해 주세요. 이 화면에서 대기합니다.";return false;
            }
            if(!clearApplied)
            {
                var before=manager.Current.dayWorld.beat;
                try
                {
                    GameFlowManager.Instance.CompleteStage(new StageClearPayload
                    {stageId=SashimiBoyConstants.StageIds.Sole,nextStageId=SashimiBoyConstants.StageIds.Mullet,
                     fishType=FishType.Sole,rewardPlates=1,accuracy01=Accuracy01,yield01=1,score=Score,allNasty=false});
                    clearApplied=!DayWorldFlow.Active||manager.Current.dayWorld.beat!=before;
                }
                catch(IOException){clearApplied=manager.Current.dayWorld.beat!=before;}
                catch(UnauthorizedAccessException){clearApplied=manager.Current.dayWorld.beat!=before;}
            }
            if(!clearApplied){resultText.text="회 한 접시 완성\n진행 상태를 확인해 주세요. 보상은 중복 적용하지 않습니다.";return false;}
            try {manager.Save();ClearRecorded=true;}
            catch(IOException){resultText.text="회 한 접시 완성\n클리어 기록을 저장하지 못했습니다.\nF 또는 버튼으로 저장을 다시 시도하세요.";return false;}
            catch(UnauthorizedAccessException){resultText.text="회 한 접시 완성\n저장할 수 없습니다. F 또는 버튼으로 다시 시도하세요.";return false;}
            resultText.text="STAGE 03 CLEAR · 회 한 판 완성\n\nSCORE  "+Score+"\nMAX COMBO  "+MaxCombo+"\nYIELD  100.0%\n회 24점 · 철수와 이야기하세요";
            return true;
        }
        public void ReturnToShop()
        {
            if(returning)return;
            if(ResultShown&&!TrySaveClear())return;
            if(SceneTransitionService.Instance==null||SceneTransitionService.Instance.IsLoading)return;
            returning=true;CancelPendingCuts();cooking.SetPaused(true);clock.Stop();Time.timeScale=1;
            if(DayWorldFlow.Active)DayWorldFlow.Instance.LoadWorld(SashimiBoyConstants.Scenes.FishShopDialogue,"StageReturn");
            else Stage01ShopReturn.Request();
        }
        private void ShowJudge(string message,Color color)
        {
            if(hudView!=null){hudView.ShowStatus(message,color);return;}
            if(judgementText!=null){judgementText.text=message;judgementText.color=color;judgeTimer=1f;}
        }
        private void RefreshHUD()
        {
            if(hudView!=null){hudView.Refresh();return;}
            if(chart==null||titleText==null)return;
            titleText.text="가자미 · "+(Run.Completed?"회 한 접시 완성":chart.phases[Run.Phase].label);
            statusText.text="회 "+slices.Count+" / 24    점수 "+Score+"    콤보 "+Combo+
                (!Run.Completed?"    손질 "+Run.Hits+" / "+Run.RequiredHits:"");
            if(songFill!=null)
            {
                double first=chart.phases[0].noteTimesMs[0]-2500d;
                var final=chart.phases[7].noteTimesMs;
                float progress=Mathf.Clamp01((float)((clock.SongTimeMs-first)/(final[final.Length-1]+2000d-first)));
                songFill.fillAmount=progress;
                if(songFill.sprite==null)songFill.rectTransform.localScale=new Vector3(progress,1,1);
            }
            if(Paused||clock.State==AudioClockState.Paused)noteText.text="일시 정지";
            else noteText.text=Started?(Run.Phase==3?"[ SPACE ] · 뒤집기 노트":"계속 박자에 맞춰 Space · 구간 성공률 70% 이상"):"Enter로 손질 시작";
            if(noteMarker!=null&&noteLane!=null)
            {
                bool show=NextNoteMs>=0&&!Paused&&!Run.Failed&&!ResultShown;
                noteMarker.gameObject.SetActive(show);
                if(show)noteMarker.anchoredPosition=new Vector2(Mathf.Clamp((float)((NextNoteMs-clock.SongTimeMs)/2000d),-1,1)*noteLane.rect.width*.44f,0);
            }
            if(judgeTimer>0){judgeTimer-=Time.unscaledDeltaTime;if(judgeTimer<=0&&judgementText!=null)judgementText.text="";}
        }
        private void OnDisable(){CancelPendingCuts();if(clock!=null)clock.Stop();}
    }
}
