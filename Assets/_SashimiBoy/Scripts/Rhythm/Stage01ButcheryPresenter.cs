using System;
using System.Collections.Generic;
using SashimiBoy.Semantics;
using UnityEngine;

namespace SashimiBoy
{
    /// <summary>Consumes judged semantic events. It never reads keys or awards progression.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class Stage01ButcheryPresenter : MonoBehaviour
    {
        public Stage01SalmonTimingScaffold timing;
        public SalmonAssemblyView assembly;
        public GameObject filletHalf;
        public GameObject reservedFilletHalf;
        public Stage01FilletSurface filletSurface;
        public Transform[] workAnchors = Array.Empty<Transform>();
        public Transform[] workEndAnchors = Array.Empty<Transform>();
        public GameObject slicePrefab;
        public Transform sliceRoot;
        public Transform[] plateSlots = Array.Empty<Transform>();
        public Transform knifeRoot;
        public Transform knifeGripAnchor;
        public Transform knifeContactAnchor;
        public Transform handRoot;
        public Transform tweezersRoot;
        public Transform wasteAnchor;
        public float separationSeconds = .55f;
        public float actionSeconds = .24f;
        public float motionScale = 1f;

        public FishPhase VisiblePhase { get; private set; }
        public int RemovedBoneCount { get; private set; }
        public int SliceCount => slices.Count;
        public bool PlateComplete { get; private set; }
        public long RunId { get; private set; }
        public string PhaseLabel => Labels[(int)VisiblePhase];
        private static readonly string[] Labels = { "머리 분리", "지느러미 손질", "척추 분리", "필렛 반으로 나누기", "반쪽 가시 제거", "회 썰기", "회 한 판 완성" };
        private readonly List<GameObject> slices = new List<GameObject>();
        private readonly List<Motion> motions = new List<Motion>();
        private Stage01SalmonTimingScaffold subscribedTiming;
        private int lastNote = -1;
        private float actionTime;
        private Vector3 actionTarget;
        private FishAction currentAction;
        private bool actionHit;
        private Quaternion knifeRotation, tweezerRotation, handRotation;

        private struct Motion
        {
            public Transform target;
            public Vector3 from, to;
            public Quaternion startRotation, endRotation;
            public Vector3 startScale, endScale;
            public float elapsed;
        }

        private void Awake()
        {
            if (knifeRoot != null) knifeRotation = knifeRoot.rotation;
            if (tweezersRoot != null) tweezerRotation = tweezersRoot.rotation;
            if (handRoot != null) handRotation = handRoot.rotation;
        }

        private void OnEnable() => Bind(timing);
        private void OnDisable()
        {
            if (subscribedTiming != null) subscribedTiming.SemanticEventRaised -= OnSemanticEvent;
            subscribedTiming = null;
        }

        public void Bind(Stage01SalmonTimingScaffold source)
        {
            if (subscribedTiming != null) subscribedTiming.SemanticEventRaised -= OnSemanticEvent;
            timing = subscribedTiming = source;
            if (subscribedTiming != null) subscribedTiming.SemanticEventRaised += OnSemanticEvent;
            ResetVisualState(source != null ? source.PhasePerformance.RunId : 0);
        }

        public void ResetVisualState(long run)
        {
            motions.Clear();
            foreach (GameObject slice in slices)
            {
                if (slice == null) continue;
                slice.SetActive(false);
                Destroy(slice);
            }
            slices.Clear();
            RunId = run;
            lastNote = -1;
            RemovedBoneCount = 0;
            PlateComplete = false;
            VisiblePhase = FishPhase.WholeFish;
            actionTime = 0f;
            assembly?.ResetAssembly();
            if (filletHalf != null) filletHalf.SetActive(false);
            if (reservedFilletHalf != null) reservedFilletHalf.SetActive(false);
            filletSurface?.ShowCut(0);
            ResetTools();
        }

        private void OnSemanticEvent(SemanticEvent value)
        {
            if (value.kind == SemanticEventKind.RunReset)
            {
                ResetVisualState(value.runId);
                return;
            }
            if (value.runId != RunId || assembly == null) return;
            if (value.kind == SemanticEventKind.ActionResolved)
            {
                if (value.noteId <= lastNote) return;
                lastNote = value.noteId;
                currentAction = value.action;
                actionTarget = GetCueWorldPosition(value.noteId);
                actionHit = value.grade != QualityGrade.Whack;
                actionTime = actionSeconds;
                PhaseGate gate = timing.semanticBeatmap.chart.phases[(int)value.phase];
                float fraction = (value.noteId - gate.firstNoteId + 1f) / gate.expectedNoteCount;
                if (actionHit && value.phase == FishPhase.PinBones)
                    RemoveBones(Mathf.FloorToInt(fraction * assembly.pinBones.Length));
                if (actionHit && value.phase == FishPhase.Slicing)
                    FillPlate(Mathf.FloorToInt(fraction * plateSlots.Length));
            }
            else if (value.kind == SemanticEventKind.PhaseSucceeded)
            {
                switch (value.phase)
                {
                    case FishPhase.WholeFish:
                        MovePiece(assembly.head, WastePosition(0));
                        break;
                    case FishPhase.Head:
                        // The approved Body mesh includes exterior fins. Replace the entire
                        // body with the provided opened fillet/spine state, then remove Fins.
                        assembly.body.SetVisible(false);
                        assembly.fillet.SetVisible(true);
                        assembly.spine.SetVisible(true);
                        assembly.fins.SetVisible(true);
                        MovePiece(assembly.fins, WastePosition(1));
                        break;
                    case FishPhase.Fins:
                        MovePiece(assembly.spine, WastePosition(2));
                        break;
                    case FishPhase.Spine:
                        assembly.fillet.SetVisible(false);
                        if (filletHalf != null) filletHalf.SetActive(true);
                        if (reservedFilletHalf != null) reservedFilletHalf.SetActive(true);
                        foreach (var bone in assembly.pinBones) bone.SetVisible(true);
                        break;
                    case FishPhase.PinBones:
                        RemoveBones(assembly.pinBones.Length);
                        break;
                    case FishPhase.Slicing:
                        FillPlate(plateSlots.Length);
                        PlateComplete = true;
                        break;
                }
                VisiblePhase = value.nextPhase;
                actionTime = 0f; // Never carry a stroke from the previous target into the next phase.
                if ((int)VisiblePhase < 6) currentAction = timing.semanticBeatmap.chart.phases[(int)VisiblePhase].action;
            }
            else if (value.kind == SemanticEventKind.PhaseFailed)
            {
                PlateComplete = false;
                actionTime = 0f;
                ResetTools();
            }
        }

        public Vector3 GetCueWorldPosition(int noteId)
        {
            if (assembly == null) return transform.position;
            FishPhase phase = VisiblePhase;
            float progress = 0f;
            if (timing != null && timing.semanticBeatmap != null && timing.semanticBeatmap.chart != null)
            {
                var chart = timing.semanticBeatmap.chart;
                if (noteId >= 0 && noteId < chart.notes.Length)
                {
                    phase = chart.notes[noteId].phase;
                    var gate = chart.phases[(int)phase];
                    progress = (noteId - gate.firstNoteId) / (float)Mathf.Max(1, gate.expectedNoteCount - 1);
                }
            }
            if (phase == FishPhase.PinBones && assembly.pinBoneAnchors.Length > 0)
            {
                int bone = Mathf.Clamp(RemovedBoneCount, 0, assembly.pinBoneAnchors.Length - 1);
                return assembly.pinBoneAnchors[bone].position;
            }
            if (phase == FishPhase.Slicing && filletSurface != null) return filletSurface.NextCutPosition(SliceCount);
            int index = Mathf.Clamp((int)phase, 0, 5);
            if (index < workAnchors.Length && workAnchors[index] != null)
                return Vector3.Lerp(workAnchors[index].position,
                    index < workEndAnchors.Length && workEndAnchors[index] != null ? workEndAnchors[index].position : workAnchors[index].position,
                    progress);
            return assembly.transform.position + Vector3.up * .5f;
        }

        private void RemoveBones(int targetCount)
        {
            while (RemovedBoneCount < Mathf.Min(targetCount, assembly.pinBones.Length))
            {
                var bone = assembly.pinBones[RemovedBoneCount++];
                MovePiece(bone, WastePosition(3) + Vector3.right * RemovedBoneCount * .04f);
            }
        }

        private void FillPlate(int targetCount)
        {
            if (slicePrefab == null || sliceRoot == null) return;
            while (slices.Count < Mathf.Min(targetCount, plateSlots.Length))
            {
                Transform slot = plateSlots[slices.Count];
                GameObject slice = Instantiate(slicePrefab, sliceRoot);
                slice.name = "PlatedSlice_" + slices.Count.ToString("00");
                slice.transform.SetPositionAndRotation(actionTarget + Vector3.up * (.1f * motionScale), slot.rotation);
                slices.Add(slice);
                filletSurface?.ShowCut(slices.Count);
                AnimateTo(slice.transform, slot.position, slot.rotation);
            }
        }

        private Vector3 WastePosition(int index) =>
            (wasteAnchor != null ? wasteAnchor.position : assembly.transform.position + Vector3.left * 3f) +
            new Vector3(index % 2 * .72f, index * .04f, index / 2 * .40f) * motionScale;

        private void MovePiece(SalmonAssemblyPieceView piece, Vector3 destination)
        {
            piece.Detach(transform);
            float scale = piece.Role == SalmonAssemblyPieceRole.PinBone ? 1f : .55f;
            Quaternion rotation = piece.Role == SalmonAssemblyPieceRole.Spine || piece.Role == SalmonAssemblyPieceRole.Fins
                ? Quaternion.Euler(90f, 15f, 0f) * piece.transform.rotation : piece.transform.rotation;
            if (piece.Role == SalmonAssemblyPieceRole.Spine || piece.Role == SalmonAssemblyPieceRole.Fins)
            {
                float bestHeight = float.PositiveInfinity;
                foreach (var candidate in new[] { piece.transform.rotation, Quaternion.Euler(90f,0f,0f)*piece.transform.rotation, Quaternion.Euler(0f,0f,90f)*piece.transform.rotation })
                {
                    float low = float.PositiveInfinity, high = float.NegativeInfinity;
                    foreach (var filter in piece.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var bounds = filter.sharedMesh.bounds;
                        for (int i=0;i<8;i++)
                        {
                            Vector3 corner = bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                            Vector3 local = piece.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                            float y = (candidate*Vector3.Scale(local,piece.transform.lossyScale*scale)).y; low=Mathf.Min(low,y); high=Mathf.Max(high,y);
                        }
                    }
                    if (high-low<bestHeight) { bestHeight=high-low; rotation=candidate; }
                }
            }
            float minY = float.PositiveInfinity;
            foreach (var filter in piece.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds bounds = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = piece.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    minY = Mathf.Min(minY, (rotation * Vector3.Scale(local, piece.transform.lossyScale * scale)).y);
                }
            }
            if (float.IsFinite(minY)) destination.y = (wasteAnchor != null ? wasteAnchor.position.y : .4f) - minY;
            AnimateTo(piece.transform, destination, rotation, scale);
        }

