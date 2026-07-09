using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class PrototypeDebugHotkeys : MonoBehaviour
    {
        [Header("Hotkeys")]
        public KeyCode clearNextStageKey = KeyCode.F2;
        public KeyCode grantAllEquipmentKey = KeyCode.F8;
        public KeyCode resetSaveKey = KeyCode.F12;

        [Header("Feedback")]
        public bool showToast = true;

        private void Update()
        {
            if (Input.GetKeyDown(clearNextStageKey))
            {
                SimulateClearNextStage();
            }

            if (Input.GetKeyDown(grantAllEquipmentKey))
            {
                GrantAllEquipment();
            }

            if (Input.GetKeyDown(resetSaveKey))
            {
                ResetSave();
            }
        }

        public void SimulateClearNextStage()
        {
            if (SaveManager.Instance == null || GameFlowManager.Instance == null)
            {
                return;
            }

            List<StageRuntimeData> stages = ContentDefaults.CreateStagesExceptStageOne();
            SaveData save = SaveManager.Instance.Current;
            StageRuntimeData target = null;

            for (int i = 0; i < stages.Count; i++)
            {
                if (!save.IsStageCleared(stages[i].stageId))
                {
                    target = stages[i];
                    break;
                }
            }

            if (target == null)
            {
                Toast("모든 스테이지가 이미 클리어 처리됨");
                return;
            }

            save.UnlockStage(target.stageId);
            GameFlowManager.Instance.CompleteStage(new StageClearPayload
            {
                stageId = target.stageId,
                nextStageId = target.nextStageId,
                fishType = target.fishType,
                rewardPlates = target.rewardPlates,
                accuracy01 = 0.86f,
                yield01 = 0.82f,
                score = 10000,
                allNasty = false
            });

            Toast($"디버그 클리어: {target.displayName}. 장비 가게에서 보상 교환 가능.");
        }

        public void GrantAllEquipment()
        {
            if (SaveManager.Instance == null)
            {
                return;
            }

            SaveData save = SaveManager.Instance.Current;
            foreach (EquipmentRuntimeData equipment in ContentDefaults.CreateEquipment())
            {
                save.AddEquipment(equipment.equipmentId);
            }

            foreach (StageRuntimeData stage in ContentDefaults.CreateStagesExceptStageOne())
            {
                save.UnlockStage(stage.stageId);
                save.MarkStageCleared(stage.stageId);
            }

            SaveManager.Instance.RaiseChanged();
            Toast("모든 장비/스테이지 진행 디버그 지급 완료");
        }

        public void ResetSave()
        {
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.ResetSave();
                Toast("세이브 초기화");
            }
        }

        private void Toast(string message)
        {
            if (showToast && ToastUI.Instance != null)
            {
                ToastUI.Instance.Show(message);
            }
            else
            {
                Debug.Log(message);
            }
        }
    }
}
