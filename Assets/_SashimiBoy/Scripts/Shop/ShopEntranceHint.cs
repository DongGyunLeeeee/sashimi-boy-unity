using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class ShopEntranceHint : MonoBehaviour
    {
        public Text text;

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

        private void Start()
        {
            Refresh();
        }

        private void HandleSaveChanged(SaveData save)
        {
            Refresh();
        }

        public void Refresh()
        {
            if (text == null || SaveManager.Instance == null)
            {
                return;
            }

            StageRuntimeData pending = ProgressionQuery.GetLatestClearedStageWithUnownedReward(SaveManager.Instance.Current);
            if (pending == null)
            {
                text.text = "장비 가게";
                return;
            }

            EquipmentRuntimeData eq = ContentDefaults.FindEquipment(pending.rewardEquipment);
            text.text = $"장비 가게\n교환 가능: {GetEquipmentName(eq, pending.rewardEquipment)}";
        }

        private string GetEquipmentName(EquipmentRuntimeData data, EquipmentId fallback)
        {
            return string.IsNullOrWhiteSpace(data.displayName) ? fallback.ToString() : data.displayName;
        }
    }
}
