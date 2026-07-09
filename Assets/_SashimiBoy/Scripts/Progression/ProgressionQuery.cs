using System.Collections.Generic;

namespace SashimiBoy
{
    public static class ProgressionQuery
    {
        public static StageRuntimeData GetLatestClearedStageWithUnownedReward(SaveData save)
        {
            if (save == null)
            {
                return null;
            }

            List<StageRuntimeData> stages = ContentDefaults.CreateRewardStagesIncludingStageOneStub();
            StageRuntimeData best = null;
            for (int i = 0; i < stages.Count; i++)
            {
                StageRuntimeData stage = stages[i];
                if (!save.IsStageCleared(stage.stageId))
                {
                    continue;
                }

                if (save.HasEquipment(stage.rewardEquipment))
                {
                    continue;
                }

                if (best == null || stage.order > best.order)
                {
                    best = stage;
                }
            }

            return best;
        }

        public static StageRuntimeData GetNextLockedStage(SaveData save)
        {
            List<StageRuntimeData> stages = ContentDefaults.CreateStagesExceptStageOne();
            for (int i = 0; i < stages.Count; i++)
            {
                if (save == null || !save.IsStageUnlocked(stages[i].stageId))
                {
                    return stages[i];
                }
            }

            return null;
        }

        public static bool HasAllPerformanceEquipment(SaveData save)
        {
            if (save == null)
            {
                return false;
            }

            List<EquipmentRuntimeData> equipment = ContentDefaults.CreateEquipment();
            for (int i = 0; i < equipment.Count; i++)
            {
                if (!save.HasEquipment(equipment[i].equipmentId))
                {
                    return false;
                }
            }

            return true;
        }

        public static string BuildMissingEquipmentText(SaveData save)
        {
            List<EquipmentRuntimeData> equipment = ContentDefaults.CreateEquipment();
            var missing = new List<string>();
            for (int i = 0; i < equipment.Count; i++)
            {
                if (save == null || !save.HasEquipment(equipment[i].equipmentId))
                {
                    missing.Add(equipment[i].displayName);
                }
            }

            if (missing.Count == 0)
            {
                return "준비 완료";
            }

            return string.Join(", ", missing.ToArray());
        }
    }
}
