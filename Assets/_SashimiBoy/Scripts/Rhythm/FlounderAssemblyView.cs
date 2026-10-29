using System;
using UnityEngine;

namespace SashimiBoy
{
    public enum FlounderBodyState
    {
        WholeFish, HeadRemovedFish, FilletRemoved1, FilletRemoved2,
        FilletRemoved3, FilletRemoved4, Hidden
    }

    // Presentation only. Stage03 gameplay decides when a cut is valid and
    // owns note input, slice counts, current fillet, rewards and result flow.
    [DisallowMultipleComponent]
    public sealed class FlounderAssemblyView : MonoBehaviour
    {
        public Transform bodyPivot;
        public Transform detachedPartsRoot;
        public GameObject[] bodyStates = Array.Empty<GameObject>();
        public FlounderAssemblyPart head;
        public FlounderAssemblyPart[] fillets = Array.Empty<FlounderAssemblyPart>();
        public Transform headStorageAnchor;
        public Transform[] filletStorageAnchors = Array.Empty<Transform>();
        public Transform filletWorkAnchor;
        public Transform knifeAttachmentAnchor;
        public Transform handAttachmentAnchor;
        public Transform sashimiOutputAnchor;
        public Transform plateOutputAnchor;
        public GameObject sashimiSlicePrefab;
        [Min(0.01f)] public float turnOverDuration = 0.7f;

        public FlounderBodyState BodyState { get; private set; }
        public int DetachedFilletCount { get; private set; }
        public bool IsBackFacing { get; private set; }
        public bool IsTurningOver { get; private set; }
        public bool HeadRemoved => head != null && head.IsDetached;
        public bool HasBody => BodyState != FlounderBodyState.Hidden;

        private bool initialized;
        private Quaternion authoredBodyRotation;
        private float turnElapsed;

        private void Awake() => ResetAssembly();
        private void Update() => AdvancePresentation(Time.deltaTime);

        private void Initialize()
        {
            if (initialized) return;
            if (bodyPivot == null || detachedPartsRoot == null || head == null ||
                bodyStates.Length != 6 || fillets.Length != 4 || filletStorageAnchors.Length != 4)
                throw new InvalidOperationException("Incomplete Stage03 flounder assembly binding.");
            for (int i = 0; i < bodyStates.Length; i++)
                if (bodyStates[i] == null) throw new InvalidOperationException("Missing flounder body state " + i);
            for (int i = 0; i < fillets.Length; i++)
                if (fillets[i] == null || filletStorageAnchors[i] == null)
                    throw new InvalidOperationException("Missing independent flounder fillet " + i);
            if (headStorageAnchor == null) throw new InvalidOperationException("Missing head storage anchor.");
            authoredBodyRotation = bodyPivot.localRotation;
            head.storageAnchor = headStorageAnchor;
            head.CaptureAuthoredPose();
            for (int i = 0; i < fillets.Length; i++)
            {
                fillets[i].storageAnchor = filletStorageAnchors[i];
                fillets[i].CaptureAuthoredPose();
            }
            initialized = true;
        }

        public bool TryRemoveHead()
        {
            Initialize();
            if (BodyState != FlounderBodyState.WholeFish || !head.TrySeparate(detachedPartsRoot)) return false;
            ShowBody(FlounderBodyState.HeadRemovedFish);
            return true;
        }

        public bool TrySeparateFillet(int index)
        {
            Initialize();
            if (index < 0 || index >= fillets.Length || !HeadRemoved || IsTurningOver ||
                index != DetachedFilletCount || (index >= 2 && !IsBackFacing) ||
                !fillets[index].TrySeparate(detachedPartsRoot)) return false;
            DetachedFilletCount++;
            ShowBody((FlounderBodyState)((int)FlounderBodyState.HeadRemovedFish + DetachedFilletCount));
            return true;
        }

        public bool TryTurnOver()
        {
            Initialize();
            if (!HeadRemoved || DetachedFilletCount != 2 || IsBackFacing || IsTurningOver) return false;
            IsTurningOver = true;
            turnElapsed = 0f;
            return true;
        }

        public void AdvancePresentation(float deltaTime)
        {
            Initialize();
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            head.AdvancePresentation(deltaTime);
            for (int i = 0; i < fillets.Length; i++) fillets[i].AdvancePresentation(deltaTime);
            if (IsTurningOver)
            {
                turnElapsed += deltaTime;
                float t = Mathf.Clamp01(turnElapsed / Mathf.Max(0.01f, turnOverDuration));
                bodyPivot.localRotation = authoredBodyRotation *
                    Quaternion.AngleAxis(180f * Mathf.SmoothStep(0f, 1f, t), Vector3.forward);
                if (t >= 1f) { IsTurningOver = false; IsBackFacing = true; }
            }
            if (DetachedFilletCount == 4 && AllPartsStored())
                ShowBody(FlounderBodyState.Hidden);
        }

        private bool AllPartsStored()
        {
            if (!head.IsStored) return false;
            for (int i = 0; i < fillets.Length; i++)
                if (!fillets[i].IsStored) return false;
            return true;
        }

        public FlounderAssemblyPart GetStoredFillet(int index)
        {
            Initialize();
            return index >= 0 && index < fillets.Length && fillets[index].IsStored ? fillets[index] : null;
        }

        public void ResetAssembly()
        {
            Initialize();
            head.ResetToAuthoredPose();
            for (int i = 0; i < fillets.Length; i++) fillets[i].ResetToAuthoredPose();
            bodyPivot.localRotation = authoredBodyRotation;
            DetachedFilletCount = 0;
            IsBackFacing = IsTurningOver = false;
            turnElapsed = 0f;
            ShowBody(FlounderBodyState.WholeFish);
        }

        private void ShowBody(FlounderBodyState state)
        {
            BodyState = state;
            for (int i = 0; i < bodyStates.Length; i++)
                bodyStates[i].SetActive(i == (int)state);
        }
    }
}
