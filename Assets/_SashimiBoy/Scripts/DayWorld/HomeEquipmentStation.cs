using System.Collections;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class HomeEquipmentStation : MonoBehaviour, IInteractable, IInteractionAvailability
    {
        public EquipmentId equipmentId;
        public GameObject equipmentVisual;
        public Transform playerPosition, rightHandTarget, leftHandTarget, practiceCamera;
        public float practiceSeconds=4.5f;
        private bool practicing;
        private KevinBodyRig body;
        private Camera view;
        private Vector3 cameraPosition;
        private Quaternion cameraRotation;
        private float cameraFov;
        public bool IsPracticing => practicing;
        public bool IsAvailable => !practicing && DayWorldFlow.Active && DayWorldRules.EquipmentAvailable(SaveManager.Instance.Current, equipmentId);
        public string Prompt => ContentDefaults.FindEquipment(equipmentId).displayName +
            (DayWorldFlow.Active && SaveManager.Instance.Current.dayWorld.placedEquipment.Contains(equipmentId.ToString()) ? " 연습하기" : " 배치하기");
        public void Refresh()
        {
            if(equipmentVisual != null) equipmentVisual.SetActive(DayWorldFlow.Active &&
                SaveManager.Instance.Current.dayWorld.placedEquipment.Contains(equipmentId.ToString()));
            var label = transform.Find("StationName");
            if (label != null) label.gameObject.SetActive(IsAvailable);
            foreach (var collider in GetComponents<Collider>()) collider.enabled = IsAvailable;
        }
        private void Start() => Refresh();
        public void Interact(GameObject actor)
        {
            if(!IsAvailable || DayWorldFlow.InputSuppressed) return;
            var save=SaveManager.Instance.Current;
            if(DayWorldRules.Place(save,equipmentId)) { DayWorldFlow.Instance.Commit(); Refresh(); return; }
            if(save.dayWorld.beat != DayWorldBeat.Practice || DayWorldRules.Equipment(save) != equipmentId ||
               !save.dayWorld.placedEquipment.Contains(equipmentId.ToString()))
            { DayWorldFlow.Instance.Notice(DayWorldRules.Objective(save)); return; }
            StartCoroutine(Practice(actor));
        }
        private IEnumerator Practice(GameObject actor)
        {
            var rig=actor.GetComponent<KevinFirstPersonCameraRig>();
            body=actor.GetComponentInChildren<KevinBodyRig>(true);
            view=rig != null ? rig.controlledCamera : Camera.main;
            if(body==null || view==null || playerPosition==null || rightHandTarget==null || leftHandTarget==null || practiceCamera==null) yield break;
            practicing=true;
            DayWorldFlow.Instance.SetBusy(true);
            cameraPosition=view.transform.localPosition; cameraRotation=view.transform.localRotation; cameraFov=view.fieldOfView;
            var controller=actor.GetComponent<CharacterController>();
            if(controller != null) controller.enabled=false;
            actor.transform.position=playerPosition.position;
            if(controller != null) controller.enabled=true;
            body.transform.rotation=playerPosition.rotation;
            body.SetHeadHidden(false);
            body.working=true;
            view.transform.SetPositionAndRotation(practiceCamera.position,practiceCamera.rotation);
            view.fieldOfView=52f;
            float elapsed=0f;
            while(elapsed<practiceSeconds)
            {
                if(Input.GetKeyDown(KeyCode.Escape)) { Cleanup(); yield break; }
                elapsed+=Time.unscaledDeltaTime;
                // Keep this shot fixed if the CharacterController settles onto the floor.
                view.transform.SetPositionAndRotation(practiceCamera.position,practiceCamera.rotation);
                float stroke=Mathf.Sin(elapsed*10f);
                body.rightTarget=rightHandTarget.position+Vector3.up*(.03f+Mathf.Max(0f,stroke)*.08f);
                body.leftTarget=leftHandTarget.position+Vector3.up*(.03f+Mathf.Max(0f,-stroke)*.07f);
                body.rightRotation=body.transform.rotation*body.right.handWorldRest;
                body.leftRotation=body.transform.rotation*body.left.handWorldRest;
                yield return null;
            }
            bool changed=DayWorldRules.Practice(SaveManager.Instance.Current,equipmentId);
            Cleanup();
            if(changed) DayWorldFlow.Instance.Commit();
        }
        private void Cleanup()
        {
            if(!practicing) return;
            practicing=false;
            if(body != null) { body.working=false; body.SetHeadHidden(true); }
            if(view != null) { view.transform.localPosition=cameraPosition; view.transform.localRotation=cameraRotation; view.fieldOfView=cameraFov; }
            if(DayWorldFlow.Instance != null) DayWorldFlow.Instance.SetBusy(false);
        }
        private void OnDisable() { StopAllCoroutines(); Cleanup(); }
    }
}
