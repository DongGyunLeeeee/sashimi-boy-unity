using System.Collections;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class CameraShakeEffect : MonoBehaviour, IDistractionEffect
    {
        public DistractionType Type => DistractionType.CameraShake;

        public Transform target;
        public float baseAmplitude = 0.15f;

        private Vector3 startLocalPosition;
        private Coroutine routine;

        private void Awake()
        {
            if (target == null && Camera.main != null)
            {
                target = Camera.main.transform;
            }

            if (target != null)
            {
                startLocalPosition = target.localPosition;
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

            routine = StartCoroutine(ShakeRoutine(cue, bpm));
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
                target.localPosition = startLocalPosition;
            }
        }

        private IEnumerator ShakeRoutine(DistractionCue cue, float bpm)
        {
            float duration = BeatToSeconds(cue.durationBeats, bpm);
            float end = Time.time + duration;
            while (Time.time < end)
            {
                Vector2 random = Random.insideUnitCircle * baseAmplitude * Mathf.Max(0.1f, cue.intensity);
                target.localPosition = startLocalPosition + new Vector3(random.x, random.y, 0f);
                yield return null;
            }

            target.localPosition = startLocalPosition;
            routine = null;
        }

        private static float BeatToSeconds(float beats, float bpm)
        {
            return beats * 60f / Mathf.Max(1f, bpm);
        }
    }
}
