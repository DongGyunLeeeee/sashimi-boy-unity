using UnityEngine;

namespace SashimiBoy
{
    public sealed class EquipmentRoomVisuals : MonoBehaviour
    {
        public EquipmentDisplaySlot[] slots;

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
            HandleSaveChanged(SaveManager.Instance != null ? SaveManager.Instance.Current : null);
        }

        private void HandleSaveChanged(SaveData save)
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    slots[i].Refresh(save);
                }
            }
        }
    }
}
