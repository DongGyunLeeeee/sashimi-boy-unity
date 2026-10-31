using System;
using UnityEngine;
namespace SashimiBoy
{
    [CreateAssetMenu(menuName="Sashimi Boy/Stage03 Chart")]
    public sealed class Stage03Chart : ScriptableObject
    {
        public float bpm = 96.54f;
        public double firstGridBeatSeconds = .20d;
        public int analysisVersion;
        public double[] beatTimesMs=Array.Empty<double>();
        public string sourceSha256;
        public AudioClip music;
        public Stage03PhaseChart[] phases = Array.Empty<Stage03PhaseChart>();
        public void Validate()
        {
            if (music == null || phases.Length != 9 || string.IsNullOrWhiteSpace(sourceSha256)||
                float.IsNaN(bpm)||float.IsInfinity(bpm)||bpm<=0||double.IsNaN(firstGridBeatSeconds)||double.IsInfinity(firstGridBeatSeconds))
                throw new InvalidOperationException("Stage3 chart source or nine phases missing.");
            double previous = 0d;
            for (int p=0;p<phases.Length;p++)
            {
                var phase=phases[p];
                if (phase == null || phase.requiredHits != (p<5?4:6) ||
                    phase.noteTimesMs == null || phase.noteTimesMs.Length != (p<5?6:8))
                    throw new InvalidOperationException("Invalid Stage3 phase "+p);
                foreach (double time in phase.noteTimesMs)
                {
                    if (double.IsNaN(time)||double.IsInfinity(time)||time<=previous||time>=music.length*1000d)
                        throw new InvalidOperationException("Unordered/out-of-range Stage3 note.");
                    previous=time;
                }
            }
            if(previous>music.length*1000d-3000d)
                throw new InvalidOperationException("Stage3 needs a three-second music tail for the final contact and result transition.");
            if(analysisVersion>=2&&(beatTimesMs==null||beatTimesMs.Length<2))
                throw new InvalidOperationException("The analyzed Stage3 beat timeline is missing.");
            double previousBeat=-1;
            if(beatTimesMs!=null)foreach(double beat in beatTimesMs)
            {
                if(double.IsNaN(beat)||double.IsInfinity(beat)||beat<=previousBeat||beat<0||beat>=music.length*1000d)
                    throw new InvalidOperationException("Invalid analyzed Stage3 beat timeline.");
                previousBeat=beat;
            }
        }
        public float BeatPhase01(double songMs)
        {
            if(double.IsNaN(songMs)||double.IsInfinity(songMs))return 0;
            double period=60000d/Math.Max(.001d,bpm);
            double anchor=firstGridBeatSeconds*1000d;
            if(beatTimesMs!=null&&beatTimesMs.Length>1)
            {
                int index=Array.BinarySearch(beatTimesMs,songMs);
                if(index>=0)return 0;
                int next=~index;
                if(next>0&&next<beatTimesMs.Length)
                    return (float)((songMs-beatTimesMs[next-1])/(beatTimesMs[next]-beatTimesMs[next-1]));
                anchor=next==0?beatTimesMs[0]:beatTimesMs[beatTimesMs.Length-1];
            }
            double phase=(songMs-anchor)/period;
            return (float)(phase-Math.Floor(phase));
        }
    }
    [Serializable] public sealed class Stage03PhaseChart
    {
        public string label;
        public int requiredHits;
        public double[] noteTimesMs = Array.Empty<double>();
    }
}
