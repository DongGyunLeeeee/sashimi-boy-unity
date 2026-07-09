using UnityEngine;

namespace SashimiBoy
{
    public sealed class StageStarterInteractable : MonoBehaviour, IInteractable
    {
        public string prompt = "스테이지 시작";
        public string stageId = SashimiBoyConstants.StageIds.Salmon;
        public string stageSceneName = SashimiBoyConstants.Scenes.FishStageTemplate;
        public bool loadStageScene = false;

        public string Prompt => prompt;

        public void Interact(GameObject actor)
        {
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
