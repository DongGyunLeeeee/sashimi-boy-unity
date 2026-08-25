using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class EquipmentShopController : MonoBehaviour
    {
        [Header("UI")]
        public Text titleText;
        public Text kevinRequestText;
        public Text shopkeeperText;
        public Text itemNameText;
        public Text itemDescriptionText;
        public Text priceText;
        public Text ownedText;
        public Button buyButton;
        public Button leaveButton;

        [Header("Scene")]
        public string streetSceneName = SashimiBoyConstants.Scenes.Street;

        private StageRuntimeData recommendedStage;
        private SaveData currentSave;

        private void OnEnable()
        {
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.OnSaveChanged += HandleSaveChanged;
            }
        }

        private void OnDisable()
        {
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.OnSaveChanged -= HandleSaveChanged;
            }
        }

        private void Awake()
        {
            if (buyButton != null)
            {
                buyButton.onClick.AddListener(BuyRecommended);
            }

            if (leaveButton != null)
            {
                leaveButton.onClick.AddListener(LeaveShop);
            }
        }

        private void Start()
        {
            if (GameFlowManager.Instance != null)
            {
                GameFlowManager.Instance.SetLocation(GameLocation.EquipmentShop);
            }

            Refresh();
        }

        private void HandleSaveChanged(SaveData save)
        {
            Refresh();
        }

        public void Refresh()
        {
            currentSave = SaveManager.Instance != null ? SaveManager.Instance.Current : null;
            recommendedStage = GetDisplayStage(currentSave);

            if (titleText != null)
            {
                titleText.text = "장비 가게";
            }

            if (recommendedStage == null)
            {
                SetNoRecommendation();
                return;
            }

            EquipmentRuntimeData equipment = ContentDefaults.FindEquipment(recommendedStage.rewardEquipment);
            bool owned = currentSave != null && currentSave.HasEquipment(recommendedStage.rewardEquipment);
            bool cleared = currentSave != null && currentSave.IsStageCleared(recommendedStage.stageId);
            bool canExchange = CanExchange(currentSave, recommendedStage);
            int plates = currentSave != null ? currentSave.GetPlates(recommendedStage.fishType) : 0;

            if (kevinRequestText != null)
            {
                kevinRequestText.text = $"케빈: {recommendedStage.kevinShopRequest}";
            }

            if (shopkeeperText != null)
            {
                shopkeeperText.text = $"사장: {recommendedStage.shopkeeperRecommendation}";
            }

            if (itemNameText != null)
            {
                itemNameText.text = GetEquipmentName(equipment, recommendedStage.rewardEquipment);
            }

            if (itemDescriptionText != null)
            {
                itemDescriptionText.text = $"역할: {equipment.role}\n변화: {equipment.experienceChange}\n연출: {equipment.visualFeedback}";
            }

            if (priceText != null)
            {
                priceText.text = $"보상 출처: {recommendedStage.displayName} 클리어 / 필요 {recommendedStage.fishType} {recommendedStage.requiredPlatesForExchange}접시 / 보유 {plates}접시";
            }

            if (ownedText != null)
            {
                ownedText.text = GetExchangeStatusText(currentSave, recommendedStage, equipment);
            }

            if (buyButton != null)
            {
                buyButton.interactable = canExchange;
                Text buttonText = buyButton.GetComponentInChildren<Text>();
                if (buttonText != null)
                {
                    buttonText.text = GetButtonText(owned, cleared, canExchange);
                }
            }
        }

        private void SetNoRecommendation()
        {
            if (kevinRequestText != null)
            {
                kevinRequestText.text = "케빈: 아직 내가 원하는 소리가 뭔지 모르겠다…";
            }

            if (shopkeeperText != null)
            {
                shopkeeperText.text = "사장: 회를 더 썰고 다시 오게.";
            }

            if (itemNameText != null)
            {
                itemNameText.text = "추천 장비 없음";
            }

            if (itemDescriptionText != null)
            {
                itemDescriptionText.text = "스테이지 클리어 후 영감을 얻으면 장비가 추천됩니다.";
            }

            if (priceText != null)
            {
                priceText.text = "";
            }

            if (ownedText != null)
            {
                ownedText.text = "";
            }

            if (buyButton != null)
            {
                buyButton.interactable = false;
                Text buttonText = buyButton.GetComponentInChildren<Text>();
                if (buttonText != null)
                {
                    buttonText.text = "교환 불가";
                }
            }
        }

        private StageRuntimeData GetDisplayStage(SaveData save)
        {
            StageRuntimeData availableReward = ProgressionQuery.GetLatestClearedStageWithUnownedReward(save);
            if (availableReward != null)
            {
                return availableReward;
            }

            StageRuntimeData latestAcquiredReward = GetLatestClearedRewardStage(save);
            if (latestAcquiredReward != null)
            {
                return latestAcquiredReward;
            }

            return GetNextRewardByProgress(save);
        }

        private StageRuntimeData GetLatestClearedRewardStage(SaveData save)
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

                if (best == null || stage.order > best.order)
                {
                    best = stage;
                }
            }

            return best;
        }

        private StageRuntimeData GetNextRewardByProgress(SaveData save)
        {
            List<StageRuntimeData> stages = ContentDefaults.CreateRewardStagesIncludingStageOneStub();
            for (int i = 0; i < stages.Count; i++)
            {
                StageRuntimeData stage = stages[i];
                if (save == null || !save.HasEquipment(stage.rewardEquipment))
                {
                    return stage;
                }
            }

            return null;
        }

        public void BuyRecommended()
        {
            if (recommendedStage == null || SaveManager.Instance == null || currentSave == null)
            {
                return;
            }

            if (!CanExchange(currentSave, recommendedStage))
            {
                if (ToastUI.Instance != null)
                {
                    ToastUI.Instance.Show(GetBlockedExchangeMessage(currentSave, recommendedStage));
                }

                Refresh();
                return;
            }

            bool success = SaveManager.Instance.TryPurchaseReward(recommendedStage);
            if (success)
            {
                EquipmentRuntimeData equipment = ContentDefaults.FindEquipment(recommendedStage.rewardEquipment);
                if (ToastUI.Instance != null)
                {
                    ToastUI.Instance.Show($"획득 장비: {GetEquipmentName(equipment, recommendedStage.rewardEquipment)}");
                }
            }
            else if (ToastUI.Instance != null)
            {
                ToastUI.Instance.Show(GetBlockedExchangeMessage(currentSave, recommendedStage));
            }

            Refresh();
        }

        public void LeaveShop()
        {
            if (SceneTransitionService.Instance != null)
            {
                SceneTransitionService.Instance.LoadScene(streetSceneName);
            }
        }

        private bool CanExchange(SaveData save, StageRuntimeData stage)
        {
            return SaveManager.CanPurchaseReward(save, stage);
        }

        private string GetExchangeStatusText(SaveData save, StageRuntimeData stage, EquipmentRuntimeData equipment)
        {
            if (save == null)
            {
                return "세이브 로드 대기";
            }

            string equipmentName = GetEquipmentName(equipment, stage.rewardEquipment);
            if (save.HasEquipment(stage.rewardEquipment))
            {
                return $"획득 장비: {equipmentName}";
            }

            if (!save.IsStageCleared(stage.stageId))
            {
                return save.IsStageUnlocked(stage.stageId) ? "교환 불가: 스테이지 클리어 필요" : "교환 불가: 스테이지 잠김";
            }

            int plates = save.GetPlates(stage.fishType);
            if (plates < stage.requiredPlatesForExchange)
            {
                return $"교환 불가: {stage.fishType} 회 부족";
            }

            return $"교환 가능: {equipmentName}";
        }

        private string GetButtonText(bool owned, bool cleared, bool canExchange)
        {
            if (owned)
            {
                return "획득 완료";
            }

            if (canExchange)
            {
                return "교환하기";
            }

            return cleared ? "회 부족" : "클리어 필요";
        }

        private string GetBlockedExchangeMessage(SaveData save, StageRuntimeData stage)
        {
            if (save == null)
            {
                return "세이브가 아직 준비되지 않았습니다.";
            }

            if (save.HasEquipment(stage.rewardEquipment))
            {
                return "이미 획득한 장비입니다.";
            }

            if (!save.IsStageCleared(stage.stageId))
            {
                return $"{stage.displayName} 클리어 후 교환할 수 있습니다.";
            }

            return $"{stage.fishType} 회가 부족합니다.";
        }

        private string GetEquipmentName(EquipmentRuntimeData equipment, EquipmentId fallback)
        {
            return string.IsNullOrWhiteSpace(equipment.displayName) ? fallback.ToString() : equipment.displayName;
        }
    }
}
