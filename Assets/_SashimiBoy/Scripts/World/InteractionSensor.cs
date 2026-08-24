using UnityEngine;

namespace SashimiBoy
{
    public sealed class InteractionSensor : MonoBehaviour
    {
        [Min(0.1f)] public float radius = 2.8f;
        public KeyCode interactKey = KeyCode.E;
        public LayerMask interactableLayers = ~0;
        public LayerMask lineOfSightLayers = ~0;
        public Camera viewCamera;
        public Transform interactionOrigin;
        public bool requireLookTarget = true;

        private IInteractable current;
        private Collider currentCollider;
        private bool inputEnabled = true;
        private readonly RaycastHit[] raycastHits = new RaycastHit[32];

        public IInteractable Current => current;

        private void Update()
        {
            if (!inputEnabled)
            {
                ClearCurrent();
                UpdatePrompt();
                return;
            }

            FindCurrent();
            UpdatePrompt();

            if (current != null && Input.GetKeyDown(interactKey))
            {
                current.Interact(gameObject);
            }
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
            if (!enabled)
            {
                ClearCurrent();
                UpdatePrompt();
            }
        }

        private void FindCurrent()
        {
            if (!requireLookTarget)
            {
                FindNearestInRange();
                return;
            }

            ResolveViewCamera();
            if (viewCamera == null)
            {
                ClearCurrent();
                return;
            }

            Ray ray = viewCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            int count = Physics.RaycastNonAlloc(
                ray,
                raycastHits,
                radius,
                lineOfSightLayers,
                QueryTriggerInteraction.Collide);
            float nearestDistance = float.MaxValue;
            Collider nearestCollider = null;
            IInteractable nearestInteractable = null;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = raycastHits[i];
                Collider collider = hit.collider;
                if (collider == null ||
                    collider.transform.IsChildOf(transform.root))
                {
                    continue;
                }

                IInteractable interactable = FindInteractable(collider);
                if (interactable != null && !ContainsLayer(
                        interactableLayers,
                        collider.gameObject.layer))
                {
                    interactable = null;
                }

                if (collider.isTrigger && interactable == null)
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestCollider = collider;
                    nearestInteractable = interactable;
                }
            }

            if (nearestCollider == null || nearestInteractable == null ||
                !IsInInteractionRange(nearestCollider))
            {
                ClearCurrent();
                return;
            }

            current = nearestInteractable;
            currentCollider = nearestCollider;
        }

        private void FindNearestInRange()
        {
            Vector3 origin = GetInteractionOrigin();
            Collider[] hits = Physics.OverlapSphere(
                origin,
                radius,
                interactableLayers,
                QueryTriggerInteraction.Collide);
            float bestDistance = float.MaxValue;
            IInteractable best = null;
            Collider bestCollider = null;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null || hit.transform.IsChildOf(transform.root))
                {
                    continue;
                }

                IInteractable interactable = FindInteractable(hit);
                if (interactable == null)
                {
                    continue;
                }

                float distance = Vector3.SqrMagnitude(
                    hit.ClosestPoint(origin) - origin);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = interactable;
                    bestCollider = hit;
                }
            }

            current = best;
            currentCollider = bestCollider;
        }

        private bool IsInInteractionRange(Collider collider)
        {
            Vector3 origin = GetInteractionOrigin();
            Vector3 closest = collider.ClosestPoint(origin);
            return Vector3.SqrMagnitude(closest - origin) <= radius * radius;
        }

        private Vector3 GetInteractionOrigin()
        {
            return interactionOrigin != null
                ? interactionOrigin.position
                : transform.position;
        }

        private void ResolveViewCamera()
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }
        }

        private void ClearCurrent()
        {
            current = null;
            currentCollider = null;
        }

        private static IInteractable FindInteractable(Collider hit)
        {
            MonoBehaviour[] behaviours = hit.GetComponentsInParent<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IInteractable interactable)
                {
                    return interactable;
                }
            }

            return null;
        }

        private static bool ContainsLayer(LayerMask mask, int layer)
        {
            return (mask.value & (1 << layer)) != 0;
        }

        private void UpdatePrompt()
        {
            if (InteractionPromptUI.Instance == null)
            {
                return;
            }

            if (current == null)
            {
                InteractionPromptUI.Instance.Hide();
                return;
            }

            InteractionPromptUI.Instance.Show($"E \u2014 {current.Prompt}");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = currentCollider != null ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(GetInteractionOrigin(), radius);

            if (viewCamera != null)
            {
                Gizmos.DrawRay(
                    viewCamera.transform.position,
                    viewCamera.transform.forward * radius);
            }
        }
    }
}
