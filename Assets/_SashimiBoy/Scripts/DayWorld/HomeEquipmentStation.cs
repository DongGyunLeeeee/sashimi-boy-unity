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
        private float bodyLeanDegrees;
        private Vector3 bodyLocalPosition, preferredBodyPosition;
        private Quaternion bodyLocalRotation;
        public bool IsPracticing => practicing;
        public bool IsAvailable => !practicing && DayWorldFlow.Active && DayWorldRules.EquipmentAvailable(SaveManager.Instance.Current, equipmentId);
        public string Prompt => ContentDefaults.FindEquipment(equipmentId).displayName +
            (DayWorldFlow.Active && SaveManager.Instance.Current.dayWorld.placedEquipment.Contains(equipmentId.ToString()) ? " 연습하기" : " 배치하기");
        public void Refresh()
        {
            if(equipmentVisual != null) equipmentVisual.SetActive(DayWorldFlow.Active &&
                DayWorldRules.CanPracticeEquipment(SaveManager.Instance.Current, equipmentId));
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
            if(!DayWorldRules.CanPracticeEquipment(save, equipmentId))
            { DayWorldFlow.Instance.Notice(DayWorldRules.Objective(save)); return; }
            StartCoroutine(Practice(actor));
        }
        private IEnumerator Practice(GameObject actor)
        {
            var rig=actor.GetComponent<KevinFirstPersonCameraRig>();
            body=actor.GetComponentInChildren<KevinBodyRig>(true);
            view=rig != null ? rig.controlledCamera : Camera.main;
            if(body==null || view==null || playerPosition==null || rightHandTarget==null || leftHandTarget==null || practiceCamera==null) yield break;
            var practiceSave=SaveManager.Instance.Current;
            int practiceDay=practiceSave.dayWorld.day;
            bool requiredAtStart=DayWorldRules.IsRequiredPractice(practiceSave,equipmentId);
            practicing=true;
            DayWorldFlow.Instance.SetBusy(true);
            Refresh();
            cameraPosition=view.transform.localPosition; cameraRotation=view.transform.localRotation; cameraFov=view.fieldOfView;
            bodyLocalPosition=body.transform.localPosition; bodyLocalRotation=body.transform.localRotation;
            bodyLeanDegrees=body.workingLeanDegrees;
            var controller=actor.GetComponent<CharacterController>();
            bool controllerEnabled=controller != null && controller.enabled;
            if(controllerEnabled) controller.enabled=false;
            actor.transform.position=playerPosition.position;
            if(controllerEnabled) controller.enabled=true;
            body.transform.rotation=playerPosition.rotation;
            preferredBodyPosition=body.transform.position;
            body.SetHeadHidden(false);
            body.working=true;
            body.PrepareInstrumentPoseAtRoot(preferredBodyPosition,
                rightHandTarget.position+Vector3.up*.03f, leftHandTarget.position+Vector3.up*.03f);
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
                body.ApplyWorkingPoseAtRoot(preferredBodyPosition);
                yield return null;
            }
            // Free practice never completes a new day's objective, even if an
            // external load changes the save while the animation is running.
            bool changed=requiredAtStart && ReferenceEquals(practiceSave,SaveManager.Instance.Current) &&
                practiceSave.dayWorld.day==practiceDay && DayWorldRules.Practice(practiceSave,equipmentId);
            Cleanup();
            if(changed) DayWorldFlow.Instance.Commit();
        }
        private void Cleanup()
        {
            if(!practicing) return;
            practicing=false;
            if(body != null)
            {
                body.working=false; body.SetHeadHidden(true);
                body.workingLeanDegrees=bodyLeanDegrees;
                body.transform.localPosition=bodyLocalPosition; body.transform.localRotation=bodyLocalRotation;
            }
            if(view != null) { view.transform.localPosition=cameraPosition; view.transform.localRotation=cameraRotation; view.fieldOfView=cameraFov; }
            if(DayWorldFlow.Instance != null) DayWorldFlow.Instance.SetBusy(false);
            Refresh();
        }
        public void CancelPractice() { StopAllCoroutines(); Cleanup(); }
        private void OnDisable() { CancelPractice(); }
    }
}
