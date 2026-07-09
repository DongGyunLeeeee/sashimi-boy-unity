using System;
using UnityEngine;

namespace SashimiBoy
{
    [DefaultExecutionOrder(-9500)]
    public sealed class GameFlowManager : MonoBehaviour
    {
        public static GameFlowManager Instance { get; private set; }

        public event Action<GameLocation> OnLocationChanged;
        public event Action<string> OnStageStartRequested;
        public event Action<StageClearPayload> OnStageCleared;

        [SerializeField] private GameLocation currentLocation = GameLocation.Unknown;
        [SerializeField] private string pendingStageId;

        public GameLocation CurrentLocation => currentLocation;
        public string PendingStageId => pendingStageId;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public void SetLocation(GameLocation location)
        {
            if (currentLocation == location)
            {
                return;
            }

            currentLocation = location;
            OnLocationChanged?.Invoke(location);
        }

        public void RequestStage(string stageId)
        {
            pendingStageId = stageId;
            OnStageStartRequested?.Invoke(stageId);
        }

        public void CompleteStage(StageClearPayload payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.stageId))
            {
                Debug.LogWarning("Stage clear ignored because payload is empty.");
                return;
            }

            SaveManager.Instance.ApplyStageClear(payload);
            OnStageCleared?.Invoke(payload);
        }
    }
}
