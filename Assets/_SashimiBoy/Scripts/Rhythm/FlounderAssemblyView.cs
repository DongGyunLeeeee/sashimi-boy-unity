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
        // The original four-piece asset-library presentation remains available.
        // Actual Stage03 uses two discarded skins and two complete edible sides.
        public GameObject[] skinnedBodyStates = Array.Empty<GameObject>();
        public FlounderAssemblyPart[] skins = Array.Empty<FlounderAssemblyPart>();
        public FlounderAssemblyPart[] wholeFillets = Array.Empty<FlounderAssemblyPart>();
        public Transform[] skinStorageAnchors = Array.Empty<Transform>();
        public Transform[] wholeFilletStorageAnchors = Array.Empty<Transform>();
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
        public bool TwoFilletPreparation { get; private set; }
        public int RemovedSkinCount { get; private set; }
        public int SeparatedWholeFilletCount { get; private set; }
        public GameObject ActiveBodyObject { get; private set; }

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
            if (TwoFilletPreparation || index < 0 || index >= fillets.Length || !HeadRemoved || IsTurningOver ||
                index != DetachedFilletCount || (index >= 2 && !IsBackFacing) ||
                !fillets[index].TrySeparate(detachedPartsRoot)) return false;
            DetachedFilletCount++;
            ShowBody((FlounderBodyState)((int)FlounderBodyState.HeadRemovedFish + DetachedFilletCount));
            return true;
        }

        public bool TryTurnOver()
        {
            Initialize();
            bool frontComplete = TwoFilletPreparation ? SeparatedWholeFilletCount == 1 : DetachedFilletCount == 2;
            if (!HeadRemoved || !frontComplete || IsBackFacing || IsTurningOver) return false;
            IsTurningOver = true;
            turnElapsed = 0f;
            return true;
        }

        public void BeginTwoFilletPreparation()
        {
            Initialize();
            if (skinnedBodyStates.Length != 2 || skins.Length != 2 || wholeFillets.Length != 2 ||
                skinStorageAnchors.Length != 2 || wholeFilletStorageAnchors.Length != 2)
                throw new InvalidOperationException("Incomplete two-side flounder preparation binding.");
            for (int i = 0; i < 2; i++)
            {
                if (skinnedBodyStates[i] == null || skins[i] == null || wholeFillets[i] == null ||
                    skinStorageAnchors[i] == null || wholeFilletStorageAnchors[i] == null)
                    throw new InvalidOperationException("Missing physical flounder side " + i);
                skins[i].storageAnchor = skinStorageAnchors[i];
                wholeFillets[i].storageAnchor = wholeFilletStorageAnchors[i];
                skins[i].CaptureAuthoredPose();
                wholeFillets[i].CaptureAuthoredPose();
            }
            TwoFilletPreparation = true;
            ResetAssembly();
        }

        public bool TryRemoveSkin(int side)
        {
            Initialize();
            if (!TwoFilletPreparation || !HeadRemoved || IsTurningOver || side < 0 || side > 1 ||
                side != RemovedSkinCount || SeparatedWholeFilletCount != side ||
                IsBackFacing != (side == 1) || !skins[side].TrySeparate(detachedPartsRoot)) return false;
            RemovedSkinCount++;
            ShowBody(side == 0 ? FlounderBodyState.FilletRemoved1 : FlounderBodyState.FilletRemoved3);
            return true;
        }

        public bool TrySeparateWholeFillet(int side)
        {
            Initialize();
            if (!TwoFilletPreparation || !HeadRemoved || IsTurningOver || side < 0 || side > 1 ||
                side != SeparatedWholeFilletCount || RemovedSkinCount != side + 1 ||
                IsBackFacing != (side == 1) || !wholeFillets[side].TrySeparate(detachedPartsRoot)) return false;
            SeparatedWholeFilletCount++;
            DetachedFilletCount = SeparatedWholeFilletCount;
            ShowBody(side == 0 ? FlounderBodyState.FilletRemoved2 : FlounderBodyState.FilletRemoved4);
            return true;
        }

        public FlounderAssemblyPart GetStoredWholeFillet(int side)
        {
            Initialize();
            return TwoFilletPreparation && side >= 0 && side < 2 && wholeFillets[side].IsStored
                ? wholeFillets[side] : null;
        }

        public void AdvancePresentation(float deltaTime)
        {
            Initialize();
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            head.AdvancePresentation(deltaTime);
            for (int i = 0; i < fillets.Length; i++) fillets[i].AdvancePresentation(deltaTime);
            if (TwoFilletPreparation)
                for (int i = 0; i < 2; i++)
                {
                    skins[i].AdvancePresentation(deltaTime);
                    wholeFillets[i].AdvancePresentation(deltaTime);
                    // Discarded skin is never presented as an edible stored fillet.
                    if (skins[i].IsStored) skins[i].gameObject.SetActive(false);
                }
            if (IsTurningOver)
            {
                turnElapsed += deltaTime;
                float t = Mathf.Clamp01(turnElapsed / Mathf.Max(0.01f, turnOverDuration));
                bodyPivot.localRotation = authoredBodyRotation *
                    Quaternion.AngleAxis(180f * Mathf.SmoothStep(0f, 1f, t), Vector3.forward);
                if (t >= 1f) { IsTurningOver = false; IsBackFacing = true; }
            }
            if ((TwoFilletPreparation ? SeparatedWholeFilletCount == 2 && AllWholePartsStored() :
                DetachedFilletCount == 4 && AllPartsStored()))
                ShowBody(FlounderBodyState.Hidden);
        }

        private bool AllWholePartsStored()
        {
            if (!head.IsStored) return false;
            for (int i = 0; i < 2; i++)
                if (!skins[i].IsStored || !wholeFillets[i].IsStored) return false;
            return true;
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
            for (int i = 0; i < skins.Length; i++) if (skins[i] != null) skins[i].ResetToAuthoredPose();
            for (int i = 0; i < wholeFillets.Length; i++) if (wholeFillets[i] != null) wholeFillets[i].ResetToAuthoredPose();
            bodyPivot.localRotation = authoredBodyRotation;
            DetachedFilletCount = 0;
            RemovedSkinCount = SeparatedWholeFilletCount = 0;
            IsBackFacing = IsTurningOver = false;
            turnElapsed = 0f;
            ShowBody(FlounderBodyState.WholeFish);
        }

        private void ShowBody(FlounderBodyState state)
        {
            BodyState = state;
            ActiveBodyObject = null;
            for (int i = 0; i < bodyStates.Length; i++)
            {
                bool active = i == (int)state && !(TwoFilletPreparation &&
                    (state == FlounderBodyState.FilletRemoved1 || state == FlounderBodyState.FilletRemoved3));
                bodyStates[i].SetActive(active);
                if (active) ActiveBodyObject = bodyStates[i];
            }
            for (int i = 0; i < skinnedBodyStates.Length; i++)
            {
                if (skinnedBodyStates[i] == null) continue;
                bool active = TwoFilletPreparation && state ==
                    (i == 0 ? FlounderBodyState.FilletRemoved1 : FlounderBodyState.FilletRemoved3);
                skinnedBodyStates[i].SetActive(active);
                if (active) ActiveBodyObject = skinnedBodyStates[i];
            }
        }
    }
}
