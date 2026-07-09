using System.Collections.Generic;

namespace SashimiBoy
{
    public sealed class SliceScoreCalculator
    {
        private struct ScoreRef
        {
            public float coeff;
            public int remainingNotes;
        }

        private readonly List<ScoreRef> activeRefs = new List<ScoreRef>();
        private readonly float refCoeff;

        public int Combo { get; private set; }
        public float Score { get; private set; }
        public int NastyCount { get; private set; }
        public int SmoothCount { get; private set; }
        public int SlippedCount { get; private set; }
        public int WhackCount { get; private set; }

        public SliceScoreCalculator(float refCoeff = 0.5f)
        {
            this.refCoeff = refCoeff;
        }

        public void Reset()
        {
            activeRefs.Clear();
            Combo = 0;
            Score = 0f;
            NastyCount = 0;
            SmoothCount = 0;
            SlippedCount = 0;
            WhackCount = 0;
        }

        public float AddJudge(JudgeResult result, float baseScore = 100f)
        {
            float original = baseScore * result.ScoreCoefficient;
            float gained = 0f;

            for (int i = activeRefs.Count - 1; i >= 0; i--)
            {
                ScoreRef scoreRef = activeRefs[i];
                gained += original * scoreRef.coeff;
                scoreRef.remainingNotes -= 1;
                if (scoreRef.remainingNotes <= 0)
                {
                    activeRefs.RemoveAt(i);
                }
                else
                {
                    activeRefs[i] = scoreRef;
                }
            }

            gained += original;

            switch (result.grade)
            {
                case JudgeGrade.Nasty:
                    NastyCount++;
                    Combo++;
                    activeRefs.Add(new ScoreRef { coeff = refCoeff, remainingNotes = 2 });
                    break;
                case JudgeGrade.Smooth:
                    SmoothCount++;
                    Combo++;
                    activeRefs.Add(new ScoreRef { coeff = refCoeff, remainingNotes = 1 });
                    break;
                case JudgeGrade.Slipped:
                    SlippedCount++;
                    Combo = 0;
                    RemoveStrongestRef();
                    break;
                case JudgeGrade.Whack:
                    WhackCount++;
                    Combo = 0;
                    activeRefs.Clear();
                    break;
            }

            Score += gained;
            return gained;
        }

        private void RemoveStrongestRef()
        {
            if (activeRefs.Count == 0)
            {
                return;
            }

            int bestIndex = 0;
            float bestCoeff = activeRefs[0].coeff;
            for (int i = 1; i < activeRefs.Count; i++)
            {
                if (activeRefs[i].coeff > bestCoeff)
                {
                    bestCoeff = activeRefs[i].coeff;
                    bestIndex = i;
                }
            }

            activeRefs.RemoveAt(bestIndex);
        }
    }
}
