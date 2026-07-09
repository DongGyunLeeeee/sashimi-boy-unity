using System;
using System.Collections.Generic;

namespace SashimiBoy
{
    [Serializable]
    public sealed class StageRuntimeData
    {
        public string stageId;
        public string displayName;
        public int order;
        public FishType fishType;
        public string hiphopGenre;
        public float bpm;
        public string requiredPreviousStageId;
        public string nextStageId;
        public EquipmentId rewardEquipment;
        public int rewardPlates;
        public int requiredPlatesForExchange;
        public string inspirationPopup;
        public string kevinShopRequest;
        public string shopkeeperRecommendation;
        public List<DistractionCue> distractionCues = new List<DistractionCue>();

        public static StageRuntimeData FromDefinition(StageDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            return new StageRuntimeData
            {
                stageId = definition.stageId,
                displayName = definition.displayName,
                order = definition.order,
                fishType = definition.fishType,
                hiphopGenre = definition.hiphopGenre,
                bpm = definition.bpm,
                requiredPreviousStageId = definition.requiredPreviousStageId,
                nextStageId = definition.nextStageId,
                rewardEquipment = definition.rewardEquipment,
                rewardPlates = definition.rewardPlates,
                requiredPlatesForExchange = definition.requiredPlatesForExchange,
                inspirationPopup = definition.inspirationPopup,
                kevinShopRequest = definition.kevinShopRequest,
                shopkeeperRecommendation = definition.shopkeeperRecommendation,
                distractionCues = definition.distractionCues != null
                    ? new List<DistractionCue>(definition.distractionCues)
                    : new List<DistractionCue>()
            };
        }
    }
}
