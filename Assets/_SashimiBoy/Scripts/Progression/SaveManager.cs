using System;
using System.IO;
using UnityEngine;

namespace SashimiBoy
{
    [DefaultExecutionOrder(-9900)]
    public sealed class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        public event Action<SaveData> OnSaveLoaded;
        public event Action<SaveData> OnSaveChanged;

        [SerializeField] private bool loadOnAwake = true;
        [SerializeField] private bool autoSaveOnChange = true;
        [SerializeField] private SaveData current;

        public SaveData Current => current;
        public string SavePath => Path.Combine(Application.persistentDataPath, SashimiBoyConstants.SaveKeys.SaveFileName);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (loadOnAwake)
            {
                LoadOrCreate();
            }
        }

        public void LoadOrCreate()
        {
            if (File.Exists(SavePath))
            {
                try
                {
                    string json = File.ReadAllText(SavePath);
                    current = JsonUtility.FromJson<SaveData>(json);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Save load failed. New save will be created. {ex.Message}");
                    current = SaveData.CreateNew();
                }
            }
            else
            {
                current = SaveData.CreateNew();
            }

            if (current == null)
            {
                current = SaveData.CreateNew();
            }

            OnSaveLoaded?.Invoke(current);
        }

        public void Save()
        {
            if (current == null)
            {
                current = SaveData.CreateNew();
            }

            string directory = Path.GetDirectoryName(SavePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(current, true);
            File.WriteAllText(SavePath, json);
        }

        public void ResetSave()
        {
            current = SaveData.CreateNew();
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }

            RaiseChanged();
        }

        public void RaiseChanged()
        {
            OnSaveChanged?.Invoke(current);
            if (autoSaveOnChange)
            {
                Save();
            }
        }

        public void ApplyStageClear(StageClearPayload payload)
        {
            if (current == null)
            {
                LoadOrCreate();
            }

            StageRuntimeData stage = ContentDefaults.FindStage(payload.stageId);
            current.MarkStageCleared(payload.stageId);

            if (stage != null)
            {
                current.AddPlates(stage.fishType, Mathf.Max(stage.rewardPlates, payload.rewardPlates));
                if (!string.IsNullOrWhiteSpace(stage.nextStageId))
                {
                    current.UnlockStage(stage.nextStageId);
                }
            }
            else
            {
                current.AddPlates(payload.fishType, payload.rewardPlates);
                if (!string.IsNullOrWhiteSpace(payload.nextStageId))
                {
                    current.UnlockStage(payload.nextStageId);
                }
            }

            if (payload.allNasty)
            {
                current.allNastyClearCount += 1;
            }

            current.currentStageId = string.IsNullOrWhiteSpace(payload.nextStageId) ? payload.stageId : payload.nextStageId;
            RaiseChanged();
        }

        public bool TryPurchaseReward(StageRuntimeData stage, bool allowFreePrototypePurchase)
        {
            if (stage == null)
            {
                return false;
            }

            if (current.HasEquipment(stage.rewardEquipment))
            {
                return true;
            }

            bool paid = allowFreePrototypePurchase || current.TryConsumePlates(stage.fishType, stage.requiredPlatesForExchange);
            if (!paid)
            {
                return false;
            }

            current.AddEquipment(stage.rewardEquipment);
            RaiseChanged();
            return true;
        }
    }
}
