using System.Collections;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class OccluderEffect : MonoBehaviour, IDistractionEffect
    {
        public DistractionType Type => DistractionType.Occluder;

        public GameObject occluderRoot;
        private Coroutine routine;

        private void Awake()
        {
            if (occluderRoot != null)
            {
                occluderRoot.SetActive(false);
            }
        }

        public void Play(DistractionCue cue, float bpm)
        {
            if (occluderRoot == null)
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

            if (occluderRoot != null)
            {
                occluderRoot.SetActive(false);
            }
        }

        private IEnumerator Routine(DistractionCue cue, float bpm)
        {
            occluderRoot.SetActive(true);
            yield return new WaitForSeconds(cue.durationBeats * 60f / Mathf.Max(1f, bpm));
            occluderRoot.SetActive(false);
            routine = null;
        }
    }
}
