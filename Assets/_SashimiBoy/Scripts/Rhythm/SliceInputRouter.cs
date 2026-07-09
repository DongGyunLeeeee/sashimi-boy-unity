using System;
using UnityEngine;

namespace SashimiBoy
{
    public sealed class SliceInputRouter : MonoBehaviour
    {
        public event Action<SliceInputType> OnInput;

        public KeyCode sliceKey = KeyCode.Space;
        public KeyCode switchLeftKey = KeyCode.A;
        public KeyCode switchRightKey = KeyCode.D;
        public float repeatTapThreshold = 0.16f;

        private float lastTapTime = -999f;

        private void Update()
        {
            if (Input.GetKeyDown(sliceKey))
            {
                float now = Time.unscaledTime;
                SliceInputType type = now - lastTapTime <= repeatTapThreshold ? SliceInputType.RepeatTap : SliceInputType.Tap;
                lastTapTime = now;
                OnInput?.Invoke(type);
                OnInput?.Invoke(SliceInputType.HoldStart);
            }

            if (Input.GetKeyUp(sliceKey))
            {
                OnInput?.Invoke(SliceInputType.HoldEnd);
            }

            if (Input.GetKeyDown(switchLeftKey))
            {
                OnInput?.Invoke(SliceInputType.SwitchLeft);
            }

            if (Input.GetKeyDown(switchRightKey))
            {
                OnInput?.Invoke(SliceInputType.SwitchRight);
            }
        }
    }
}
