using UnityEngine;

namespace SashimiBoy
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class SimpleTopDownPlayerController : MonoBehaviour
    {
        public float moveSpeed = 5f;
        public bool faceMoveDirection = true;
        public bool cameraRelativeMovement = true;
        [Min(0f)] public float turnSharpness = 12f;
        public Transform cameraTransform;

        private CharacterController controller;
        private bool inputEnabled = true;

        public bool InputEnabled => inputEnabled;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (controller == null)
            {
                return;
            }

            if (!inputEnabled)
            {
                controller.SimpleMove(Vector3.zero);
                return;
            }

            float x = Input.GetAxisRaw("Horizontal");
            float z = Input.GetAxisRaw("Vertical");
            Vector3 input = new Vector3(x, 0f, z);
            Vector3 move = BuildMoveVector(input);
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            controller.SimpleMove(move * moveSpeed);

            if (faceMoveDirection && move.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(move, Vector3.up);
                float blend = turnSharpness <= 0f
                    ? 1f
                    : 1f - Mathf.Exp(-turnSharpness * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    blend);
            }
        }

        public void SetCameraTransform(Transform value)
        {
            cameraTransform = value;
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
        }

        private Vector3 BuildMoveVector(Vector3 input)
        {
            if (!cameraRelativeMovement)
            {
                return input;
            }

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            if (cameraTransform == null)
            {
                return input;
            }

            Vector3 forward = cameraTransform.forward;
            Vector3 right = cameraTransform.right;
            forward.y = 0f;
            right.y = 0f;
            if (forward.sqrMagnitude < 0.001f ||
                right.sqrMagnitude < 0.001f)
            {
                return input;
            }

            forward.Normalize();
            right.Normalize();
            return right * input.x + forward * input.z;
        }
    }
}
