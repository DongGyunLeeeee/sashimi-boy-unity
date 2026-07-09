using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class DistractionDirector : MonoBehaviour
    {
        public AudioClock audioClock;
        public float fallbackBpm = 90f;
        public List<DistractionCue> cues = new List<DistractionCue>();
        public MonoBehaviour[] effectBehaviours;

        private readonly Dictionary<DistractionType, IDistractionEffect> effects = new Dictionary<DistractionType, IDistractionEffect>();
        private int nextCueIndex;
        private float manualStartTime;

        private void Awake()
        {
            RegisterEffects();
        }

        private void OnEnable()
        {
            nextCueIndex = 0;
            manualStartTime = Time.time;
        }

        private void Update()
        {
            if (cues == null || nextCueIndex >= cues.Count)
            {
                return;
            }

            double songTimeMs = audioClock != null && audioClock.IsRunning
                ? audioClock.SongTimeMs
                : (Time.time - manualStartTime) * 1000d;

            double beat = songTimeMs / (60000d / Mathf.Max(1f, fallbackBpm));

            while (nextCueIndex < cues.Count && beat >= cues[nextCueIndex].beat)
            {
                PlayCue(cues[nextCueIndex]);
                nextCueIndex++;
            }
        }

        public void LoadStageCues(StageRuntimeData stage)
        {
            cues.Clear();
            if (stage != null && stage.distractionCues != null)
            {
                cues.AddRange(stage.distractionCues);
                fallbackBpm = stage.bpm;
            }

            cues.Sort((a, b) => a.beat.CompareTo(b.beat));
            nextCueIndex = 0;
            manualStartTime = Time.time;
        }

        public void PlayCue(DistractionCue cue)
        {
            if (cue == null || cue.type == DistractionType.None)
            {
                return;
            }

            if (effects.TryGetValue(cue.type, out IDistractionEffect effect))
            {
                effect.Play(cue, fallbackBpm);
            }
        }

        private void RegisterEffects()
        {
            effects.Clear();
            if (effectBehaviours == null)
            {
                return;
            }

            for (int i = 0; i < effectBehaviours.Length; i++)
            {
                IDistractionEffect effect = effectBehaviours[i] as IDistractionEffect;
                if (effect == null)
                {
                    continue;
                }

                effects[effect.Type] = effect;
            }
        }
    }
}
