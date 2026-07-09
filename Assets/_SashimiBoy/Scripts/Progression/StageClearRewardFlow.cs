using UnityEngine;

namespace SashimiBoy
{
    /// <summary>
    /// Attach this to result scenes or debug tools to bridge a clear payload into inspiration -> shop.
    /// </summary>
    public sealed class StageClearRewardFlow : MonoBehaviour
    {
        public InspirationPopup inspirationPopup;
        public bool autoOpenShopPopup = true;

        private void OnEnable()
        {
            if (GameFlowManager.Instance != null)
            {
                GameFlowManager.Instance.OnStageCleared += HandleStageCleared;
            }
        }

        private void OnDisable()
        {
            if (GameFlowManager.Instance != null)
            {
                GameFlowManager.Instance.OnStageCleared -= HandleStageCleared;
            }
        }

        private void HandleStageCleared(StageClearPayload payload)
        {
            if (!autoOpenShopPopup)
            {
                return;
            }

            StageRuntimeData stage = ContentDefaults.FindStage(payload.stageId);
            if (stage != null && inspirationPopup != null)
            {
                inspirationPopup.ShowForStage(stage);
            }
        }
    }
}
