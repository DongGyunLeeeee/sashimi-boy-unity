using System.Collections;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class CameraFlipEffect : MonoBehaviour, IDistractionEffect
    {
        public DistractionType Type => DistractionType.CameraFlip;

        public Transform target;
        public float flipZ = 180f;

        private Quaternion startRotation;
        private Coroutine routine;

        private void Awake()
        {
            if (target == null && Camera.main != null)
            {
                target = Camera.main.transform;
            }

            if (target != null)
            {
                startRotation = target.localRotation;
            }
        }

        public void Play(DistractionCue cue, float bpm)
        {
            if (target == null)
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

            if (target != null)
            {
                target.localRotation = startRotation;
            }
        }

        private IEnumerator Routine(DistractionCue cue, float bpm)
        {
            float duration = cue.durationBeats * 60f / Mathf.Max(1f, bpm);
            target.localRotation = startRotation * Quaternion.Euler(0f, 0f, flipZ);
            yield return new WaitForSeconds(duration);
            target.localRotation = startRotation;
            routine = null;
        }
    }
}
