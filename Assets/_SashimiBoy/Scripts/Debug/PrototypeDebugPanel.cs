using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class PrototypeDebugPanel : MonoBehaviour
    {
        public static PrototypeDebugPanel Instance { get; private set; }

        [Header("Hotkey")]
        public KeyCode toggleKey = KeyCode.F9;
        public bool visibleOnStart = false;

        [Header("Layout")]
        public Vector2 anchoredPosition = new Vector2(12f, -12f);
        public Vector2 size = new Vector2(520f, 220f);
        public int fontSize = 14;

        private readonly StringBuilder builder = new StringBuilder(768);
        private GameObject panelObject;
        private RectTransform panelRect;
        private Text contentText;
        private SaveManager subscribedSaveManager;
        private float nextRefreshTime;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            CreateUi();
            SetVisible(visibleOnStart);
        }

        private void Start()
        {
            TrySubscribeToSaveManager();
            Refresh();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                SetVisible(panelObject == null || !panelObject.activeSelf);
            }

            TrySubscribeToSaveManager();
            if (panelObject != null && panelObject.activeSelf && Time.unscaledTime >= nextRefreshTime)
            {
                Refresh();
                nextRefreshTime = Time.unscaledTime + 0.5f;
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromSaveManager();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void CreateUi()
        {
            var canvasObject = new GameObject("PrototypeDebugPanel_Canvas");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2500;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            panelObject = new GameObject("PrototypeDebugPanel");
            panelObject.transform.SetParent(canvasObject.transform, false);

            panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = anchoredPosition;
            panelRect.sizeDelta = size;

            Image background = panelObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.72f);

            var textObject = new GameObject("Content");
            textObject.transform.SetParent(panelObject.transform, false);

            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 10f);
            textRect.offsetMax = new Vector2(-12f, -10f);

            contentText = textObject.AddComponent<Text>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                contentText.font = font;
            }

            contentText.fontSize = fontSize;
            contentText.alignment = TextAnchor.UpperLeft;
            contentText.color = Color.white;
            contentText.horizontalOverflow = HorizontalWrapMode.Wrap;
            contentText.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void SetVisible(bool visible)
        {
            if (panelObject == null)
            {
                return;
            }

            panelObject.SetActive(visible);
            if (visible)
            {
                Refresh();
            }
        }

        private void TrySubscribeToSaveManager()
        {
            SaveManager manager = SaveManager.Instance;
            if (manager == null || subscribedSaveManager == manager)
            {
                return;
            }

            UnsubscribeFromSaveManager();
            subscribedSaveManager = manager;
            subscribedSaveManager.OnSaveLoaded += HandleSaveChanged;
            subscribedSaveManager.OnSaveChanged += HandleSaveChanged;
        }

        private void UnsubscribeFromSaveManager()
        {
            if (subscribedSaveManager == null)
            {
                return;
            }

            subscribedSaveManager.OnSaveLoaded -= HandleSaveChanged;
            subscribedSaveManager.OnSaveChanged -= HandleSaveChanged;
            subscribedSaveManager = null;
        }

        private void HandleSaveChanged(SaveData save)
        {
            Refresh(save);
        }

        private void Refresh()
        {
            SaveData save = SaveManager.Instance != null ? SaveManager.Instance.Current : null;
            Refresh(save);
        }

        private void Refresh(SaveData save)
        {
            if (contentText == null)
            {
                return;
            }

            builder.Length = 0;
            builder.AppendLine($"Non-Stage Debug Panel ({toggleKey}: toggle)");

            if (save == null)
            {
                builder.AppendLine("Save: not loaded");
                contentText.text = builder.ToString();
                return;
            }

            StageRuntimeData currentUnlocked = GetCurrentUnlockedStage(save);
            builder.Append("Current unlocked stage: ");
            AppendStageName(currentUnlocked, save.currentStageId);
            builder.AppendLine();

            builder.Append("Owned equipment: ");
            AppendOwnedEquipment(save);
            builder.AppendLine();

            builder.Append("Plates: ");
            AppendPlates(save);
            builder.AppendLine();

            builder.Append("Next reward: ");
            AppendNextReward(save);

            Stage01SalmonTimingScaffold stageTiming =
                FindAnyObjectByType<Stage01SalmonTimingScaffold>();
            if (stageTiming != null)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append(stageTiming.BuildDeveloperDebugText());
                if (panelRect != null)
                {
                    panelRect.sizeDelta = new Vector2(size.x, 460f);
                }
            }
            else if (panelRect != null)
            {
                panelRect.sizeDelta = size;
            }

            contentText.text = builder.ToString();
        }

        private static StageRuntimeData GetCurrentUnlockedStage(SaveData save)
        {
            List<StageRuntimeData> stages = ContentDefaults.CreateRewardStagesIncludingStageOneStub();
            StageRuntimeData latest = null;
            for (int i = 0; i < stages.Count; i++)
            {
                StageRuntimeData stage = stages[i];
                if (!save.IsStageUnlocked(stage.stageId))
                {
                    continue;
                }

                if (latest == null || stage.order > latest.order)
                {
                    latest = stage;
                }
            }

            return latest;
        }

        private void AppendStageName(StageRuntimeData stage, string fallbackStageId)
        {
            if (stage != null)
            {
                builder.Append(stage.order);
                builder.Append(". ");
                builder.Append(stage.displayName);
                builder.Append(" / ");
                builder.Append(stage.stageId);
                return;
            }

            builder.Append(string.IsNullOrWhiteSpace(fallbackStageId) ? "none" : fallbackStageId);
        }

        private void AppendOwnedEquipment(SaveData save)
        {
            if (save.ownedEquipmentIds == null || save.ownedEquipmentIds.Count == 0)
            {
                builder.Append("none");
                return;
            }

            bool appended = false;
            List<EquipmentRuntimeData> equipment = ContentDefaults.CreateEquipment();
            for (int i = 0; i < equipment.Count; i++)
            {
                string id = equipment[i].equipmentId.ToString();
                if (save.ownedEquipmentIds.Contains(id))
                {
                    AppendSeparator(ref appended);
                    builder.Append(equipment[i].displayName);
                }
            }

            for (int i = 0; i < save.ownedEquipmentIds.Count; i++)
            {
                string id = save.ownedEquipmentIds[i];
                if (TryParseEquipment(id, out _))
                {
                    continue;
                }

                AppendSeparator(ref appended);
                builder.Append(id);
            }
        }

        private void AppendPlates(SaveData save)
        {
            if (save.fishPlates == null || save.fishPlates.Count == 0)
            {
                builder.Append("none");
                return;
            }

            bool appended = false;
            for (int i = 0; i < save.fishPlates.Count; i++)
            {
                FishPlateStack stack = save.fishPlates[i];
                if (stack == null || stack.amount <= 0)
                {
                    continue;
                }

                AppendSeparator(ref appended);
                builder.Append(stack.fishType);
                builder.Append(" ");
                builder.Append(stack.amount);
            }

            if (!appended)
            {
                builder.Append("none");
            }
        }

        private void AppendNextReward(SaveData save)
        {
            StageRuntimeData stage = GetNextRewardStage(save);
            if (stage == null)
            {
                builder.Append("complete");
                return;
            }

            EquipmentRuntimeData equipment = ContentDefaults.FindEquipment(stage.rewardEquipment);
            builder.Append(string.IsNullOrWhiteSpace(equipment.displayName) ? stage.rewardEquipment.ToString() : equipment.displayName);
            builder.Append(" from ");
            builder.Append(stage.order);
            builder.Append(". ");
            builder.Append(stage.displayName);
            builder.Append(" (");
            builder.Append(GetRewardState(save, stage));
            builder.Append(")");
        }

        private static StageRuntimeData GetNextRewardStage(SaveData save)
        {
            StageRuntimeData available = ProgressionQuery.GetLatestClearedStageWithUnownedReward(save);
            if (available != null)
            {
                return available;
            }

            List<StageRuntimeData> stages = ContentDefaults.CreateRewardStagesIncludingStageOneStub();
            for (int i = 0; i < stages.Count; i++)
            {
                if (!save.HasEquipment(stages[i].rewardEquipment))
                {
                    return stages[i];
                }
            }

            return null;
        }

        private static string GetRewardState(SaveData save, StageRuntimeData stage)
        {
            int plates = save.GetPlates(stage.fishType);
            if (save.IsStageCleared(stage.stageId))
            {
                return plates >= stage.requiredPlatesForExchange
                    ? "exchange ready"
                    : $"needs {stage.requiredPlatesForExchange} {stage.fishType} plate";
            }

            if (save.IsStageUnlocked(stage.stageId))
            {
                return "clear stage first";
            }

            return "locked";
        }

        private static bool TryParseEquipment(string equipmentId, out EquipmentId parsed)
        {
            return Enum.TryParse(equipmentId, out parsed);
        }

        private void AppendSeparator(ref bool appended)
        {
            if (appended)
            {
                builder.Append(", ");
            }

            appended = true;
        }
    }
}
