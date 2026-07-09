using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class EquipmentShopController : MonoBehaviour
    {
        [Header("Prototype")]
        public bool allowFreePrototypePurchases = true;
        public bool simulateStageOneCleared = true;

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

            PreparePrototypeState();
            Refresh();
        }

        public void Refresh()
        {
            SaveData save = SaveManager.Instance != null ? SaveManager.Instance.Current : null;
            recommendedStage = ProgressionQuery.GetLatestClearedStageWithUnownedReward(save);

            if (recommendedStage == null)
            {
                recommendedStage = GetNextRecommendationByProgress(save);
            }

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
            bool owned = save != null && save.HasEquipment(recommendedStage.rewardEquipment);
            int plates = save != null ? save.GetPlates(recommendedStage.fishType) : 0;

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
                itemNameText.text = equipment.displayName;
            }

            if (itemDescriptionText != null)
            {
                itemDescriptionText.text = $"역할: {equipment.role}\n변화: {equipment.experienceChange}\n연출: {equipment.visualFeedback}";
            }

            if (priceText != null)
            {
                priceText.text = $"교환 조건: {recommendedStage.displayName} 회 {recommendedStage.requiredPlatesForExchange}접시 / 보유 {plates}접시";
            }

            if (ownedText != null)
            {
                ownedText.text = owned ? "이미 보유 중" : "미보유";
            }

            if (buyButton != null)
            {
                buyButton.interactable = !owned && (allowFreePrototypePurchases || plates >= recommendedStage.requiredPlatesForExchange);
                Text buttonText = buyButton.GetComponentInChildren<Text>();
                if (buttonText != null)
                {
                    buttonText.text = owned ? "획득 완료" : "교환하기";
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
            }
        }

        private StageRuntimeData GetNextRecommendationByProgress(SaveData save)
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
            if (recommendedStage == null || SaveManager.Instance == null)
            {
                return;
            }

            bool success = SaveManager.Instance.TryPurchaseReward(recommendedStage, allowFreePrototypePurchases);
            if (success)
            {
                EquipmentRuntimeData equipment = ContentDefaults.FindEquipment(recommendedStage.rewardEquipment);
                if (ToastUI.Instance != null)
                {
                    ToastUI.Instance.Show($"{equipment.displayName} 획득");
                }
            }
            else if (ToastUI.Instance != null)
            {
                ToastUI.Instance.Show("회가 부족합니다.");
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

        private void PreparePrototypeState()
        {
            if (!simulateStageOneCleared || SaveManager.Instance == null)
            {
                return;
            }

            SaveData save = SaveManager.Instance.Current;
            if (save == null)
            {
                return;
            }

            if (!save.IsStageCleared(SashimiBoyConstants.StageIds.Salmon))
            {
                save.MarkStageCleared(SashimiBoyConstants.StageIds.Salmon);
                save.UnlockStage(SashimiBoyConstants.StageIds.Rockfish);
                save.AddPlates(FishType.Salmon, 1);
                SaveManager.Instance.RaiseChanged();
            }
        }
    }
}
