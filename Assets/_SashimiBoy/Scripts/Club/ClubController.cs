using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class ClubController : MonoBehaviour
    {
        [Header("UI")]
        public Text titleText;
        public Text statusHeadlineText;
        public Text statusText;
        public Button performButton;
        public Button leaveButton;
        public int readyStatusFontSize = 19;
        public int missingStatusFontSize = 14;

        [Header("Scene")]
        public string streetSceneName = SashimiBoyConstants.Scenes.Street;
        public GameObject lockedLighting;
        public GameObject performanceLighting;
        public AudiencePulse[] audience;

        [Header("Prototype")]
        public float performancePreviewSeconds = 6f;

        private bool canPerform;
        private bool isPerforming;

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
            if (performButton != null)
            {
                performButton.onClick.AddListener(StartPerformancePreview);
            }

            if (leaveButton != null)
            {
                leaveButton.onClick.AddListener(LeaveClub);
            }
        }

        private void Start()
        {
            if (GameFlowManager.Instance != null)
            {
                GameFlowManager.Instance.SetLocation(GameLocation.Club);
            }

            Refresh();
        }

        private void HandleSaveChanged(SaveData save)
        {
            Refresh();
        }

        public void Refresh()
        {
            SaveData save = SaveManager.Instance != null ? SaveManager.Instance.Current : null;
            canPerform = ProgressionQuery.HasAllPerformanceEquipment(save);

            if (titleText != null)
            {
                titleText.text = "클럽";
            }

            if (statusText != null)
            {
                statusText.fontSize = canPerform ? readyStatusFontSize : missingStatusFontSize;
                statusText.text = statusHeadlineText != null
                    ? BuildStatusDetail(save)
                    : BuildStatusText(save);
            }

            if (statusHeadlineText != null)
            {
                statusHeadlineText.text = canPerform
                    ? "공연 가능"
                    : "공연 준비 필요";
                statusHeadlineText.color = canPerform
                    ? new Color(0.2f, 0.82f, 0.88f, 1f)
                    : new Color(1f, 0.68f, 0.2f, 1f);
            }

            if (performButton != null)
            {
                performButton.interactable = canPerform && !isPerforming;
                Text t = performButton.GetComponentInChildren<Text>();
                if (t != null)
                {
                    t.text = canPerform ? "공연 시작" : "장비 부족";
                }
            }

            if (lockedLighting != null)
            {
                lockedLighting.SetActive(!canPerform);
            }

            if (performanceLighting != null)
            {
                performanceLighting.SetActive(canPerform);
            }
        }

        public void StartPerformancePreview()
        {
            if (!canPerform || isPerforming)
            {
                Refresh();
                if (ToastUI.Instance != null)
                {
                    ToastUI.Instance.Show("필수 장비를 모두 모아야 공연할 수 있습니다.");
                }

                return;
            }

            StartCoroutine(PerformanceRoutine());
        }

        private string BuildStatusText(SaveData save)
        {
            if (save == null)
            {
                return "공연 불가\n세이브 데이터를 불러오는 중입니다.";
            }

            if (canPerform)
            {
                return "공연 가능\n필수 장비를 모두 보유했습니다.\n공연 시작은 placeholder만 재생됩니다.";
            }

            return "공연 불가\n부족한 필수 장비:\n" + BuildMissingEquipmentLines(save);
        }

        private string BuildStatusDetail(SaveData save)
        {
            if (save == null)
            {
                return "세이브 데이터를 불러오는 중입니다.";
            }

            if (canPerform)
            {
                return "필수 장비를 모두 보유했습니다.\n" +
                    "공연 시작은 placeholder만 재생됩니다.";
            }

            return "부족한 필수 장비\n" + BuildMissingEquipmentLines(save);
        }

        private string BuildMissingEquipmentLines(SaveData save)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            System.Collections.Generic.List<StageRuntimeData> stages = ContentDefaults.CreateRewardStagesIncludingStageOneStub();
            int missingCount = 0;

            for (int i = 0; i < stages.Count; i++)
            {
                StageRuntimeData stage = stages[i];
                if (save.HasEquipment(stage.rewardEquipment))
                {
                    continue;
                }

                EquipmentRuntimeData equipment = ContentDefaults.FindEquipment(stage.rewardEquipment);
                if (missingCount > 0)
                {
                    builder.AppendLine();
                }

                builder.Append("- ");
                builder.Append(GetEquipmentName(equipment, stage.rewardEquipment));
                builder.Append(" (");
                builder.Append(stage.displayName);
                builder.Append(" 보상)");
                missingCount++;
            }

            return missingCount == 0 ? "- 준비 완료" : builder.ToString();
        }

        private IEnumerator PerformanceRoutine()
        {
            isPerforming = true;
            if (performButton != null)
            {
                performButton.interactable = false;
            }

            if (statusText != null)
            {
                statusText.fontSize = readyStatusFontSize;
                statusText.text =
                    "케빈이 무대에 오르는 placeholder 연출을 확인합니다.";
            }

            if (statusHeadlineText != null)
            {
                statusHeadlineText.text = "공연 시작";
                statusHeadlineText.color =
                    new Color(0.95f, 0.18f, 0.16f, 1f);
            }

            for (int i = 0; i < audience.Length; i++)
            {
                if (audience[i] != null)
                {
                    audience[i].SetEnergy(1f);
                }
            }

            yield return new WaitForSeconds(performancePreviewSeconds);

            if (statusText != null)
            {
                statusText.fontSize = readyStatusFontSize;
                statusText.text = "실제 공연 스테이지는 이후 연결됩니다.";
            }

            if (statusHeadlineText != null)
            {
                statusHeadlineText.text = "공연 종료";
            }

            for (int i = 0; i < audience.Length; i++)
            {
                if (audience[i] != null)
                {
                    audience[i].SetEnergy(0.25f);
                }
            }

            isPerforming = false;
            Refresh();
        }

        public void LeaveClub()
        {
            if (SceneTransitionService.Instance != null)
            {
                SceneTransitionService.Instance.LoadScene(streetSceneName);
            }
        }

        private string GetEquipmentName(EquipmentRuntimeData equipment, EquipmentId fallback)
        {
            return string.IsNullOrWhiteSpace(equipment.displayName) ? fallback.ToString() : equipment.displayName;
        }
    }
}
