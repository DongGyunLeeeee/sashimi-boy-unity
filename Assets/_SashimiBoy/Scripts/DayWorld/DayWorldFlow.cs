using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy
{
    [DefaultExecutionOrder(-9300)]
    public sealed class DayWorldFlow : MonoBehaviour
    {
        public static DayWorldFlow Instance { get; private set; }
        public static bool Active => DayWorldRules.Active(SaveManager.Instance != null ? SaveManager.Instance.Current : null);
        public static bool AwaitingWake => Active && SaveManager.Instance.Current.dayWorld.beat == DayWorldBeat.Wake;
        public static bool InputSuppressed => Instance != null && (Instance.busy || Time.unscaledTime < Instance.holdUntil);
        public bool Busy => busy;
        private bool busy;
        private float holdUntil;
        private string requestedSpawn;
        private SaveData Save => SaveManager.Instance.Current;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            SceneManager.sceneLoaded += SceneLoaded;
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (Instance == this) Instance = null;
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            busy = false;
            SuppressInput();
            if (!Active) return;
            if (scene.name == SashimiBoyConstants.Scenes.Stage01Salmon || scene.name == DayWorldRules.StageTwoScene)
            {
                if (FindAnyObjectByType<DayWorldStageBridge>() == null) new GameObject("DayWorldStageBridge").AddComponent<DayWorldStageBridge>();
                return;
            }
            if (scene.name != SashimiBoyConstants.Scenes.Bootstrap)
            {
                Save.dayWorld.checkpointScene = scene.name;
                if (!string.IsNullOrEmpty(requestedSpawn)) Save.dayWorld.checkpointSpawn = requestedSpawn;
                SaveManager.Instance.RaiseChanged();
                StartCoroutine(RestoreSpawn(requestedSpawn));
                requestedSpawn = null;
            }
        }
        private IEnumerator RestoreSpawn(string id)
        {
            yield return null;
            if (string.IsNullOrEmpty(id)) yield break; // Stage1 failure return retains its existing captured entry pose.
            var director = FindAnyObjectByType<DayWorldSceneDirector>();
            var player = FindAnyObjectByType<SimpleTopDownPlayerController>();
            if (director == null || player == null) yield break;
            Transform spawn = director.FindSpawn(id);
            if (spawn == null) yield break;
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.transform.position = spawn.position;
            if (controller != null) controller.enabled = true;
            player.GetComponent<KevinFirstPersonCameraRig>()?.RestoreStageEntryView(spawn.eulerAngles.y-player.transform.eulerAngles.y, spawn.eulerAngles.x);
            SetBusy(busy); // Restoring the camera must retain a resumed clear screen's input lock.
            SuppressInput();
        }
        public void NewGame()
        {
            NewGameWithFace("CuteFace");
        }
        public void NewGameWithFace(string faceId)
        {
            if (busy || SceneTransitionService.Instance.IsLoading) return;
            SaveManager.Instance.StartDayWorldNewGameWithFace(faceId);
            LoadWorld(DayWorldRules.Home, "Wake");
        }
        public void ContinueGame()
        {
            if (!Active || busy || SceneTransitionService.Instance.IsLoading) return;
            // Resume a saved clear screen before advancing, and migrate the former manual wake checkpoint.
            if (Save.dayWorld.pendingStageClear > 0) { LoadWorld(DayWorldRules.Home, "Wake"); return; }
            if (AwaitingWake)
            {
                DayWorldRules.Wake(Save);
                Commit();
                LoadWorld(DayWorldRules.Home, "Wake");
                return;
            }
            string scene = Save.dayWorld.checkpointScene;
            if (scene == SashimiBoyConstants.Scenes.Stage01Salmon || scene == DayWorldRules.StageTwoScene) scene = SashimiBoyConstants.Scenes.FishShopDialogue;
            LoadWorld(scene, Save.dayWorld.checkpointSpawn);
        }
        public void ReturnToTitle()
        {
            if (SceneTransitionService.Instance.IsLoading) return;
            // Keep the saved checkpoint and pending clear. Title navigation is not a world
            // door, so it must also work while Day1's next-morning Wake gate is pending.
            requestedSpawn = null;
            SetBusy(true);
            SuppressInput();
            SceneTransitionService.Instance.LoadScene(SashimiBoyConstants.Scenes.Bootstrap);
        }
        public void LoadWorld(string scene, string spawn = "Entry")
        {
            if (busy || SceneTransitionService.Instance.IsLoading || string.IsNullOrWhiteSpace(scene)) return;
            if (AwaitingWake && scene != DayWorldRules.Home) return;
            if (!Application.CanStreamedLevelBeLoaded(scene)) { Notice("이 장소에는 아직 들어갈 수 없습니다."); return; }
            requestedSpawn = spawn;
            SuppressInput();
            SceneTransitionService.Instance.LoadScene(scene);
        }
        public bool EnterWork(GameObject actor, StageStarterInteractable starter)
        {
            if (!Active || busy || Time.unscaledTime < holdUntil || SceneTransitionService.Instance.IsLoading) return false;
            string stageId = DayWorldRules.StageId(Save);
            if (!DayWorldRules.CanStart(Save, stageId)) { Notice(DayWorldRules.Objective(Save)); return false; }
            if (SceneManager.GetActiveScene().name != SashimiBoyConstants.Scenes.FishShopDialogue) return false;
            string scene = DayWorldRules.StageScene(Save);
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                Notice("지금은 손질을 시작할 수 없습니다. 현재 진행은 저장되어 있습니다.");
                return false;
            }
            Stage01ShopReturn.Capture(actor);
            GameFlowManager.Instance.RequestStage(stageId);
            Save.dayWorld.checkpointScene = SashimiBoyConstants.Scenes.FishShopDialogue;
            Save.dayWorld.checkpointSpawn = "StageReturn";
            SaveManager.Instance.RaiseChanged();
            SuppressInput();
            SceneTransitionService.Instance.LoadScene(scene);
            return true;
        }
        public static bool AcceptClear(StageClearPayload payload, string pendingStageId)
        {
            if (!Active) return true;
            var save = SaveManager.Instance.Current;
            return payload != null && DayWorldRules.CanRecordClear(save, payload.stageId) && pendingStageId == payload.stageId &&
                   SceneManager.GetActiveScene().name == DayWorldRules.StageScene(save);
        }
        public void SetBusy(bool value)
        {
            busy = value;
            foreach (var rig in FindObjectsByType<KevinFirstPersonCameraRig>()) rig.SetUiBlocked(value);
            if (!value) SuppressInput();
        }
        public void SuppressInput(float seconds = .28f) => holdUntil = Mathf.Max(holdUntil, Time.unscaledTime + seconds);
        public void Commit() { SaveManager.Instance.RaiseChanged(); SuppressInput(); }
        public void Notice(string text) { if (ToastUI.Instance != null) ToastUI.Instance.Show(text); }
    }

}
