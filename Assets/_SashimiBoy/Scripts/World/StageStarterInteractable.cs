using UnityEngine;

namespace SashimiBoy
{
    public sealed class StageStarterInteractable : MonoBehaviour, IInteractable, IInteractionAvailability
    {
        public string prompt = "스테이지 시작";
        public string stageId = SashimiBoyConstants.StageIds.Salmon;
        public string stageSceneName = SashimiBoyConstants.Scenes.FishStageTemplate;
        public bool loadStageScene = false;
        public bool IsAvailable => !DayWorldFlow.Active || DayWorldRules.CanStart(SaveManager.Instance.Current,
            DayWorldRules.StageId(SaveManager.Instance.Current));

        public string Prompt => DayWorldFlow.Active
            ? (SaveManager.Instance.Current.dayWorld.day == 1 ? "연어 손질 시작" : "우럭 손질 시작")
            : stageId == SashimiBoyConstants.StageIds.Salmon && loadStageScene ? "연어 손질 시작" : prompt;

        private void Start()
        {
            StartCoroutine(Stage01ShopReturn.RestoreAt(this));
        }

        public void Interact(GameObject actor)
        {
            if (!IsAvailable) return;
            if (DayWorldFlow.Active) { DayWorldFlow.Instance.EnterWork(actor, this); return; }
            if (SceneTransitionService.Instance != null && SceneTransitionService.Instance.IsLoading) return;
            if (loadStageScene && stageId == SashimiBoyConstants.StageIds.Salmon)
                Stage01ShopReturn.Capture(actor);
            if (GameFlowManager.Instance != null)
            {
                GameFlowManager.Instance.RequestStage(stageId);
            }

            if (loadStageScene)
            {
                if (SceneTransitionService.Instance != null)
                {
                    SceneTransitionService.Instance.LoadScene(stageSceneName);
                }
            }
            else
            {
                string message = "1스테이지 음악/비트맵이 들어오면 여기에서 FishStageTemplate로 연결합니다.";
                if (ToastUI.Instance != null)
                {
                    ToastUI.Instance.Show(message);
                }
                else
                {
                    Debug.Log(message);
                }
            }
        }
    }
}
