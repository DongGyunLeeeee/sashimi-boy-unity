using UnityEngine;

namespace SashimiBoy
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class SimpleTopDownPlayerController : MonoBehaviour
    {
        public float moveSpeed = 5f;
        public bool faceMoveDirection = true;

        private CharacterController controller;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            float x = Input.GetAxisRaw("Horizontal");
            float z = Input.GetAxisRaw("Vertical");
            Vector3 move = new Vector3(x, 0f, z);
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            controller.SimpleMove(move * moveSpeed);

            if (faceMoveDirection && move.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(move, Vector3.up);
            }
        }
    }
}
