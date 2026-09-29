using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class Stage01SalmonHUD : MonoBehaviour
    {
        public string stageTitle = "STAGE 01";
        public string fishLabel = "SALMON / 연어";
        public string fishProgressPrefix = "";
        [Header("Top Left")]
        public Text stageTitleText;
        public Text fishTypeText;
        public Text yieldText;
        public Image yieldFill;

        [Header("Top Center")]
        public Image songProgressFill;
        public Image[] beatDots;

        [Header("Top Right")]
        public Text scoreText;
        public Text comboText;
        public Text fishProgressText;

        [Header("Bottom and Center")]
        public Text dialogueText;
        public Text inspirationText;
        public GameObject inspirationRoot;
        public Text countdownText;
        public Text noteEventText;
        public Text missingClipText;
        public Image screenFlash;

        [Header("Result")]
        public GameObject resultRoot;
        public Text resultText;
        public Stage01ButcheryPresenter butchery;
        [Header("Phase requirement")]
        public Image phaseQualityFill;
        public RectTransform phaseThresholdMarker;
        public Text phaseQualityText;
        public Text phaseRequirementText;
        public Text phaseRemainingText;
        public bool compactLayout;
        public GameObject focusHudRoot;
        public Text lastJudgementText;
        public Image judgementImage;
        public Text judgementDetail;
        public JudgementVisualLibrary ownerJudgementVisuals;

        private Stage01SalmonTimingScaffold timing;
        private ProceduralSalmonView salmon;
        private float inspirationTimer;
        private float flashTimer;
        private float noteEventTimer;
        private Color activeFlashColor;
        private bool inspirationShown;

        private void Awake()
        {
            if (stageTitleText != null)
            {
                stageTitleText.text = stageTitle;
            }

            SetText(fishTypeText, fishLabel);

            SetText(inspirationText, string.Empty);
            if (inspirationRoot != null)
            {
                inspirationRoot.SetActive(false);
            }
            SetText(countdownText, string.Empty);
            SetText(noteEventText, string.Empty);
            SetFlash(Color.clear);
            if (resultRoot != null)
            {
                resultRoot.SetActive(false);
            }
        }

        private void Update()
        {
            if (timing == null)
            {
                return;
            }

            double songSec = timing.SongTimeSeconds;
            float yield01 = timing.YieldPercent / 100f;
            SetText(yieldText, $"YIELD  {timing.YieldPercent:0.0}%");
            if (yieldFill != null)
            {
                yieldFill.fillAmount = Mathf.Clamp01(yield01);
            }

            if (songProgressFill != null)
            {
                songProgressFill.fillAmount =
                    timing.GetGameplayProgress01(songSec);
            }

            SetText(scoreText, compactLayout ? $"점수  {timing.Score:N0}" : $"SCORE  {timing.Score:0000000}");
            SetText(comboText, compactLayout ? $"콤보  {timing.Combo}" : $"COMBO  {timing.Combo}");
            if (butchery != null)
            {
                SetText(fishProgressText, $"{fishProgressPrefix}{butchery.PhaseLabel}  |  회 {butchery.SliceCount} / {butchery.plateSlots.Length}");
            }
            else if (salmon != null)
            {
                SetText(
                    fishProgressText,
                    $"FISH {salmon.CompletedFishCount}   " +
                    $"CUT {salmon.SuccessfulCuts} / {salmon.cutsPerFish}");
            }

            RefreshBeatDots(songSec);
            RefreshPhaseGauge();
            RefreshDialogue();
            RefreshInspiration();
            RefreshFlash();
            RefreshNoteEvent();
        }

        private void RefreshPhaseGauge()
        {
            if (timing.semanticBeatmap == null || timing.semanticBeatmap.chart == null) return;
            var performance = timing.PhasePerformance;
            int index = Mathf.Clamp(performance.PhaseIndex, 0, 5);
            var gate = timing.semanticBeatmap.chart.phases[index];
            double requiredPoints = gate.metric == SashimiBoy.Semantics.GateMetric.Ratio
                ? gate.requiredQuality * gate.expectedNoteCount : gate.requiredQuality;
            double points = performance.Completed ? performance.LastGatePoints : performance.PhasePoints;
            float ratio = (float)(points / gate.expectedNoteCount);
            float target = (float)(requiredPoints / gate.expectedNoteCount);
            if (phaseQualityFill != null)
            {
                phaseQualityFill.fillAmount = Mathf.Clamp01(ratio);
                phaseQualityFill.color = performance.Failed ? new Color(1f, .25f, .18f)
                    : points >= requiredPoints ? new Color(.32f, .9f, .67f) : new Color(1f, .69f, .23f);
            }
            if (phaseThresholdMarker != null)
            {
                phaseThresholdMarker.anchorMin = compactLayout ? new Vector2(target,.5f) : new Vector2(0f, target);
                phaseThresholdMarker.anchorMax = compactLayout ? new Vector2(target,.5f) : new Vector2(1f, target);
                phaseThresholdMarker.anchoredPosition = Vector2.zero;
            }
            SetText(phaseQualityText, $"현재 {points:0.00}점");
            SetText(phaseRequirementText, compactLayout ? $"통과 {requiredPoints:0.00} · {target * 100f:0}%" : $"통과 {requiredPoints:0.00}점\n({target * 100f:0}% 이상)");
            SetText(phaseRemainingText, performance.Completed ? "손질 완료" : performance.Failed ? "단계 실패" :
                $"남은 노트 {Mathf.Max(0, gate.lastNoteId + 1 - performance.NoteCursor)}" + (compactLayout ? "" : "\n단계 끝에 판정"));
        }

        public void ShowReadableJudgement(string label, Color color)
        {
            string word = label.Split(' ')[0];
            JudgeGrade grade = word == "NASTY" ? JudgeGrade.Nasty : word == "CLEAN" ? JudgeGrade.Smooth :
                word == "SLIPPED" ? JudgeGrade.Slipped : JudgeGrade.Whack;
            bool graded = word == "NASTY" || word == "CLEAN" || word == "SLIPPED" || word == "WHACK" || word == "MISS";
            bool showImage = graded && judgementImage != null && ownerJudgementVisuals != null &&
                ownerJudgementVisuals.TryGet(grade, out var visual) && visual.sprite != null;
            if (judgementImage != null)
            {
                judgementImage.enabled = showImage;
                if (showImage)
                {
                    ownerJudgementVisuals.TryGet(grade, out var definition);
                    judgementImage.sprite = definition.sprite;
                    judgementImage.color = Color.white;
                }
            }
            if (judgementDetail != null)
            {
                judgementDetail.enabled = showImage;
                judgementDetail.text = word == "MISS" ? "놓침" : label.Substring(word.Length).Trim();
                judgementDetail.color = color;
            }
            if (lastJudgementText != null)
            {
                lastJudgementText.enabled = !showImage;
                lastJudgementText.text = label; lastJudgementText.color = color;
            }
        }

        public void Bind(
            Stage01SalmonTimingScaffold timingSource,
            ProceduralSalmonView salmonView)
        {
            timing = timingSource;
            salmon = salmonView;
        }

        public void SetCountdownLabel(string label)
        {
            SetText(countdownText, label);
        }

        public void ShowReaction(JudgeGrade grade)
        {
            flashTimer = grade == JudgeGrade.Whack ? 0.16f : 0.1f;
            switch (grade)
            {
                case JudgeGrade.Nasty:
                    activeFlashColor = new Color(1f, 1f, 1f, 0.18f);
                    break;
                case JudgeGrade.Smooth:
                    activeFlashColor = new Color(0.55f, 0.92f, 1f, 0.1f);
                    break;
                case JudgeGrade.Slipped:
                    activeFlashColor = new Color(1f, 0.65f, 0.2f, 0.1f);
                    break;
                default:
                    activeFlashColor = new Color(1f, 0.04f, 0.02f, 0.2f);
                    break;
            }
        }

        public void ShowNoteEvent(
            string label,
            Color color,
            float duration = 0.5f)
        {
            if (noteEventText == null)
            {
                return;
            }

            noteEventText.color = color;
            noteEventText.text = label;
            noteEventTimer = Mathf.Max(0.05f, duration);
        }

        public void ShowResult()
        {
            if (focusHudRoot != null) focusHudRoot.SetActive(false);
            if (resultRoot != null)
            {
                resultRoot.SetActive(true);
            }

            if (resultText != null && timing != null)
            {
                resultText.text =
                    $"{stageTitle} CLEAR · 회 한 판 완성\n\nSCORE  {timing.Score}\n" +
                    $"MAX COMBO  {timing.MaxCombo}\n" +
                    $"YIELD  {timing.YieldPercent:0.0}%";
            }
        }

        public void ResetForRetry()
        {
            if (focusHudRoot != null) focusHudRoot.SetActive(true);
            ShowReadableJudgement("박자에 맞춰 Space",Color.white);
            inspirationTimer = flashTimer = noteEventTimer = 0f;
            inspirationShown = false;
            SetText(inspirationText, string.Empty);
            SetText(countdownText, string.Empty);
            SetText(noteEventText, string.Empty);
            if (inspirationRoot != null) inspirationRoot.SetActive(false);
            if (resultRoot != null) resultRoot.SetActive(false);
            SetFlash(Color.clear);
        }

        private void RefreshBeatDots(double songSec)
        {
            if (beatDots == null)
            {
                return;
            }

            int activeBeat = timing.GetCurrentBeatInBar(songSec) - 1;
            for (int i = 0; i < beatDots.Length; i++)
            {
                Image dot = beatDots[i];
                if (dot == null)
                {
                    continue;
                }

                bool active = i == activeBeat;
                dot.color = active
                    ? i == 0
                        ? new Color(1f, 0.78f, 0.24f, 1f)
                        : new Color(0.42f, 0.92f, 1f, 1f)
                    : new Color(1f, 1f, 1f, 0.22f);
                dot.rectTransform.localScale = Vector3.one *
                    (active ? i == 0 ? 1.32f : 1.18f : 1f);
            }
        }

        private void RefreshDialogue()
        {
            if (dialogueText == null || timing == null)
            {
                return;
            }

            switch (timing.CurrentSection)
            {
                case Stage01SalmonSection.BossDemo:
                    dialogueText.text =
                        "사장: 칼을 내리찍는 게 아냐. " +
                        "리듬을 타면서 밀어 넣는 거다.";
                    break;
                case Stage01SalmonSection.Gameplay:
                    dialogueText.text =
                        "예고 노트가 절단선에 닿을 때 Space";
                    break;
                default:
                    dialogueText.text = "손질 결과를 확인한다.";
                    break;
            }
        }

        private void RefreshInspiration()
        {
            if (!inspirationShown && timing != null && timing.Combo >= 8)
            {
                inspirationShown = true;
                inspirationTimer = 1.5f;
                SetText(inspirationText, "영감이 떠오른다...");
                if (inspirationRoot != null)
                {
                    inspirationRoot.SetActive(true);
                }
            }

            if (inspirationTimer <= 0f)
            {
                return;
            }

            inspirationTimer -= Time.unscaledDeltaTime;
            if (inspirationTimer <= 0f)
            {
                SetText(inspirationText, string.Empty);
                if (inspirationRoot != null)
                {
                    inspirationRoot.SetActive(false);
                }
            }
        }

        private void RefreshFlash()
        {
            if (flashTimer <= 0f)
            {
                SetFlash(Color.clear);
                return;
            }

            flashTimer -= Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(flashTimer / 0.16f);
            Color color = activeFlashColor;
            color.a *= alpha;
            SetFlash(color);
        }

        private void RefreshNoteEvent()
        {
            if (noteEventTimer <= 0f)
            {
                return;
            }

            noteEventTimer -= Time.unscaledDeltaTime;
            if (noteEventTimer <= 0f)
            {
                SetText(noteEventText, string.Empty);
            }
        }

        private void SetFlash(Color color)
        {
            if (screenFlash != null)
            {
                screenFlash.color = color;
            }
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
    }
}
