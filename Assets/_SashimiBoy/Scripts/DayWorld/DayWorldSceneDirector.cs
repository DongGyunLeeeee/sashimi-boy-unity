using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    public sealed class DayWorldSceneDirector : MonoBehaviour
    {
        public Transform[] spawns;
        public Text dayText, objectiveText;
        public GameObject menuRoot, completeRoot, shopRoot;
        public GameObject explorationControls;
        public GameObject optionalVenuePanel;
        public CanvasGroup fade;
        public Button newGameButton, continueButton;
        public KevinCustomizationScreen customization;
        public DayWorldStageClearScreen stageClearScreen;
        public DayWorldNpc[] npcs;
        public HomeEquipmentStation[] equipmentStations;
        public bool ShopOpen => shopRoot != null && shopRoot.activeSelf;
        private DialogueRunner[] sceneRunners;
        private GameObject[] legacyNpcRoots;

        private void Awake()
        {
            newGameButton?.onClick.AddListener(() =>
            {
                if (customization != null) customization.Open();
                else DayWorldFlow.Instance.NewGame();
            });
            continueButton?.onClick.AddListener(()=>DayWorldFlow.Instance.ContinueGame());
            if (shopRoot != null) shopRoot.SetActive(false);
        }
        private void OnEnable()
        {
            if (SaveManager.Instance != null) SaveManager.Instance.OnSaveChanged += Refresh;
        }
        private void OnDisable()
        {
            if (SaveManager.Instance != null) SaveManager.Instance.OnSaveChanged -= Refresh;
            if(sceneRunners != null) foreach(var runner in sceneRunners) if(runner != null)
            {runner.OnDialogueStarted-=LockForDialogue;runner.OnDialogueFinished-=UnlockAfterDialogue;runner.OnDialogueCancelled-=UnlockAfterDialogue;}
        }
        private void Start()
        {
            legacyNpcRoots = gameObject.scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<Transform>(true))
                .Where(t=>t.GetComponent<DialogueTrigger>() != null || t.name == "Boss_Visual" ||
                    t.name.StartsWith("Audience_") && int.TryParse(t.name.Substring(9),out _))
                .Select(t=>t.gameObject).ToArray();
            sceneRunners=FindObjectsByType<DialogueRunner>();
            foreach (var runner in sceneRunners)
            {
                runner.OnDialogueStarted += LockForDialogue;
                runner.OnDialogueFinished += UnlockAfterDialogue;
                runner.OnDialogueCancelled += UnlockAfterDialogue;
            }
            if (DayWorldFlow.Active && SaveManager.Instance.Current.dayWorld.pendingStageClear == 0 && DayWorldRules.Wake(SaveManager.Instance.Current))
                DayWorldFlow.Instance.Commit();
            Refresh(SaveManager.Instance.Current);
            if(optionalVenuePanel != null && DayWorldFlow.Active) optionalVenuePanel.SetActive(false);
            if (menuRoot != null) { Cursor.lockState=CursorLockMode.None; Cursor.visible=true; }
            if (DayWorldFlow.Active || menuRoot != null)
                foreach (var debug in FindObjectsByType<PrototypeDebugPanel>()) debug.enabled=false;
        }
        private void LockForDialogue() { if (DayWorldFlow.Active) DayWorldFlow.Instance.SetBusy(true); }
        private void UnlockAfterDialogue() { if (DayWorldFlow.Active) DayWorldFlow.Instance.SetBusy(false); }
        private void Update()
        {
            if(explorationControls != null) explorationControls.SetActive(!DayWorldFlow.InputSuppressed && !DayWorldFlow.AwaitingWake);
            if (ShopOpen && Input.GetKeyDown(KeyCode.Escape)) CloseShop();
        }
        public Transform FindSpawn(string id)
        {
            if (spawns != null) foreach(var spawn in spawns) if(spawn != null && spawn.name == id) return spawn;
            return spawns != null && spawns.Length > 0 ? spawns[0] : null;
        }
        public void Refresh(SaveData save)
        {
            bool active=DayWorldRules.Active(save);
            if(dayText != null) dayText.text=active ? (save.dayWorld.day==1 ? "첫째 날" : "둘째 날") : "SASHIMI BOY";
            if(objectiveText != null) objectiveText.text=DayWorldRules.Objective(save);
            if(continueButton != null) continueButton.interactable=active;
            if (stageClearScreen != null) stageClearScreen.Refresh(save);
            else if (completeRoot != null) completeRoot.SetActive(false);
            if(npcs != null) foreach(var npc in npcs) if(npc != null) npc.gameObject.SetActive(DayWorldRules.NpcAvailable(save,npc.day,npc.npcId));
            if(equipmentStations != null) foreach(var station in equipmentStations) if(station != null) station.Refresh();
            SetLabelVisible("BedLabel", DayWorldRules.BedAvailable(save));
            SetLabelVisible("ShopPrompt", DayWorldRules.ShopAvailable(save));
            SetLabelVisible("VenueAssets/Owner_EquipmentShopOwner", DayWorldRules.ShopAvailable(save));
            SetLabelVisible("VenueAssets/Owner_EquipmentShopOwner_Collision", DayWorldRules.ShopAvailable(save));
            if (active && legacyNpcRoots != null) foreach (var legacy in legacyNpcRoots) legacy.SetActive(false);
            // The existing wall generator keeps label backings under InteriorShell, not under each label.
            // Hide those matching plaques too, so unavailable stations leave no floating black panels.
            var shell = transform.Find("InteriorShell");
            if (shell != null)
            {
                var labels = GetComponentsInChildren<TextMesh>(true);
                foreach (var backing in shell.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("LabelPlaque")))
                {
                    var label = labels.FirstOrDefault(t=>Vector3.Distance(t.transform.position,backing.position)<.1f);
                    if (label != null) backing.gameObject.SetActive(label.gameObject.activeInHierarchy);
                }
            }
        }
        private void SetLabelVisible(string name, bool visible)
        {
            var label = transform.Find(name);
            if (label != null) label.gameObject.SetActive(visible);
        }
        public void OpenShop()
        {
            if(!DayWorldFlow.Active || !DayWorldRules.ShopAvailable(SaveManager.Instance.Current) || DayWorldFlow.InputSuppressed || shopRoot == null) return;
            shopRoot.SetActive(true);
            shopRoot.GetComponent<EquipmentShopController>()?.Refresh();
            DayWorldFlow.Instance.SetBusy(true);
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
        }
        public void CloseShop()
        {
            if(shopRoot != null) shopRoot.SetActive(false);
            DayWorldFlow.Instance.SetBusy(false);
        }
        public void UseBed(GameObject actor)
        {
            var save = SaveManager.Instance.Current;
            if (!DayWorldRules.BedAvailable(save) || DayWorldFlow.InputSuppressed) return;
            StartCoroutine(Rest());
        }
        private IEnumerator Rest()
        {
            DayWorldFlow.Instance.SetBusy(true);
            float elapsed = 0f;
            while (elapsed < 1.2f)
            {
                elapsed += Time.unscaledDeltaTime;
                if (fade != null) fade.alpha = Mathf.SmoothStep(0f, 1f, elapsed / 1.2f);
                yield return null;
            }
            bool changed = DayWorldRules.Sleep(SaveManager.Instance.Current);
            if (changed)
            {
                try { DayWorldFlow.Instance.Commit(); }
                catch (System.Exception error) when (error is System.IO.IOException || error is System.UnauthorizedAccessException)
                { stageClearScreen?.ShowSaveFailure(); }
            }
            else DayWorldFlow.Instance.SetBusy(false);
            if (fade != null) fade.alpha = 0f;
        }
    }
}
