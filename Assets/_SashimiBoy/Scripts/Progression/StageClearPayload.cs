using System;

namespace SashimiBoy
{
    [Serializable]
    public sealed class StageClearPayload
    {
        public string stageId;
        public string nextStageId;
        public FishType fishType;
        public int rewardPlates = 1;
        public bool allNasty;
        public float accuracy01;
        public float yield01;
        public int score;
    }
}
