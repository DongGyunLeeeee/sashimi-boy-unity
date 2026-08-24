using UnityEngine;

namespace SashimiBoy
{
    public sealed class KnifeVisualController : MonoBehaviour
    {
        public GameObject visualRoot;
        public Transform motionRoot;
        public GameObject highlight;
        [Min(0.01f)] public float slashDownDuration = 0.1f;
        [Min(0.01f)] public float returnDuration = 0.15f;
        public float slashTravel = 1.15f;
        public float windupTravel = 0.18f;

        private Vector3 baseWorldPosition;
        private Vector3 baseMotionPosition;
        private Quaternion baseMotionRotation;
        private float animationTime;
        private float activeDownDuration;
        private float activeReturnDuration;
        private JudgeGrade activeGrade;
        private float windup01;
        private bool isAnimating;
        private bool isVisible = true;

        public bool IsAnimating => isAnimating;

        private void Awake()
        {
            if (visualRoot == null)
            {
                visualRoot = gameObject;
            }

            if (motionRoot == null)
            {
                motionRoot = transform;
            }

            baseWorldPosition = transform.position;
            baseMotionPosition = motionRoot.localPosition;
            baseMotionRotation = motionRoot.localRotation;
            SetHighlight(false);
        }

        private void Update()
        {
            if (!isAnimating)
            {
                ApplyWindupPose();
                return;
            }

            animationTime += Time.unscaledDeltaTime;
            float totalDuration = activeDownDuration + activeReturnDuration;
            if (animationTime >= totalDuration)
            {
                isAnimating = false;
                animationTime = 0f;
                motionRoot.localRotation = baseMotionRotation;
                SetHighlight(false);
                ApplyWindupPose();
                return;
            }

            float forward01;
            if (animationTime <= activeDownDuration)
            {
                forward01 = EaseOut(animationTime / activeDownDuration);
            }
            else
            {
                float return01 = (animationTime - activeDownDuration) /
                    activeReturnDuration;
                forward01 = 1f - Smooth01(return01);
            }

            float travel = slashTravel;
            if (activeGrade == JudgeGrade.Whack)
            {
                travel *= 0.58f;
                float bounce = Mathf.Sin(
                    Mathf.Clamp01(animationTime / totalDuration) *
                    Mathf.PI * 3f) * 0.05f;
                motionRoot.localPosition = baseMotionPosition +
                    Vector3.forward * (forward01 * travel - bounce);
                motionRoot.localRotation = baseMotionRotation *
                    Quaternion.Euler(0f, 11f * forward01, 0f);
            }
            else
            {
                motionRoot.localPosition = baseMotionPosition +
                    Vector3.forward * forward01 * travel;
                float twist = activeGrade == JudgeGrade.Slipped
                    ? Mathf.Sin(forward01 * Mathf.PI) * 9f
                    : 0f;
                motionRoot.localRotation = baseMotionRotation *
                    Quaternion.Euler(0f, twist, 0f);
            }
        }

        public void SetTargetWorld(Vector3 cutWorldPosition)
        {
            baseWorldPosition.x = cutWorldPosition.x;
            transform.position = baseWorldPosition;
        }

        public void SetWindup(float normalized)
        {
            windup01 = Mathf.Clamp01(normalized);
            if (!isAnimating)
            {
                ApplyWindupPose();
            }
        }

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            if (visualRoot != null && visualRoot != gameObject)
            {
                visualRoot.SetActive(visible);
            }
            else
            {
                Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].enabled = visible;
                }
            }

            if (!visible)
            {
                SetHighlight(false);
            }
        }

        public void PlaySlice(JudgeGrade grade)
        {
            if (!isVisible)
            {
                return;
            }

            activeGrade = grade;
            activeDownDuration = grade == JudgeGrade.Nasty
                ? 0.08f
                : slashDownDuration;
            activeReturnDuration = grade == JudgeGrade.Whack
                ? 0.18f
                : returnDuration;
            animationTime = 0f;
            isAnimating = true;
            SetHighlight(grade == JudgeGrade.Nasty);
        }

        private void ApplyWindupPose()
        {
            if (motionRoot == null)
            {
                return;
            }

            motionRoot.localPosition = baseMotionPosition -
                Vector3.forward * windupTravel * windup01;
            motionRoot.localRotation = baseMotionRotation;
        }

        private void SetHighlight(bool visible)
        {
            if (highlight != null)
            {
                highlight.SetActive(visible);
            }
        }

        private static float EaseOut(float value)
        {
            value = Mathf.Clamp01(value);
            return 1f - (1f - value) * (1f - value);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
