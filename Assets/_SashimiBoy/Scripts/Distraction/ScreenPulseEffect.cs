using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class ScreenPulseEffect : MonoBehaviour, IDistractionEffect
    {
        public DistractionType Type => DistractionType.ScreenPulse;

        public Image overlay;
        public RectTransform targetRect;
        public float maxAlpha = 0.25f;
        public float maxScale = 1.05f;

        private Color startColor;
        private Vector3 startScale;
        private Coroutine routine;

        private void Awake()
        {
            if (overlay != null)
            {
                startColor = overlay.color;
                Color c = startColor;
                c.a = 0f;
                overlay.color = c;
            }

            if (targetRect != null)
            {
                startScale = targetRect.localScale;
            }
        }

        public void Play(DistractionCue cue, float bpm)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
            }

            routine = StartCoroutine(Routine(cue, bpm));
        }

        public void Stop()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            ResetVisuals();
        }

        private IEnumerator Routine(DistractionCue cue, float bpm)
        {
            float duration = cue.durationBeats * 60f / Mathf.Max(1f, bpm);
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float pulse = Mathf.Sin((t / duration) * Mathf.PI);
                if (overlay != null)
                {
                    Color c = overlay.color;
                    c.a = maxAlpha * cue.intensity * pulse;
                    overlay.color = c;
                }

                if (targetRect != null)
                {
                    targetRect.localScale = startScale * Mathf.Lerp(1f, maxScale, pulse * cue.intensity);
                }

                yield return null;
            }

            ResetVisuals();
            routine = null;
        }

        private void ResetVisuals()
        {
            if (overlay != null)
            {
                Color c = overlay.color;
                c.a = 0f;
                overlay.color = c;
            }

            if (targetRect != null)
            {
                targetRect.localScale = startScale;
            }
        }
    }
}
