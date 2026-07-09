using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class ShopEntranceHint : MonoBehaviour
    {
        public Text text;

        private void Start()
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
            text.text = $"장비 가게\n추천: {eq.displayName}";
        }
    }
}