        public void PlayDemonstrationStroke()
        {
            if (timing == null || timing.CurrentSection != Stage01SalmonSection.BossDemo) return;
            currentAction = FishAction.CutHead; actionTarget = GetCueWorldPosition(0);
            actionHit = true; actionTime = actionSeconds;
        }

        private void AnimateTo(Transform target, Vector3 destination, Quaternion rotation, float scale = 1f)
        {
            motions.Add(new Motion { target = target, from = target.position, to = destination,
                startRotation = target.rotation, endRotation = rotation,
                startScale = target.localScale, endScale = target.localScale * scale });
        }

        private void Update()
        {
            for (int i = motions.Count - 1; i >= 0; i--)
            {
                Motion m = motions[i];
                if (m.target == null) { motions.RemoveAt(i); continue; }
                m.elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(m.elapsed / separationSeconds);
                float smooth = Mathf.SmoothStep(0f, 1f, t);
                m.target.position = Vector3.Lerp(m.from, m.to, smooth) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .32f * motionScale);
                m.target.rotation = Quaternion.Slerp(m.startRotation, m.endRotation, smooth);
                m.target.localScale = Vector3.Lerp(m.startScale, m.endScale, smooth);
                if (t >= 1f) motions.RemoveAt(i); else motions[i] = m;
            }
            bool playable = timing != null && timing.SemanticValidation.IsValid &&
                !timing.PhasePerformance.Failed && !timing.PhasePerformance.Completed &&
                timing.CurrentSection != Stage01SalmonSection.Result;
            if (!playable) { SetToolVisible(false, false); return; }
            bool pulling = VisiblePhase == FishPhase.PinBones;
            SetToolVisible(!pulling, pulling);
            Vector3 target = actionTime > 0f ? actionTarget : GetCueWorldPosition(timing.PhasePerformance.NoteCursor);
            float stroke = actionTime > 0f ? Mathf.Sin((1f - actionTime / actionSeconds) * Mathf.PI) : 0f;
            if (!actionHit) stroke *= -.35f;
            Vector3 movement;
            switch (currentAction)
            {
                case FishAction.Prepare: movement = Vector3.down * (.14f * stroke); break;
                case FishAction.SeparateSpine: movement = new Vector3(-.32f * stroke, .12f * stroke, 0f); break;
                case FishAction.SplitFillet: movement = new Vector3(-.32f * stroke, -.08f * stroke, 0f); break;
                case FishAction.PullPinBone: movement = new Vector3(0f, .6f * stroke, -.16f * stroke); break;
                case FishAction.RemoveFin: movement = new Vector3(0f, -.1f * stroke, .38f * stroke); break;
                default: movement = new Vector3(0f, -.10f * stroke, .28f * stroke); break;
            }
            Quaternion workRotation = Quaternion.Euler(0f,
                currentAction == FishAction.SeparateSpine || currentAction == FishAction.SplitFillet ? 90f : 0f, 0f);
            movement *= motionScale;
            if (knifeRoot != null)
            {
                knifeRoot.rotation = workRotation * knifeRotation;
                Vector3 contact = knifeContactAnchor != null ? knifeRoot.TransformVector(knifeContactAnchor.localPosition) : Vector3.forward * .35f;
                knifeRoot.position = target + Vector3.up * (.055f * motionScale) + movement - contact;
            }
            if (tweezersRoot != null)
            {
                tweezersRoot.position = target + new Vector3(0f, .13f, -.24f) * motionScale + movement;
                tweezersRoot.rotation = tweezerRotation;
            }
            if (handRoot != null)
            {
                handRoot.position = !pulling && knifeGripAnchor != null
                    ? knifeGripAnchor.position + workRotation * new Vector3(0f, .045f, 0f) * motionScale
                    : target + new Vector3(0f, .20f, -.45f) * motionScale + movement;
                handRoot.rotation = (pulling ? Quaternion.identity : workRotation) * handRotation;
            }
            actionTime = Mathf.Max(0f, actionTime - Time.unscaledDeltaTime);
        }

        private void SetToolVisible(bool knife, bool tweezers)
        {
            if (knifeRoot != null) knifeRoot.gameObject.SetActive(knife);
            if (tweezersRoot != null) tweezersRoot.gameObject.SetActive(tweezers);
            if (handRoot != null) handRoot.gameObject.SetActive(knife || tweezers ||
                (timing != null && timing.PhasePerformance.IsRunning && timing.CurrentSection == Stage01SalmonSection.Gameplay));
        }

        private void ResetTools()
        {
            actionTarget = GetCueWorldPosition(0);
            currentAction = FishAction.CutHead;
            actionHit = true;
            if (knifeRoot != null) knifeRoot.SetPositionAndRotation(actionTarget + new Vector3(0f, .18f, -.65f), knifeRotation);
            if (tweezersRoot != null) tweezersRoot.SetPositionAndRotation(actionTarget + new Vector3(0f, .15f, -.2f), tweezerRotation);
            if (handRoot != null) handRoot.SetPositionAndRotation(actionTarget + new Vector3(-.12f, .22f, -.9f), handRotation);
            SetToolVisible(false, false);
        }
    }
}
