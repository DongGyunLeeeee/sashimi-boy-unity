using System;
namespace SashimiBoy
{
    // Rhythm resolution never stops at the work quota. Geometry progress is
    // capped separately; every authored note contributes to score and misses.
    public sealed class Stage03RunState
    {
        public const int PhaseCount=8, FirstSlicePhase=6, SlicesPerFillet=12;
        private int[] opportunities={20,20,20,1,20,20,20,20};
        public int Phase {get;private set;}
        public int Hits {get;private set;}
        public int ResolvedNotes {get;private set;}
        public bool WaitingForTurn=>false;
        public bool Turned {get;private set;}
        public bool Failed {get;private set;}
        public bool Completed=>Phase==PhaseCount;
        public int[] Slices {get;}=new int[2];
        public int TotalSlices=>Slices[0]+Slices[1];
        public int NoteCount=>Completed?0:opportunities[Phase];
        public int RequiredHits=>Completed?0:(int)Math.Ceiling(NoteCount*.7d);
        public bool PhaseSatisfied=>!Completed&&Hits>=RequiredHits;
        public void Configure(int[] counts)
        {
            if(counts==null||counts.Length!=PhaseCount||Array.Exists(counts,n=>n<1)||
               counts[3]!=1||Math.Ceiling(counts[6]*.7d)<12||Math.Ceiling(counts[7]*.7d)<12)
                throw new ArgumentException("Eight complete rhythm sections, one turnover and two twelve-slice quotas are required.");
            opportunities=(int[])counts.Clone();Reset();
        }
        public bool TryHit()
        {
            if(Failed||Completed||ResolvedNotes>=NoteCount||(Phase>=4&&!Turned))return false;
            Hits++;ResolvedNotes++;
            if(Phase>=FirstSlicePhase)
                Slices[Phase-FirstSlicePhase]=Math.Min(SlicesPerFillet,Hits*SlicesPerFillet/RequiredHits);
            return true;
        }
        public bool TryMiss()
        {
            if(Failed||Completed||ResolvedNotes>=NoteCount)return false;
            ResolvedNotes++;return true;
        }
        public bool TryAdvance()
        {
            if(Failed||Completed||ResolvedNotes!=NoteCount||!PhaseSatisfied)return false;
            if(Phase==3)Turned=true;
            if(Phase==7&&TotalSlices!=24)return false;
            Phase++;Hits=ResolvedNotes=0;return true;
        }
        public void Fail(){if(!Completed)Failed=true;}
        // Compatibility entry point now restarts the entire stage, never a section.
        public bool RetryPhase(){if(!Failed)return false;Reset();return true;}
        public void Reset(){Phase=Hits=ResolvedNotes=0;Turned=Failed=false;Array.Clear(Slices,0,Slices.Length);}
    }
}
