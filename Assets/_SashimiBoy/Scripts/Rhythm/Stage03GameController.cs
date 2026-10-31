using System;
using System.Collections.Generic;
using System.IO;
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
        public Transform sliceRoot;
        public Transform[] plateSlots;
        public Text titleText,statusText,noteText,judgementText;
        public RectTransform noteMarker,noteLane;
        public GameObject startPanel,pausePanel,failurePanel,resultPanel;
        public Text resultText;
        public Image songFill;
        public Stage03RunState Run {get;}=new Stage03RunState();
        public int Score {get;private set;}
        public int Combo {get;private set;}
        public bool Started {get;private set;}
        public bool Paused {get;private set;}
        public bool ResultShown {get;private set;}
        public bool ClearRecorded {get;private set;}
        public int NoteCursor {get;private set;}
        public int SpawnedSlices => slices.Count;
        public double NextNoteMs => !Started||Run.Completed||Run.Failed||Run.WaitingForTurn||Run.PhaseSatisfied||
            NoteCursor>=chart.phases[Run.Phase].noteTimesMs.Length ? -1d : chart.phases[Run.Phase].noteTimesMs[NoteCursor];
        private readonly List<GameObject> slices=new List<GameObject>();
        private readonly List<int> sliceOwners=new List<int>();
        private int phaseBaseScore;
        private int preparedFillet=-1;
        private bool returning;
        private bool clearApplied;
        private float resultDelay=-1f;
        private float judgeTimer;
        private float WindowMs=>RhythmJudge.GetMaxWindowMs(chart.bpm,RhythmDifficulty.Casual);
        private float Offset=>SaveManager.Instance!=null?SaveManager.Instance.Current.globalJudgeOffsetMs:0f;
        private void Start()
        {
            chart.Validate();assembly.ResetAssembly();assembly.enabled=false;
            if(fillets==null||fillets.Length!=4||plateSlots==null||plateSlots.Length!=24)
                throw new InvalidOperationException("Stage3 needs four surfaces and twenty-four plate slots.");
            ResetRun();
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            if(GameFlowManager.Instance!=null)GameFlowManager.Instance.SetLocation(GameLocation.FishStage);
        }
        private void Update()
        {
            if(!Started){if(Input.GetKeyDown(KeyCode.Return))StartRun();RefreshHUD();return;}
            if(ResultShown)
            {
                if(Input.GetKeyDown(KeyCode.F))ReturnToShop();
                RefreshHUD();return;
            }
            if(Input.GetKeyDown(KeyCode.Escape))TogglePause();
            if(Paused){RefreshHUD();return;}
            if(Run.Failed){if(Input.GetKeyDown(KeyCode.R))RetryPhase();RefreshHUD();return;}
            if(resultDelay>=0)
            {
                resultDelay-=Time.deltaTime;if(resultDelay<=0)ShowResult();
                RefreshHUD();return;
            }
            bool canPresent=clock.State==AudioClockState.Playing||clock.State==AudioClockState.Scheduled||Run.WaitingForTurn;
            if(canPresent)assembly.AdvancePresentation(Time.deltaTime);
            if(Run.WaitingForTurn)
            {
                if(!assembly.IsTurningOver&&!assembly.IsBackFacing&&Input.GetKeyDown(KeyCode.E))assembly.TryTurnOver();
                if(assembly.IsBackFacing&&!assembly.IsTurningOver)
                {
                    Run.FinishTurn();NoteCursor=0;phaseBaseScore=Score;clock.Resume();PreparePhase();
                }
                RefreshHUD();return;
            }
            if(clock.State==AudioClockState.Faulted){Fail("음원 재생을 확인한 뒤 R로 다시 시도하세요.");RefreshHUD();return;}
            if(clock.State!=AudioClockState.Playing&&clock.State!=AudioClockState.Scheduled){RefreshHUD();return;}
            TickAt(clock.SongTimeMs);
            if(Input.GetKeyDown(KeyCode.Space))SubmitAt(clock.SongTimeMs);
            RefreshHUD();
        }
        public bool StartRun()
        {
            if(Started||returning)return false;
            Started=true;startPanel.SetActive(false);
            if(!clock.Play()){Fail("음원을 재생하지 못했습니다.");return false;}
            return true;
        }
        public void StartFromButton()=>StartRun();
        public void RetryFromButton()=>RetryPhase();
        public void ResetRun()
        {
            clock.Stop();Run.Reset();Score=Combo=NoteCursor=phaseBaseScore=0;
            Started=Paused=ResultShown=ClearRecorded=returning=clearApplied=false;resultDelay=-1;preparedFillet=-1;
            for(int i=0;i<fillets.Length;i++){fillets[i].ResetCuts();fillets[i].SetCuttingEnabled(false);}
            assembly.ResetAssembly();
            foreach(var slice in slices)if(slice!=null)Destroy(slice);
            slices.Clear();sliceOwners.Clear();
            startPanel.SetActive(true);pausePanel.SetActive(false);failurePanel.SetActive(false);resultPanel.SetActive(false);
            if(judgementText!=null)judgementText.text="";
            PreparePhase();
        }
        public void TickAt(double nowMs)
        {
            if(!Started||Paused||Run.Failed||Run.Completed||Run.WaitingForTurn||resultDelay>=0||!clock.IsRunning)return;
            var phase=chart.phases[Run.Phase];
            while(NoteCursor<phase.noteTimesMs.Length&&nowMs-phase.noteTimesMs[NoteCursor]-Offset>WindowMs)
            {
                if(!Run.PhaseSatisfied){Combo=0;ShowJudge("놓침",Color.red);}
                NoteCursor++;
            }
            if(nowMs<phase.noteTimesMs[phase.noteTimesMs.Length-1]+Offset+WindowMs+400d)return;
            if(!Run.PhaseSatisfied){Fail("이 구간을 다시 연습해 보세요. 앞서 마친 손질은 유지됩니다.");return;}
            if(!Run.TryAdvance())
            {
                if(Run.WaitingForTurn)clock.Pause();
                return;
            }
            NoteCursor=0;phaseBaseScore=Score;
            if(Run.Completed)
            {
                SetSurfacesEnabled(false);
                resultDelay=2f;
            }
            else PreparePhase();
        }
        public bool SubmitAt(double inputMs)
        {
            if(!Started||Paused||ResultShown||Run.Failed||Run.Completed||Run.WaitingForTurn||
               Run.PhaseSatisfied||!clock.IsRunning||NextNoteMs<0)return false;
            double adjusted=inputMs-NextNoteMs-Offset;
            if(adjusted < -WindowMs || adjusted>WindowMs)return false;
            var judged=RhythmJudge.Judge(chart.bpm,inputMs,NextNoteMs,Offset,RhythmDifficulty.Casual);
            if(!judged.IsHit)return false;
            int phase=Run.Phase;
            if(phase>=5&&!fillets[phase-5].TryCut(Run.Hits))return false;
            if(!Run.TryHit())throw new InvalidOperationException("Stage3 state rejected a validated cut.");
            NoteCursor++;Combo++;Score+=(int)(1000*judged.ScoreCoefficient);
            ShowJudge(judged.grade.ToString(),new Color(1,.78f,.25f));
            cooking.Pulse();
            if(phase>=5)
            {
                SpawnSlice(phase-5);
                cooking.SetWorkPoint(fillets[phase-5].NextCutPosition);
            }
            else if(Run.PhaseSatisfied)
            {
                bool changed=phase==0?assembly.TryRemoveHead():assembly.TrySeparateFillet(phase-1);
                if(!changed)throw new InvalidOperationException("Stage3 body transition rejected phase "+phase);
            }
            return true;
        }
        private void PreparePhase()
        {
            SetSurfacesEnabled(false);
            if(Run.Phase>=5&&Run.Phase<9)
            {
                int index=Run.Phase-5;
                if(preparedFillet!=index)
                {
                    var part=assembly.GetStoredFillet(index);
                    if(part==null)throw new InvalidOperationException("Stage3 fillet is not stored.");
                    part.transform.SetParent(assembly.filletWorkAnchor,false);
                    part.transform.localPosition=Vector3.zero;part.transform.localRotation=Quaternion.identity;
                    part.gameObject.SetActive(true);preparedFillet=index;
                }
                fillets[index].SetCuttingEnabled(true);
                cooking.SetWorkPoint(fillets[index].NextCutPosition);
            }
            else
            {
                Vector3 focus=assembly.transform.position+new Vector3(0,.05f,0);
                if(Run.Phase==0)focus+=new Vector3(.24f,0,0);
                cooking.SetWorkPoint(focus);
            }
        }
        private void SetSurfacesEnabled(bool enabled)
        {foreach(var fillet in fillets)fillet.SetCuttingEnabled(enabled);}
        private void SpawnSlice(int owner)
        {
            if(slices.Count>=24)throw new InvalidOperationException("Too many Stage3 slices.");
            var go=Instantiate(assembly.sashimiSlicePrefab,sliceRoot);
            var slot=plateSlots[slices.Count];
            go.name="Stage03_Slice_"+slices.Count;
            go.transform.SetPositionAndRotation(slot.position,slot.rotation);
            go.transform.localScale=Vector3.one*.13f;
            slices.Add(go);sliceOwners.Add(owner);
        }
        private void Fail(string message)
        {
            Run.Fail();clock.Pause();SetSurfacesEnabled(false);failurePanel.SetActive(true);
            if(judgementText!=null)judgementText.text=message;
        }
        public bool RetryPhase()
        {
            if(!Run.Failed||Paused||ResultShown)return false;
            int phase=Run.Phase;
            bool preservePreparation=phase<5&&Run.PhaseSatisfied;
            if(phase>=5)
            {
                int owner=phase-5;fillets[owner].ResetCuts();
                for(int i=slices.Count-1;i>=0;i--)if(sliceOwners[i]==owner)
                {Destroy(slices[i]);slices.RemoveAt(i);sliceOwners.RemoveAt(i);}
            }
            if(!(preservePreparation?Run.RecoverSatisfiedPreparation():Run.RetryPhase()))return false;
            if(!preservePreparation){Score=phaseBaseScore;Combo=NoteCursor=0;}
            failurePanel.SetActive(false);
            double restart=Math.Max(0,chart.phases[phase].noteTimesMs[0]-2500d);
            if(!clock.PlayFromMilliseconds(restart)){Fail("음원 재생을 확인해 주세요.");return false;}
            PreparePhase();return true;
        }
        public void TogglePause()
        {
            if(!Started||ResultShown||returning||Run.Completed)return;
            Paused=!Paused;pausePanel.SetActive(Paused);
            if(Paused){clock.Pause();SetSurfacesEnabled(false);}
            else if(!Run.Failed&&!Run.WaitingForTurn){clock.Resume();if(Run.Phase>=5)fillets[Run.Phase-5].SetCuttingEnabled(true);}
        }
        private void ShowResult()
        {
            if(ResultShown||!Run.Completed||Run.TotalSlices!=24||slices.Count!=24)return;
            ResultShown=true;resultDelay=-1;clock.Stop();resultPanel.SetActive(true);
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
                     fishType=FishType.Sole,rewardPlates=1,accuracy01=Mathf.Clamp01(Score/44000f),yield01=1,score=Score,allNasty=false});
                    clearApplied=!DayWorldFlow.Active||manager.Current.dayWorld.beat!=before;
                }
                catch(IOException){clearApplied=manager.Current.dayWorld.beat!=before;}
                catch(UnauthorizedAccessException){clearApplied=manager.Current.dayWorld.beat!=before;}
            }
            if(!clearApplied){resultText.text="회 한 접시 완성\n진행 상태를 확인해 주세요. 보상은 중복 적용하지 않습니다.";return false;}
            try {manager.Save();ClearRecorded=true;}
            catch(IOException){resultText.text="회 한 접시 완성\n클리어 기록을 저장하지 못했습니다.\nF 또는 버튼으로 저장을 다시 시도하세요.";return false;}
            catch(UnauthorizedAccessException){resultText.text="회 한 접시 완성\n저장할 수 없습니다. F 또는 버튼으로 다시 시도하세요.";return false;}
            resultText.text="가자미 손질 완료\n회 24점 · 한 접시 완성\n점수 "+Score+"\n\n식당으로 돌아가 철수와 이야기하세요";
            return true;
        }
        public void ReturnToShop()
        {
            if(returning)return;
            if(ResultShown&&!TrySaveClear())return;
            returning=true;clock.Stop();Time.timeScale=1;
            if(DayWorldFlow.Active)DayWorldFlow.Instance.LoadWorld(SashimiBoyConstants.Scenes.FishShopDialogue,"StageReturn");
            else Stage01ShopReturn.Request();
        }
        private void ShowJudge(string message,Color color)
        {if(judgementText!=null){judgementText.text=message;judgementText.color=color;judgeTimer=1f;}}
        private void RefreshHUD()
        {
            if(chart==null||titleText==null)return;
            titleText.text="가자미 · "+(Run.Completed?"회 한 접시 완성":chart.phases[Run.Phase].label);
            statusText.text="회 "+slices.Count+" / 24    점수 "+Score+"    콤보 "+Combo+
                (!Run.Completed?"    손질 "+Run.Hits+" / "+Run.RequiredHits:"");
            if(songFill!=null)
            {
                double first=chart.phases[0].noteTimesMs[0]-2500d;
                var final=chart.phases[8].noteTimesMs;
                songFill.fillAmount=Mathf.Clamp01((float)((clock.SongTimeMs-first)/(final[final.Length-1]+2000d-first)));
            }
            if(Run.WaitingForTurn)noteText.text=assembly.IsTurningOver?"생선을 뒤집고 있습니다":"앞면 필렛 분리 완료 · E를 눌러 뒤집기";
            else if(Run.PhaseSatisfied)noteText.text="이 구간 완료 · 다음 손질을 준비하세요";
            else if(Paused||clock.State==AudioClockState.Paused)noteText.text="일시 정지";
            else noteText.text=Started?"다가오는 박자에 Space · Esc 일시 정지":"Enter로 손질 시작";
            if(noteMarker!=null&&noteLane!=null)
            {
                bool show=NextNoteMs>=0&&!Paused&&!Run.WaitingForTurn&&!Run.Failed&&!ResultShown;
                noteMarker.gameObject.SetActive(show);
                if(show)noteMarker.anchoredPosition=new Vector2(Mathf.Clamp((float)((NextNoteMs-clock.SongTimeMs)/2000d),-1,1)*noteLane.rect.width*.44f,0);
            }
            if(judgeTimer>0){judgeTimer-=Time.unscaledDeltaTime;if(judgeTimer<=0&&judgementText!=null)judgementText.text="";}
        }
        private void OnDisable(){if(clock!=null)clock.Stop();}
    }
}
