using System;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class ProceduralSalmonView : MonoBehaviour
    {
        [Header("Replaceable Visual Root")]
        public Transform visualRoot;
        public GameObject[] cutMarks = Array.Empty<GameObject>();
        public Renderer[] cutMarkRenderers = Array.Empty<Renderer>();
        public GameObject whackScratch;
        public Renderer whackScratchRenderer;

        [Header("Visual Progression")]
        [Min(1)] public int cutsPerFish = 8;
        public float cutStartLocalX = -1.85f;
        public float cutEndLocalX = 1.85f;
        public float cutLocalY = 0.42f;
        public float cutLocalZ;
        public Vector3 completedFishOffset = new Vector3(3.15f, 0.35f, 1.25f);
        [Min(0.1f)] public float completionDuration = 0.55f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock propertyBlock;
        private Vector3 baseVisualPosition;
        private Vector3 baseVisualScale;
        private float scratchTimer;
        private float punchTimer;
        private float completionTimer;
        private int successfulCuts;
        private int completedFishCount;
        private readonly Queue<JudgeGrade> pendingSuccessfulJudgements =
            new Queue<JudgeGrade>();

        public int SuccessfulCuts => successfulCuts;
        public int CompletedFishCount => completedFishCount;
        public bool IsTransitioning => completionTimer > 0f;

        public Vector3 NextCutWorldPosition
        {
            get => GetCutWorldPosition(successfulCuts);
        }

        public Vector3 GetCutWorldPosition(int cutIndex)
        {
            Transform basis = visualRoot != null ? visualRoot : transform;
            int denominator = Mathf.Max(1, cutsPerFish - 1);
            float normalized = Mathf.Clamp(cutIndex, 0, denominator) /
                (float)denominator;
            float x = Mathf.Lerp(
                cutStartLocalX,
                cutEndLocalX,
                normalized);
            return basis.TransformPoint(
                new Vector3(x, cutLocalY, cutLocalZ));
        }

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (visualRoot == null)
            {
                visualRoot = transform;
            }

            baseVisualPosition = visualRoot.localPosition;
            baseVisualScale = visualRoot.localScale;
            ResetCutMarks();
            SetScratchVisible(false);
        }

        private void Update()
        {
            float deltaTime = Time.unscaledDeltaTime;
            UpdateScratch(deltaTime);
            UpdateCompletion(deltaTime);
            UpdatePunch(deltaTime);
        }

        public void ApplyJudgement(JudgeGrade grade)
        {
            if (IsTransitioning)
            {
                if (grade != JudgeGrade.Whack)
                {
                    pendingSuccessfulJudgements.Enqueue(grade);
                }

                return;
            }

            if (grade == JudgeGrade.Whack)
            {
                ShowWhackScratch();
                return;
            }

            if (successfulCuts >= cutsPerFish)
            {
                return;
            }

            int markCount = Math.Min(cutsPerFish, cutMarks.Length);
            int markIndex = markCount > 0
                ? Mathf.Clamp(successfulCuts, 0, markCount - 1)
                : -1;
            if (markIndex >= 0 && markIndex < cutMarks.Length)
            {
                ConfigureCutMark(markIndex, grade);
            }

            successfulCuts++;
            punchTimer = grade == JudgeGrade.Nasty ? 0.12f : 0.07f;

            if (successfulCuts >= cutsPerFish)
            {
                completionTimer = completionDuration;
            }
        }

        public void ResetVisualProgress()
        {
            successfulCuts = 0;
            completionTimer = 0f;
            pendingSuccessfulJudgements.Clear();
            visualRoot.localPosition = baseVisualPosition;
            visualRoot.localScale = baseVisualScale;
            ResetCutMarks();
            SetScratchVisible(false);
        }

        private void ConfigureCutMark(int index, JudgeGrade grade)
        {
            GameObject mark = cutMarks[index];
            if (mark == null)
            {
                return;
            }

            float normalized = cutsPerFish <= 1
                ? 0f
                : index / (float)(cutsPerFish - 1);
            float x = Mathf.Lerp(cutStartLocalX, cutEndLocalX, normalized);
            mark.transform.localPosition =
                new Vector3(x, cutLocalY - 0.015f, cutLocalZ);

            float angle = 0f;
            Color color = new Color(0.95f, 0.98f, 1f, 1f);
            Vector3 scale = mark.transform.localScale;
            if (grade == JudgeGrade.Nasty)
            {
                color = Color.white;
                scale.x = 0.035f;
            }
            else if (grade == JudgeGrade.Slipped)
            {
                angle = index % 2 == 0 ? -8f : 8f;
                color = new Color(1f, 0.72f, 0.35f, 1f);
                scale.x = 0.055f;
            }
            else
            {
                color = new Color(0.88f, 0.95f, 1f, 1f);
                scale.x = 0.045f;
            }

            mark.transform.localScale = scale;
            mark.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            mark.SetActive(true);
            SetRendererColor(GetCutRenderer(index), color);
        }

        private void ShowWhackScratch()
        {
            if (whackScratch == null)
            {
                return;
            }

            Transform basis = visualRoot != null ? visualRoot : transform;
            Vector3 local = basis.InverseTransformPoint(NextCutWorldPosition);
            whackScratch.transform.localPosition =
                new Vector3(local.x, cutLocalY + 0.01f, cutLocalZ);
            whackScratch.transform.localRotation = Quaternion.Euler(
                0f,
                successfulCuts % 2 == 0 ? -13f : 13f,
                0f);
            SetRendererColor(
                whackScratchRenderer,
                new Color(1f, 0.08f, 0.04f, 1f));
            SetScratchVisible(true);
            scratchTimer = 0.18f;
        }

        private void UpdateScratch(float deltaTime)
        {
            if (scratchTimer <= 0f)
            {
                return;
            }

            scratchTimer -= deltaTime;
            if (scratchTimer <= 0f)
            {
                SetScratchVisible(false);
            }
        }

        private void UpdateCompletion(float deltaTime)
        {
            if (completionTimer <= 0f)
            {
                return;
            }

            completionTimer -= deltaTime;
            float elapsed = completionDuration - completionTimer;
            float normalized = Mathf.Clamp01(elapsed / completionDuration);

            if (normalized < 0.72f)
            {
                float move = Smooth01(normalized / 0.72f);
                visualRoot.localPosition = Vector3.Lerp(
                    baseVisualPosition,
                    baseVisualPosition + completedFishOffset,
                    move);
                visualRoot.localScale = Vector3.Lerp(
                    baseVisualScale,
                    baseVisualScale * 0.72f,
                    move);
            }
            else
            {
                float reset = Smooth01((normalized - 0.72f) / 0.28f);
                visualRoot.localPosition = Vector3.Lerp(
                    baseVisualPosition + completedFishOffset,
                    baseVisualPosition,
                    reset);
                visualRoot.localScale = Vector3.Lerp(
                    baseVisualScale * 0.3f,
                    baseVisualScale,
                    reset);
            }

            if (completionTimer > 0f)
            {
                return;
            }

            completedFishCount++;
            successfulCuts = 0;
            visualRoot.localPosition = baseVisualPosition;
            visualRoot.localScale = baseVisualScale;
            ResetCutMarks();
            ApplyPendingSuccessfulJudgements();
        }

        private void ApplyPendingSuccessfulJudgements()
        {
            while (!IsTransitioning &&
                pendingSuccessfulJudgements.Count > 0)
            {
                ApplyJudgement(pendingSuccessfulJudgements.Dequeue());
            }
        }

        private void UpdatePunch(float deltaTime)
        {
            if (completionTimer > 0f)
            {
                return;
            }

            if (punchTimer <= 0f)
            {
                visualRoot.localScale = baseVisualScale;
                return;
            }

            punchTimer -= deltaTime;
            float strength = Mathf.Sin(
                Mathf.Clamp01(punchTimer / 0.12f) * Mathf.PI) * 0.018f;
            visualRoot.localScale = baseVisualScale * (1f + strength);
        }

        private void ResetCutMarks()
        {
            for (int i = 0; i < cutMarks.Length; i++)
            {
                if (cutMarks[i] != null)
                {
                    cutMarks[i].SetActive(false);
                }
            }
        }

        private Renderer GetCutRenderer(int index)
        {
            if (index >= 0 && index < cutMarkRenderers.Length &&
                cutMarkRenderers[index] != null)
            {
                return cutMarkRenderers[index];
            }

            return index >= 0 && index < cutMarks.Length &&
                cutMarks[index] != null
                ? cutMarks[index].GetComponent<Renderer>()
                : null;
        }

        private void SetScratchVisible(bool visible)
        {
            if (whackScratch != null)
            {
                whackScratch.SetActive(visible);
            }
        }

        private void SetRendererColor(Renderer target, Color color)
        {
            if (target == null)
            {
                return;
            }

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            target.SetPropertyBlock(propertyBlock);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
