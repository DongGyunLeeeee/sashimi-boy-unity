using System.Collections;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class LightFlickerEffect : MonoBehaviour, IDistractionEffect
    {
        public DistractionType Type => DistractionType.LightFlicker;

        public Light targetLight;
        public float flickerSpeed = 18f;

        private float startIntensity;
        private Coroutine routine;

        private void Awake()
        {
            if (targetLight == null)
            {
                targetLight = Object.FindAnyObjectByType<Light>();
            }

            if (targetLight != null)
            {
                startIntensity = targetLight.intensity;
            }
        }

        public void Play(DistractionCue cue, float bpm)
        {
            if (targetLight == null)
            {
                return;
            }

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

            if (targetLight != null)
            {
                targetLight.intensity = startIntensity;
            }
        }

        private IEnumerator Routine(DistractionCue cue, float bpm)
        {
            float duration = cue.durationBeats * 60f / Mathf.Max(1f, bpm);
            float end = Time.time + duration;
            while (Time.time < end)
            {
                float flicker = Mathf.Abs(Mathf.Sin(Time.time * flickerSpeed));
                targetLight.intensity = Mathf.Lerp(startIntensity, startIntensity * 0.2f, flicker * cue.intensity);
                yield return null;
            }

            targetLight.intensity = startIntensity;
            routine = null;
        }
    }
}
