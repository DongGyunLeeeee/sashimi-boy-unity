using System;
using UnityEngine;
namespace SashimiBoy
{
    [CreateAssetMenu(menuName="Sashimi Boy/Stage03 Chart")]
    public sealed class Stage03Chart : ScriptableObject
    {
        public const int PhaseCount=8;
        public const int TurnPhase=3;
        public const int FirstSlicingPhase=6;
        public const int SuccessPercent=70;
        public const int SlicesPerFillet=12;
        public const double MinimumNoteGapMs=280d;
        public const double MinimumPhaseGapMs=1250d;
        public const double ContactTailMs=1000d;
        public const string ApprovedSourceSha256="3572605dec84991aabf1e4911eee443287da0f7e5c403b0a449aee403cf31062";
        public float bpm = 96.54f;
        public double firstGridBeatSeconds = .20d;
        public int analysisVersion;
        public double[] beatTimesMs=Array.Empty<double>();
        public string sourceSha256;
        public AudioClip music;
        public Stage03PhaseChart[] phases = Array.Empty<Stage03PhaseChart>();
        public static int RequiredHitsFor(int noteCount)
        {
            if(noteCount<=0)throw new ArgumentOutOfRangeException(nameof(noteCount));
            return checked((noteCount*SuccessPercent+99)/100);
        }
        public void Validate()
        {
            if (music == null || phases==null || phases.Length != PhaseCount || analysisVersion<3 ||
                !string.Equals(sourceSha256,ApprovedSourceSha256,StringComparison.Ordinal) ||
                Math.Abs(music.length-230.92d)>.05d ||
                float.IsNaN(bpm)||float.IsInfinity(bpm)||bpm<=0||!Finite(firstGridBeatSeconds)||firstGridBeatSeconds<0)
                throw new InvalidOperationException("Stage3 requires its approved full vocal source and eight continuous phases.");
            double previous = -MinimumNoteGapMs;
            double previousLast=-MinimumPhaseGapMs;
            double previousEnd=-250d;
            for (int p=0;p<phases.Length;p++)
            {
                var phase=phases[p];
                if (phase == null || string.IsNullOrWhiteSpace(phase.label) ||
                    phase.noteTimesMs == null || phase.noteTimesMs.Length==0 ||
                    phase.requiredHits != RequiredHitsFor(phase.noteTimesMs.Length) ||
                    (p==TurnPhase&&phase.noteTimesMs.Length!=1) ||
                    (p>=FirstSlicingPhase&&phase.requiredHits<SlicesPerFillet))
                    throw new InvalidOperationException("Invalid Stage3 phase "+p);
                double first=phase.noteTimesMs[0];
                double last=phase.noteTimesMs[phase.noteTimesMs.Length-1];
                if(!Finite(first)||!Finite(last)||!Finite(phase.endTimeMs)||
                    first-previousLast<MinimumPhaseGapMs-.001d||first-previousEnd<250d-.001d||
                    phase.endTimeMs-last<ContactTailMs-.001d||phase.endTimeMs>=music.length*1000d)
                    throw new InvalidOperationException("Stage3 phase deadline must preserve contact/separation time without pausing music: "+p);
                foreach (double time in phase.noteTimesMs)
                {
                    if (!Finite(time)||time<0||time-previous<MinimumNoteGapMs-.001d||time>=music.length*1000d)
                        throw new InvalidOperationException("Unordered, out-of-range, or overlapping Stage3 note.");
                    previous=time;
                }
                previousLast=last;previousEnd=phase.endTimeMs;
            }
            if(previous>music.length*1000d-3000d)
                throw new InvalidOperationException("Stage3 needs a three-second music tail for the final contact and result transition.");
            if(beatTimesMs==null||beatTimesMs.Length<2)
                throw new InvalidOperationException("The analyzed Stage3 beat timeline is missing.");
            double previousBeat=-1;
            if(beatTimesMs!=null)foreach(double beat in beatTimesMs)
            {
                if(!Finite(beat)||beat<=previousBeat||beat<0||beat>=music.length*1000d)
                    throw new InvalidOperationException("Invalid analyzed Stage3 beat timeline.");
                previousBeat=beat;
            }
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
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
        // Fixed musical deadline; success never disables the remaining notes.
        public double endTimeMs;
        public double[] noteTimesMs = Array.Empty<double>();
    }
}
