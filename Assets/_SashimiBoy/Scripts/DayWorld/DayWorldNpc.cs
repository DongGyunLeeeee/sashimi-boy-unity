using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SashimiBoy
{
    [DefaultExecutionOrder(600)]
    public sealed class DayWorldNpc : MonoBehaviour, IInteractable, IInteractionAvailability
    {
        public string npcId, displayName;
        public int day = 1;
        public Transform faceAnchor;
        public Transform motorcycle;
        public DialogueRunner runner;
        private KevinFirstPersonCameraRig playerRig;
        private Camera view;
        private Vector3 viewPosition;
        private Quaternion viewRotation;
        private float viewFov;
        private bool inConversation;
        private Vector3 motorcycleRestPosition;
        private Renderer[] kevinHeads;
        private ShadowCastingMode[] headShadowModes;
        private Vector3 conversationPosition;
        private Quaternion conversationRotation;
        private float flinchPitch;
        public string Prompt => displayName + "와 대화";
        public bool IsAvailable => DayWorldFlow.Active && DayWorldRules.NpcAvailable(SaveManager.Instance.Current, day, npcId);

        public void Interact(GameObject actor)
        {
            if (!IsAvailable || DayWorldFlow.InputSuppressed || inConversation || runner == null || runner.IsRunning) return;
            var save = SaveManager.Instance.Current;
            if (save.dayWorld.day != day || DayWorldRules.RequiredNpc(save) != npcId)
            { DayWorldFlow.Instance.Notice(DayWorldRules.Objective(save)); return; }
            playerRig = actor.GetComponent<KevinFirstPersonCameraRig>();
            view = playerRig != null ? playerRig.controlledCamera : Camera.main;
            if (view == null || faceAnchor == null) return;
            viewPosition = view.transform.localPosition; viewRotation = view.transform.localRotation; viewFov = view.fieldOfView;
            DayWorldFlow.Instance.SetBusy(true);
            var body = actor.GetComponentInChildren<KevinBodyRig>(true);
            // The former fixed close-up could land behind/inside Kevin when he approached the NPC.
            // Pick a clear side of the conversation before leaving the first-person eyes.
            bool external = TryConversationPosition(actor, body, out conversationPosition);
            conversationRotation = Quaternion.LookRotation(faceAnchor.position - Vector3.up * .06f - conversationPosition);
            kevinHeads = body != null ? body.headRenderers : null;
            if (kevinHeads != null)
            {
                headShadowModes = new ShadowCastingMode[kevinHeads.Length];
                for (int i = 0; i < kevinHeads.Length; i++)
                {
                    if (kevinHeads[i] == null) continue;
                    headShadowModes[i] = kevinHeads[i].shadowCastingMode;
                    if (external) kevinHeads[i].shadowCastingMode = ShadowCastingMode.On;
                }
            }
            view.transform.SetPositionAndRotation(conversationPosition, conversationRotation);
            view.fieldOfView = 45f;
            inConversation = true;
            if (motorcycle != null) motorcycleRestPosition = motorcycle.localPosition;
            runner.OnDialogueFinished += Finished;
            runner.OnDialogueCancelled += Cancelled;
            runner.OnLineShown += LineShown;
            runner.Play(DayWorldScenario.Lines(npcId));
        }
        private bool TryConversationPosition(GameObject actor, KevinBodyRig body, out Vector3 position)
        {
            Vector3 target = faceAnchor.position - Vector3.up * .06f;
            Renderer[] bodyRenderers = body != null ? body.GetComponentsInChildren<Renderer>() : actor.GetComponentsInChildren<Renderer>();
            float side = Vector3.Dot(actor.transform.position - transform.position, transform.right) > 0f ? -1f : 1f;
            foreach (float distance in new[] { 1.95f, 1.35f })
                foreach (float angle in new[] { 35f, -35f, 55f, -55f, 75f, -75f, 15f, -15f, 90f, -90f })
                {
                    Vector3 direction = Quaternion.AngleAxis(angle * side, Vector3.up) * transform.forward;
                    Vector3 candidate = faceAnchor.position + direction * distance + Vector3.up * .035f;
                    Vector3 rayDirection = (target - candidate).normalized;
                    float rayLength = Vector3.Distance(candidate, target) - .20f;
                    bool blocked = false;
                    foreach (var renderer in bodyRenderers)
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        Bounds bounds = renderer.bounds; bounds.Expand(.20f);
                        if (bounds.Contains(candidate) || (bounds.IntersectRay(new Ray(candidate,rayDirection),out float hit) && hit < rayLength))
                        { blocked = true; break; }
                    }
                    if (blocked) continue;
                    foreach (var collider in Physics.OverlapSphere(candidate,.12f,~0,QueryTriggerInteraction.Ignore))
                        if (!collider.transform.IsChildOf(transform)) { blocked = true; break; }
                    if (blocked) continue;
                    foreach (var hit in Physics.SphereCastAll(candidate,.08f,rayDirection,rayLength,~0,QueryTriggerInteraction.Ignore))
                        if (!hit.transform.IsChildOf(transform)) { blocked = true; break; }
                    if (blocked) continue;
                    position = candidate; return true;
                }
            // In a confined space retain the actual first-person eyes instead of moving through a body or wall.
            position = view.transform.position; return false;
        }
        private void LateUpdate()
        {
            if (inConversation && view != null)
                view.transform.SetPositionAndRotation(conversationPosition, conversationRotation * Quaternion.Euler(flinchPitch,0f,0f));
        }
        private void LineShown(DialogueLine line)
        {
            if (line.kind != DialogueLineKind.Action) return;
            if (line.actionId == "motorcycle_stop" && motorcycle != null) StartCoroutine(StopMotorcycle());
            else if (line.actionId == "kevin_flinch") StartCoroutine(Flinch());
        }
        private IEnumerator StopMotorcycle()
        {
            Vector3 end = motorcycle.position, start = end - transform.right * 2.5f;
            float t = 0f;
            while (t < 1f && inConversation) { t += Time.unscaledDeltaTime; motorcycle.position = Vector3.Lerp(start,end,Mathf.SmoothStep(0f,1f,t)); yield return null; }
            motorcycle.position = end;
        }
        private IEnumerator Flinch()
        {
            float t=0f;
            while(t<.65f && inConversation) { t+=Time.unscaledDeltaTime; flinchPitch=Mathf.Sin(t*9f)*1.5f; yield return null; }
            flinchPitch=0f;
        }
        private void Finished()
        {
            if (!inConversation) return;
            bool changed = DayWorldRules.FinishDialogue(SaveManager.Instance.Current, npcId);
            Release();
            if (changed) DayWorldFlow.Instance.Commit();
        }
        private void Cancelled() => Release();
        private void OnDisable() { if (inConversation) { runner?.Cancel(); Release(); } }
        private void Release()
        {
            if (!inConversation) return;
            inConversation=false;
            if (runner != null) { runner.OnDialogueFinished-=Finished; runner.OnDialogueCancelled-=Cancelled; runner.OnLineShown-=LineShown; }
            StopAllCoroutines();
            flinchPitch=0f;
            if (kevinHeads != null && headShadowModes != null)
                for (int i=0;i<kevinHeads.Length;i++) if (kevinHeads[i]!=null) kevinHeads[i].shadowCastingMode=headShadowModes[i];
            kevinHeads=null;headShadowModes=null;
            if (motorcycle != null) motorcycle.localPosition=motorcycleRestPosition;
            if (view != null) { view.transform.localPosition=viewPosition; view.transform.localRotation=viewRotation; view.fieldOfView=viewFov; }
            if (DayWorldFlow.Instance != null) DayWorldFlow.Instance.SetBusy(false);
        }
    }
}
