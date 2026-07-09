using System;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    [CreateAssetMenu(menuName = "Sashimi Boy/Rhythm/Beatmap Definition", fileName = "BeatmapDefinition")]
    public sealed class BeatmapDefinition : ScriptableObject
    {
        public float bpm = 90f;
        public List<BeatmapEvent> events = new List<BeatmapEvent>();

        public double BeatToMs(float beat)
        {
            return beat * (60000d / bpm);
        }
    }

    [Serializable]
    public sealed class BeatmapEvent
    {
        public float beat;
        public SliceInputType inputType = SliceInputType.Tap;
        public string label;
        public bool guideVisible = true;

        public double TimeMs(float bpm)
        {
            return beat * (60000d / bpm);
        }
    }
}
