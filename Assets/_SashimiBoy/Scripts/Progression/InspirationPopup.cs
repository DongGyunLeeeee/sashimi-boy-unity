using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class InspirationPopup : MonoBehaviour
    {
        public GameObject root;
        public Text titleText;
        public Text bodyText;
        public Button goShopButton;
        public Button closeButton;
        public string equipmentShopSceneName = SashimiBoyConstants.Scenes.EquipmentShop;

        private void Awake()
        {
            if (goShopButton != null)
            {
                goShopButton.onClick.AddListener(GoToShop);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Hide);
            }

            Hide();
        }

        public void ShowForStage(StageRuntimeData stage)
        {
            if (stage == null)
            {
                return;
            }

            EquipmentRuntimeData equipment = ContentDefaults.FindEquipment(stage.rewardEquipment);

            if (titleText != null)
            {
                titleText.text = "영감이 떠오른다…";
            }

            if (bodyText != null)
            {
                bodyText.text = $"{stage.inspirationPopup}\n\n음향 장비 매장으로 이동해.\n추천 장비: {equipment.displayName}";
            }

            SetVisible(true);
        }

        public void ShowText(string body)
        {
            if (titleText != null)
            {
                titleText.text = "영감이 떠오른다…";
            }

            if (bodyText != null)
            {
                bodyText.text = body;
            }

            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        public void GoToShop()
        {
            if (SceneTransitionService.Instance != null)
            {
                SceneTransitionService.Instance.LoadScene(equipmentShopSceneName);
            }
        }

        private void SetVisible(bool visible)
        {
            if (root != null)
            {
                root.SetActive(visible);
            }
            else
            {
                gameObject.SetActive(visible);
            }
        }
    }
}
