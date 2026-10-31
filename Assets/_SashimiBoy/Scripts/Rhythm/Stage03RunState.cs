using System;
namespace SashimiBoy
{
    // Stage3's nine phases are separate from the serialized six-phase Stage1/2 contract.
    public sealed class Stage03RunState
    {
        public int Phase {get;private set;}
        public int Hits {get;private set;}
        public bool WaitingForTurn {get;private set;}
        public bool Turned {get;private set;}
        public bool Failed {get;private set;}
        public bool Completed => Phase==9;
        public int[] Slices {get;} = new int[4];
        public int TotalSlices => Slices[0]+Slices[1]+Slices[2]+Slices[3];
        public int RequiredHits => Phase<5?4:6;
        public bool PhaseSatisfied => !Completed && Hits>=RequiredHits;
        public bool TryHit()
        {
            if (Failed||Completed||WaitingForTurn||PhaseSatisfied) return false;
            if (Phase>=3&&!Turned) return false;
            Hits++;
            if (Phase>=5) Slices[Phase-5]++;
            return true;
        }
        public bool TryAdvance()
        {
            if (Failed||Completed||!PhaseSatisfied||WaitingForTurn) return false;
            if (Phase==2&&!Turned) {WaitingForTurn=true;return false;}
            if (Phase==8&&TotalSlices!=24) return false;
            Phase++;Hits=0;
            return true;
        }
        public bool FinishTurn()
        {
            if (!WaitingForTurn||Turned||Failed) return false;
            Turned=true;WaitingForTurn=false;Phase++;Hits=0;return true;
        }
        public void Fail() {if(!Completed)Failed=true;}
        public bool RecoverSatisfiedPreparation()
        {
            if(!Failed||Phase>=5||!PhaseSatisfied)return false;
            Failed=false;return true;
        }
        public bool RetryPhase()
        {
            if(!Failed||Completed)return false;
            if(Phase>=5)Slices[Phase-5]=0;
            Hits=0;Failed=false;return true;
        }
        public void Reset()
        {
            Phase=Hits=0;WaitingForTurn=Turned=Failed=false;Array.Clear(Slices,0,4);
        }
    }
}
