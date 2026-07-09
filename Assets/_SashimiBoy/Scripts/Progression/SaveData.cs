using System;
using System.Collections.Generic;

namespace SashimiBoy
{
    [Serializable]
    public sealed class SaveData
    {
        public int version = 1;
        public string currentStageId = SashimiBoyConstants.StageIds.Salmon;
        public List<string> unlockedStageIds = new List<string>();
        public List<string> clearedStageIds = new List<string>();
        public List<string> ownedEquipmentIds = new List<string>();
        public List<FishPlateStack> fishPlates = new List<FishPlateStack>();
        public float globalJudgeOffsetMs = 0f;
        public int allNastyClearCount = 0;
        public bool sawClubIntro = false;
        public bool sawEquipmentShopIntro = false;

        public static SaveData CreateNew()
        {
            var data = new SaveData();
            data.UnlockStage(SashimiBoyConstants.StageIds.Salmon);
            return data;
        }

        public bool IsStageUnlocked(string stageId)
        {
            return !string.IsNullOrWhiteSpace(stageId) && unlockedStageIds.Contains(stageId);
        }

        public bool IsStageCleared(string stageId)
        {
            return !string.IsNullOrWhiteSpace(stageId) && clearedStageIds.Contains(stageId);
        }

        public void UnlockStage(string stageId)
        {
            if (!string.IsNullOrWhiteSpace(stageId) && !unlockedStageIds.Contains(stageId))
            {
                unlockedStageIds.Add(stageId);
            }
        }

        public void MarkStageCleared(string stageId)
        {
            if (!string.IsNullOrWhiteSpace(stageId) && !clearedStageIds.Contains(stageId))
            {
                clearedStageIds.Add(stageId);
            }
        }

        public bool HasEquipment(EquipmentId equipmentId)
        {
            return ownedEquipmentIds.Contains(equipmentId.ToString());
        }

        public void AddEquipment(EquipmentId equipmentId)
        {
            string id = equipmentId.ToString();
            if (!ownedEquipmentIds.Contains(id))
            {
                ownedEquipmentIds.Add(id);
            }
        }

        public int GetPlates(FishType fishType)
        {
            string key = fishType.ToString();
            for (int i = 0; i < fishPlates.Count; i++)
            {
                if (fishPlates[i].fishType == key)
                {
                    return fishPlates[i].amount;
                }
            }

            return 0;
        }

        public void AddPlates(FishType fishType, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            string key = fishType.ToString();
            for (int i = 0; i < fishPlates.Count; i++)
            {
                if (fishPlates[i].fishType == key)
                {
                    fishPlates[i].amount += amount;
                    return;
                }
            }

            fishPlates.Add(new FishPlateStack { fishType = key, amount = amount });
        }

        public bool TryConsumePlates(FishType fishType, int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            string key = fishType.ToString();
            for (int i = 0; i < fishPlates.Count; i++)
            {
                if (fishPlates[i].fishType != key)
                {
                    continue;
                }

                if (fishPlates[i].amount < amount)
                {
                    return false;
                }

                fishPlates[i].amount -= amount;
                return true;
            }

            return false;
        }
    }

    [Serializable]
    public sealed class FishPlateStack
    {
        public string fishType;
        public int amount;
    }
}
