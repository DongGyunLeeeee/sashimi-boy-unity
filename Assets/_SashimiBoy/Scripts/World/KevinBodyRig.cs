using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SashimiBoy
{
    // The visible hands belong to this skinned body's arm chains. Targets never move bones independently.
    [DefaultExecutionOrder(400)]
    public sealed class KevinBodyRig : MonoBehaviour
    {
        [Serializable] public sealed class Arm
        {
            public Transform upper, lower, hand, palm;
            public Quaternion upperRest, lowerRest, handRest, handWorldRest;
            public float upperLength, lowerLength;
        }
        public Arm right = new Arm(), left = new Arm();
        public Renderer[] headRenderers = Array.Empty<Renderer>();
        public Transform head, eyeAnchor;
        public Transform cookingSpine;
        public Quaternion spineRest;
        public Transform[] fingerBones = Array.Empty<Transform>();
        public Quaternion[] fingerRest = Array.Empty<Quaternion>();
        public Vector3[] fingerAxes = Array.Empty<Vector3>();
        public float[] fingerAngles = Array.Empty<float>();
        public bool working;
        public Vector3 rightTarget, leftTarget;
        public Quaternion rightRotation, leftRotation;
        public float MaximumGripError { get; private set; }

        private void LateUpdate() => ApplyPose();

        public void SetHeadHidden(bool hidden)
        {
            foreach (var renderer in headRenderers)
                if (renderer != null) renderer.shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        public void ApplyPose()
        {
            if (cookingSpine != null) cookingSpine.localRotation = spineRest * Quaternion.AngleAxis(working ? 27f : 0f, Vector3.right);
            for (int i = 0; i < fingerBones.Length; i++)
                fingerBones[i].localRotation = fingerRest[i] * Quaternion.AngleAxis(working ? fingerAngles[i] : fingerAngles[i] * .18f, fingerAxes[i]);
            if (working)
            {
                Solve(right, rightTarget, rightRotation, transform.right * .6f + Vector3.down);
                Solve(left, leftTarget, leftRotation, -transform.right * .6f + Vector3.down);
                MaximumGripError = Mathf.Max(Vector3.Distance(right.palm.position, rightTarget), Vector3.Distance(left.palm.position, leftTarget));
            }
            else
            {
                Vector3 basePoint = transform.position;
                Solve(right, basePoint + transform.TransformVector(new Vector3(.24f, .88f, .08f)), transform.rotation * Quaternion.Euler(0f, 0f, -76f) * right.handWorldRest, transform.forward);
                Solve(left, basePoint + transform.TransformVector(new Vector3(-.24f, .88f, .08f)), transform.rotation * Quaternion.Euler(0f, 0f, 76f) * left.handWorldRest, transform.forward);
            }
        }

        private static void Solve(Arm arm, Vector3 palmTarget, Quaternion handRotation, Vector3 elbowHint)
        {
            if (arm.upper == null || arm.lower == null || arm.hand == null) return;
            arm.upper.localRotation = arm.upperRest; arm.lower.localRotation = arm.lowerRest; arm.hand.localRotation = arm.handRest;
            Vector3 wrist = palmTarget - handRotation * Vector3.Scale(arm.palm.localPosition, arm.hand.lossyScale);
            Vector3 shoulder = arm.upper.position, delta = wrist - shoulder;
            float a = Vector3.Distance(shoulder, arm.lower.position), b = Vector3.Distance(arm.lower.position, arm.hand.position);
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            Vector3 direction = delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.forward;
            Vector3 bend = Vector3.ProjectOnPlane(elbowHint, direction).normalized;
            if (bend.sqrMagnitude < .1f) bend = Vector3.ProjectOnPlane(Vector3.forward, direction).normalized;
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            Vector3 elbow = shoulder + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            arm.upper.rotation = Quaternion.FromToRotation(arm.lower.position - shoulder, elbow - shoulder) * arm.upper.rotation;
            arm.lower.rotation = Quaternion.FromToRotation(arm.hand.position - arm.lower.position, shoulder + direction * distance - arm.lower.position) * arm.lower.rotation;
            arm.hand.rotation = handRotation;
        }
    }
}
