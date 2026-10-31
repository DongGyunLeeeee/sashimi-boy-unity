using System;
using UnityEngine;
namespace SashimiBoy
{
    [CreateAssetMenu(menuName="Sashimi Boy/Stage03 Chart")]
    public sealed class Stage03Chart : ScriptableObject
    {
        public float bpm = 96.53f;
        public double firstGridBeatSeconds = .16d;
        public string sourceSha256;
        public AudioClip music;
        public Stage03PhaseChart[] phases = Array.Empty<Stage03PhaseChart>();
        public void Validate()
        {
            if (music == null || phases.Length != 9 || string.IsNullOrWhiteSpace(sourceSha256))
                throw new InvalidOperationException("Stage3 chart source or nine phases missing.");
            double previous = 0d;
            for (int p=0;p<phases.Length;p++)
            {
                var phase=phases[p];
                if (phase == null || phase.requiredHits != (p<5?4:6) ||
                    phase.noteTimesMs == null || phase.noteTimesMs.Length < phase.requiredHits)
                    throw new InvalidOperationException("Invalid Stage3 phase "+p);
                foreach (double time in phase.noteTimesMs)
                {
                    if (double.IsNaN(time)||double.IsInfinity(time)||time<=previous||time>=music.length*1000d)
                        throw new InvalidOperationException("Unordered/out-of-range Stage3 note.");
                    previous=time;
                }
            }
        }
    }
    [Serializable] public sealed class Stage03PhaseChart
    {
        public string label;
        public int requiredHits;
        public double[] noteTimesMs = Array.Empty<double>();
    }
}
