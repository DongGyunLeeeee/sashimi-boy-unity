using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class DayWorldStageClearScreen : MonoBehaviour
    {
        public Text title, description, saveStatus;
        public Button continueButton, saveAndExitButton;

        private void Awake()
        {
            continueButton.onClick.AddListener(Continue);
            saveAndExitButton.onClick.AddListener(SaveAndExit);
        }

        public void Refresh(SaveData save)
        {
            bool visible = DayWorldRules.Active(save) && save.dayWorld.pendingStageClear > 0;
            bool wasVisible = gameObject.activeSelf;
            gameObject.SetActive(visible);
            if (!visible) return;
            int stage = save.dayWorld.pendingStageClear;
            title.text = stage + "스테이지 클리어";
            description.text = stage == 1 ? "오늘의 일과를 마쳤습니다. 다음 날을 시작해 볼까요?" : "다음 스테이지를 준비하고 있습니다. 진행 상황을 저장하고 나갈 수 있습니다.";
            continueButton.interactable = stage == 1;
            continueButton.GetComponentInChildren<Text>().text = stage == 1 ? "이어서 하기" : "다음 스테이지 준비 중";
            DayWorldFlow.Instance.SetBusy(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (!wasVisible)
            {
                saveStatus.text = "";
                EventSystem.current?.SetSelectedGameObject((continueButton.interactable ? continueButton : saveAndExitButton).gameObject);
            }
        }

        public void Continue()
        {
            var save = SaveManager.Instance.Current;
            int pending = save.dayWorld.pendingStageClear;
            var beat = save.dayWorld.beat;
            if (!DayWorldRules.ContinueAfterStageClear(save)) return;
            try { DayWorldFlow.Instance.Commit(); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                save.dayWorld.pendingStageClear = pending;
                save.dayWorld.beat = beat;
                Refresh(save);
                ShowSaveFailure();
                return;
            }
            DayWorldFlow.Instance.SetBusy(false);
            DayWorldFlow.Instance.LoadWorld(DayWorldRules.Home, "Wake");
        }

        public bool SaveCheckpointForExit()
        {
            try
            {
                SaveManager.Instance.Save();
                saveStatus.text = "진행 상황을 저장했습니다.";
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                ShowSaveFailure();
                return false;
            }
        }

        public void ShowSaveFailure()
        {
            saveStatus.text = "저장하지 못했습니다. 저장 공간과 권한을 확인한 뒤 다시 시도해 주세요.";
        }

        private void SaveAndExit()
        {
            if (!SaveCheckpointForExit()) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
