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
            if(completeRoot != null) completeRoot.SetActive(active && save.dayWorld.beat==DayWorldBeat.Complete);
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
            var save=SaveManager.Instance.Current;
            if(!DayWorldFlow.Active || DayWorldFlow.InputSuppressed) return;
            if(save.dayWorld.beat != DayWorldBeat.Wake && save.dayWorld.beat != DayWorldBeat.Sleep)
            { DayWorldFlow.Instance.Notice(DayWorldRules.Objective(save)); return; }
            StartCoroutine(Rest(actor,save.dayWorld.beat==DayWorldBeat.Wake));
        }
        private IEnumerator Rest(GameObject actor, bool waking)
        {
            DayWorldFlow.Instance.SetBusy(true);
            var camera=actor.GetComponent<KevinFirstPersonCameraRig>()?.controlledCamera;
            Vector3 original=camera != null ? camera.transform.localPosition : Vector3.zero;
            float t=0f;
            while(t<1.5f)
            {
                t+=Time.unscaledDeltaTime;
                float progress=Mathf.SmoothStep(0f,1f,t/1.5f);
                if(fade != null) fade.alpha=waking ? 1f-progress : progress;
                if(camera != null && waking) camera.transform.localPosition=original+Vector3.down*(1f-progress)*.5f;
                yield return null;
            }
            if(camera != null) camera.transform.localPosition=original;
            bool changed=waking ? DayWorldRules.Wake(SaveManager.Instance.Current) : DayWorldRules.Sleep(SaveManager.Instance.Current);
            DayWorldFlow.Instance.SetBusy(false);
            if(changed) DayWorldFlow.Instance.Commit();
            if(waking) { if(fade != null) fade.alpha=0f; }
            else DayWorldFlow.Instance.LoadWorld(DayWorldRules.Home,"Wake");
        }
    }

}
