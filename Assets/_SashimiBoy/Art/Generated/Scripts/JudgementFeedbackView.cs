using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class JudgementFeedbackView : MonoBehaviour
    {
        [Header("Data")]
        public JudgementVisualLibrary visualLibrary;

        [Header("UI")]
        public CanvasGroup canvasGroup;
        public Image icon;
        public Text fallbackText;
        public Text offsetText;
        public Text directionText;

        [Header("Animation")]
        [Min(0f)] public float scaleInDuration = 0.10f;
        [Min(0f)] public float holdDuration = 0.35f;
        [Min(0f)] public float fadeOutDuration = 0.15f;
        [Range(0.1f, 1f)] public float initialScale = 0.72f;

        private static bool missingSpriteWarningLogged;
        private Coroutine animationRoutine;
        private Vector3 layoutScale = Vector3.one;
        private bool layoutScaleCaptured;

        private void Awake()
        {
            ResolveReferences();
            CaptureLayoutScale();
            transform.localScale = layoutScale;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            animationRoutine = null;
        }

        public void Show(
            JudgeGrade grade,
            double offsetMs,
            string direction)
        {
            ResolveReferences();

            JudgementVisualDefinition visual = null;
            bool hasVisual =
                visualLibrary != null &&
                visualLibrary.TryGet(grade, out visual) &&
                visual != null;
            Sprite sprite = hasVisual ? visual.sprite : null;
            string displayLabel =
                hasVisual && !string.IsNullOrWhiteSpace(visual.displayLabel)
                    ? visual.displayLabel
                    : grade.ToString().ToUpperInvariant();

            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
                icon.preserveAspect = true;
            }

            if (fallbackText != null)
            {
                fallbackText.text = displayLabel;
                fallbackText.color = Color.white;
                fallbackText.gameObject.SetActive(sprite == null);
            }

            if (sprite == null && !missingSpriteWarningLogged)
            {
                missingSpriteWarningLogged = true;
                Debug.LogWarning(
                    "Judgement feedback Sprite is missing. " +
                    "Using the text fallback.");
            }

            if (offsetText != null)
            {
                offsetText.text = offsetMs.ToString("+0;-0;0") + " ms";
                offsetText.color = Color.white;
            }

            if (directionText != null)
            {
                directionText.text = string.IsNullOrWhiteSpace(direction)
                    ? string.Empty
                    : direction.ToUpperInvariant();
                directionText.color = Color.white;
            }

            RestartAnimation();
        }

        public void ShowStatus(
            string headline,
            string detail,
            string caption,
            Color accent)
        {
            ResolveReferences();

            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            if (fallbackText != null)
            {
                fallbackText.text = string.IsNullOrWhiteSpace(headline)
                    ? string.Empty
                    : headline.ToUpperInvariant();
                fallbackText.color = accent;
                fallbackText.gameObject.SetActive(true);
            }

            if (offsetText != null)
            {
                offsetText.text = string.IsNullOrWhiteSpace(detail)
                    ? string.Empty
                    : detail.ToUpperInvariant();
                offsetText.color = Color.white;
            }

            if (directionText != null)
            {
                directionText.text = string.IsNullOrWhiteSpace(caption)
                    ? string.Empty
                    : caption.ToUpperInvariant();
                directionText.color = accent;
            }

            RestartAnimation();
        }

        private void RestartAnimation()
        {
            CaptureLayoutScale();
            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
                animationRoutine = null;
            }

            gameObject.SetActive(true);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            transform.localScale = layoutScale * initialScale;
            animationRoutine = StartCoroutine(PlayAnimation());
        }

        public void HideImmediate()
        {
            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
                animationRoutine = null;
            }

            ResolveReferences();
            CaptureLayoutScale();
            transform.localScale = layoutScale;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        private IEnumerator PlayAnimation()
        {
            float elapsed = 0f;
            while (elapsed < scaleInDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = scaleInDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01(elapsed / scaleInDuration);
                float eased = 1f - (1f - t) * (1f - t);
                transform.localScale = layoutScale *
                    Mathf.Lerp(initialScale, 1f, eased);
                yield return null;
            }

            transform.localScale = layoutScale;

            elapsed = 0f;
            while (elapsed < holdDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = fadeOutDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01(elapsed / fadeOutDuration);
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = 1f - t;
                }

                yield return null;
            }

            transform.localScale = layoutScale;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            animationRoutine = null;
        }

        private void CaptureLayoutScale()
        {
            if (layoutScaleCaptured)
            {
                return;
            }

            layoutScale = transform.localScale;
            if (layoutScale.sqrMagnitude < 0.000001f)
            {
                layoutScale = Vector3.one;
            }

            layoutScaleCaptured = true;
        }

        private void ResolveReferences()
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (icon == null)
            {
                Transform child = transform.Find("Icon");
                if (child != null)
                {
                    icon = child.GetComponent<Image>();
                }
            }

            if (fallbackText == null && icon != null)
            {
                Transform child = icon.transform.Find("FallbackText");
                if (child != null)
                {
                    fallbackText = child.GetComponent<Text>();
                }
            }

            if (offsetText == null)
            {
                Transform child = transform.Find("OffsetText");
                if (child != null)
                {
                    offsetText = child.GetComponent<Text>();
                }
            }

            if (directionText == null)
            {
                Transform child = transform.Find("DirectionText");
                if (child != null)
                {
                    directionText = child.GetComponent<Text>();
                }
            }
        }
    }
}
