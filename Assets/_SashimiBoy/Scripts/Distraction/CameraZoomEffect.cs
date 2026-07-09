using System.Collections;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class CameraZoomEffect : MonoBehaviour, IDistractionEffect
    {
        public DistractionType Type => DistractionType.CameraZoom;

        public Camera targetCamera;
        public float zoomMultiplier = 0.75f;

        private float startFov;
        private Coroutine routine;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera != null)
            {
                startFov = targetCamera.fieldOfView;
            }
        }

        public void Play(DistractionCue cue, float bpm)
        {
            if (targetCamera == null)
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

            if (targetCamera != null)
            {
                targetCamera.fieldOfView = startFov;
            }
        }

        private IEnumerator Routine(DistractionCue cue, float bpm)
        {
            float duration = cue.durationBeats * 60f / Mathf.Max(1f, bpm);
            float targetFov = startFov * Mathf.Lerp(1f, zoomMultiplier, Mathf.Clamp01(cue.intensity));
            float half = Mathf.Max(0.01f, duration * 0.5f);
            float t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                targetCamera.fieldOfView = Mathf.Lerp(startFov, targetFov, t / half);
                yield return null;
            }

            t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                targetCamera.fieldOfView = Mathf.Lerp(targetFov, startFov, t / half);
                yield return null;
            }

            targetCamera.fieldOfView = startFov;
            routine = null;
        }
    }
}
