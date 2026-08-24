using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class Stage01SalmonHUD : MonoBehaviour
    {
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
                stageTitleText.text = "STAGE 01";
            }

            SetText(fishTypeText, "SALMON / 연어");

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

            SetText(scoreText, $"SCORE  {timing.Score:0000000}");
            SetText(comboText, $"COMBO  {timing.Combo}");
            if (salmon != null)
            {
                SetText(
                    fishProgressText,
                    $"FISH {salmon.CompletedFishCount}   " +
                    $"CUT {salmon.SuccessfulCuts} / {salmon.cutsPerFish}");
            }

            RefreshBeatDots(songSec);
            RefreshDialogue();
            RefreshInspiration();
            RefreshFlash();
            RefreshNoteEvent();
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
            if (resultRoot != null)
            {
                resultRoot.SetActive(true);
            }

            if (resultText != null && timing != null)
            {
                resultText.text =
                    $"RESULT PLACEHOLDER\n\nSCORE  {timing.Score}\n" +
                    $"MAX COMBO  {timing.MaxCombo}\n" +
                    $"YIELD  {timing.YieldPercent:0.0}%";
            }
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
