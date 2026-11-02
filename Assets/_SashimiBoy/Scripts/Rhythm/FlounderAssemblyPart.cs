using System;
using UnityEngine;

namespace SashimiBoy
{
    [DisallowMultipleComponent]
    public sealed class FlounderAssemblyPart : MonoBehaviour
    {
        public string stableId;
        public Transform storageAnchor;
        [Min(0.01f)] public float separationDuration = 0.65f;
        [Min(0f)] public float separationLift = 0.1f;

        public bool IsDetached { get; private set; }
        public bool IsStored { get; private set; }
        public bool IsMoving { get; private set; }

        private bool captured;
        private Transform authoredParent;
        private Vector3 authoredPosition;
        private Quaternion authoredRotation;
        private Vector3 authoredScale;
        private Vector3 departurePosition;
        private Quaternion departureRotation;
        private float elapsed;

        public void CaptureAuthoredPose()
        {
            if (captured) return;
            authoredParent = transform.parent;
            authoredPosition = transform.localPosition;
            authoredRotation = transform.localRotation;
            authoredScale = transform.localScale;
            captured = true;
        }

        public bool TrySeparate(Transform independentRoot)
        {
            CaptureAuthoredPose();
            if (IsDetached || storageAnchor == null || independentRoot == null) return false;
            departurePosition = transform.position;
            departureRotation = transform.rotation;
            transform.SetParent(independentRoot, true);
            if (transform.parent != independentRoot) return false;
            IsDetached = true;
            IsStored = false;
            IsMoving = true;
            elapsed = 0f;
            gameObject.SetActive(true);
            return true;
        }

        // The owner advances all five parts from one presentation clock. This
        // has no physics collision, score, slicing, inventory or completion side effect.
        public void AdvancePresentation(float deltaTime)
        {
            if (!IsMoving || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            elapsed += deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, separationDuration));
            float smooth = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(departurePosition, storageAnchor.position, smooth)
                + Vector3.up * (4f * t * (1f - t) * separationLift);
            transform.rotation = Quaternion.Slerp(departureRotation, storageAnchor.rotation, smooth);
            if (t >= 1f)
            {
                transform.SetParent(storageAnchor, true);
                if (transform.parent != storageAnchor)
                    throw new InvalidOperationException("Flounder part could not reach its storage parent: " + stableId);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                IsMoving = false;
                IsStored = true;
            }
        }

        public void ResetToAuthoredPose()
        {
            CaptureAuthoredPose();
            transform.SetParent(authoredParent, false);
            if (transform.parent != authoredParent)
                throw new InvalidOperationException("Flounder part could not restore its authored parent: " + stableId);
            transform.localPosition = authoredPosition;
            transform.localRotation = authoredRotation;
            transform.localScale = authoredScale;
            IsDetached = IsStored = IsMoving = false;
            elapsed = 0f;
            gameObject.SetActive(false);
        }
    }
}
