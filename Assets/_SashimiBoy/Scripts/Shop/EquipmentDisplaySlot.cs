using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class EquipmentDisplaySlot : MonoBehaviour
    {
        public EquipmentId equipmentId;
        public Text labelText;
        public GameObject lockedVisual;
        public GameObject ownedVisual;

        private void OnEnable()
        {
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.OnSaveChanged += HandleSaveChanged;
                Refresh(SaveManager.Instance.Current);
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
            Refresh(SaveManager.Instance != null ? SaveManager.Instance.Current : null);
        }

        private void HandleSaveChanged(SaveData save)
        {
            Refresh(save);
        }

        public void Refresh(SaveData save)
        {
            EquipmentRuntimeData data = ContentDefaults.FindEquipment(equipmentId);
            bool owned = save != null && save.HasEquipment(equipmentId);

            if (labelText != null)
            {
                labelText.text = owned ? data.displayName : "???";
            }

            if (lockedVisual != null)
            {
                lockedVisual.SetActive(!owned);
            }

            if (ownedVisual != null)
            {
                ownedVisual.SetActive(owned);
            }
        }
    }
}
