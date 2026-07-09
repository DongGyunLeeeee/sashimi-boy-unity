using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class ClubController : MonoBehaviour
    {
        [Header("UI")]
        public Text titleText;
        public Text statusText;
        public Button performButton;
        public Button leaveButton;

        [Header("Scene")]
        public string streetSceneName = SashimiBoyConstants.Scenes.Street;
        public GameObject lockedLighting;
        public GameObject performanceLighting;
        public AudiencePulse[] audience;

        [Header("Prototype")]
        public float performancePreviewSeconds = 6f;

        private bool canPerform;
        private bool isPerforming;

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
                statusText.text = canPerform
                    ? "장비는 준비됐다. 이제 무대에 오르면 된다."
                    : $"아직 무대는 멀다. 부족한 장비: {ProgressionQuery.BuildMissingEquipmentText(save)}";
            }

            if (performButton != null)
            {
                performButton.interactable = canPerform && !isPerforming;
                Text t = performButton.GetComponentInChildren<Text>();
                if (t != null)
                {
                    t.text = canPerform ? "공연 시작" : "공연 불가";
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
                return;
            }

            StartCoroutine(PerformanceRoutine());
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
                statusText.text = "케빈이 무대 위로 올라간다…";
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
                statusText.text = "공연 프로토타입 종료. 엔딩/공연 리듬파트는 이후 연결.";
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
    }
}
