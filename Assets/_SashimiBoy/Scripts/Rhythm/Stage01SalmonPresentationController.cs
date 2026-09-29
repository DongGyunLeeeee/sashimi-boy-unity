using UnityEngine;

namespace SashimiBoy
{
    public sealed class Stage01SalmonPresentationController : MonoBehaviour
    {
        public Stage01SalmonTimingScaffold timing;
        public ProceduralSalmonView salmon;
        public KnifeVisualController playerKnife;
        public SliceCuePresenter sliceCue;
        public Stage01SalmonHUD hud;
        public BossDemoPresenter bossDemo;
        public JudgementFeedbackView judgementFeedback;
        public Stage01NotePatternProvider notePatternProvider;
        public Stage01ActiveNoteTracker activeNoteTracker;
        public Stage01ButcheryPresenter butchery;
        public bool cameraDrivenByBody;

        private Camera stageCamera;
        private Vector3 cameraBasePosition;
        private float cameraReactionTimer;
        private float cameraReactionStrength;
        private bool cameraReactionRandom;

        private void Awake()
        {
            stageCamera = Camera.main;
            if (stageCamera != null)
            {
                cameraBasePosition = stageCamera.transform.position;
            }

            if (timing != null)
            {
                Bind(timing);
            }
        }

        private void Start()
        {
            if (timing == null)
            {
                timing = FindAnyObjectByType<Stage01SalmonTimingScaffold>();
            }

            if (timing != null)
            {
                Bind(timing);
            }
        }

        private void Update()
        {
            UpdateCameraReaction();
        }

        public void Bind(Stage01SalmonTimingScaffold timingSource)
        {
            timing = timingSource;
            notePatternProvider = timing != null
                ? timing.notePatternProvider
                : notePatternProvider;
            activeNoteTracker = timing != null
                ? timing.activeNoteTracker
                : activeNoteTracker;

            if (bossDemo != null)
            {
                bossDemo.Bind(
                    timing,
                    hud,
                    notePatternProvider,
                    salmon);
            }

            if (sliceCue != null)
            {
                sliceCue.Bind(
                    timing,
                    salmon,
                    playerKnife,
                    activeNoteTracker,
                    bossDemo);
            }

            if (hud != null)
            {
                hud.Bind(timing, salmon);
            }

        }

        public void PresentNonScoringInput()
        {
            // Boss demo remains authoritative before gameplay; input is ignored.
        }

        public void PresentBlockedInput()
        {
            if (sliceCue != null)
            {
                sliceCue.HideImmediate();
            }
        }

        public void PresentJudgement(
            JudgeGrade grade,
            double offsetMs,
            string direction)
        {
            bossDemo?.NotifyGameplayInput();

            if (butchery == null && playerKnife != null)
            {
                playerKnife.PlaySlice(grade);
            }

            if (butchery == null && salmon != null)
            {
                salmon.ApplyJudgement(grade);
            }

            if (judgementFeedback != null)
            {
                judgementFeedback.Show(grade, offsetMs, direction);
            }

            if (hud != null)
            {
                hud.ShowReaction(grade);
                string label = grade == JudgeGrade.Nasty ? "NASTY" : grade == JudgeGrade.Smooth ? "CLEAN" : grade == JudgeGrade.Slipped ? "SLIPPED" : "WHACK";
                hud.ShowReadableJudgement(label + "  " + offsetMs.ToString("+0;-0;0") + "ms",
                    grade == JudgeGrade.Nasty ? new Color(.45f,1f,.73f) : grade == JudgeGrade.Smooth ? new Color(.5f,.9f,1f) : new Color(1f,.63f,.35f));
            }

            cameraReactionTimer = grade == JudgeGrade.Whack ? 0.12f : 0.08f;
            cameraReactionStrength = grade == JudgeGrade.Nasty
                ? 0.018f
                : grade == JudgeGrade.Whack ? 0.032f : 0.008f;
            cameraReactionRandom = grade == JudgeGrade.Whack;
        }

        public void PresentEmptyHit()
        {
            bossDemo?.NotifyGameplayInput();
            if (butchery == null)
            {
                playerKnife?.PlaySlice(JudgeGrade.Whack);
                salmon?.ApplyJudgement(JudgeGrade.Whack);
            }
            judgementFeedback?.ShowStatus(
                "EMPTY",
                "NO ACTIVE NOTE",
                "INPUT WHACK",
                new Color(1f, 0.48f, 0.18f, 1f));
            hud?.ShowReaction(JudgeGrade.Whack);
            BeginFailureReaction(0.032f);
        }

        public void PresentMiss(Stage01RuntimeNote note)
        {
            hud?.ShowReadableJudgement("MISS · 놓침",new Color(1f,.45f,.38f));
            judgementFeedback?.ShowStatus(
                "MISS",
                "NOTE PASSED",
                "AUTO MISS",
                new Color(1f, 0.16f, 0.12f, 1f));
            hud?.ShowReaction(JudgeGrade.Whack);
            BeginFailureReaction(0.022f);
        }

        public void PresentResult()
        {
            if (sliceCue != null)
            {
                sliceCue.HideImmediate();
            }

            if (butchery == null && playerKnife != null)
            {
                playerKnife.SetVisible(false);
            }

            hud?.ShowResult();
        }

        public void ResetForRetry()
        {
            cameraReactionTimer = cameraReactionStrength = 0f;
            if (stageCamera != null && !cameraDrivenByBody) stageCamera.transform.position = cameraBasePosition;
            judgementFeedback?.HideImmediate();
            sliceCue?.HideImmediate();
            Bind(timing);
        }

        private void BeginFailureReaction(float strength)
        {
            cameraReactionTimer = 0.12f;
            cameraReactionStrength = strength;
            cameraReactionRandom = true;
        }

        private void UpdateCameraReaction()
        {
            if (cameraDrivenByBody) return;
            if (stageCamera == null)
            {
                stageCamera = Camera.main;
                if (stageCamera == null)
                {
                    return;
                }

                cameraBasePosition = stageCamera.transform.position;
            }

            if (cameraReactionTimer <= 0f)
            {
                stageCamera.transform.position = cameraBasePosition;
                return;
            }

            cameraReactionTimer -= Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(cameraReactionTimer / 0.12f);
            Vector3 offset;
            if (cameraReactionRandom)
            {
                offset = new Vector3(
                    Random.Range(-1f, 1f),
                    0f,
                    Random.Range(-1f, 1f)) *
                    cameraReactionStrength * normalized;
            }
            else
            {
                offset = Vector3.forward *
                    Mathf.Sin(normalized * Mathf.PI) * cameraReactionStrength;
            }

            stageCamera.transform.position = cameraBasePosition + offset;
        }
    }
}
