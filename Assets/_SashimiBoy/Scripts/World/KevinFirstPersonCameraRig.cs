using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace SashimiBoy
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class KevinFirstPersonCameraRig : MonoBehaviour
    {
        [Header("Rig")]
        public Transform firstPersonRig;
        public Transform yawRoot;
        public Transform pitchRoot;
        public Camera controlledCamera;
        public GameObject reticleRoot;

        [Header("Player")]
        public SimpleTopDownPlayerController movement;
        public InteractionSensor interactionSensor;
        public KevinVisualLoader visualLoader;

        [Header("View")]
        [Min(0.1f)] public float eyeHeight = 1.65f;
        [Range(0.88f, 0.92f)] public float modelEyeHeightRatio = 0.9f;
        public bool deriveEyeHeightFromVisual = true;
        [Range(40f, 100f)] public float fieldOfView = 75f;
        [Range(0.01f, 0.3f)] public float nearClipPlane = 0.05f;
        [Min(10f)] public float farClipPlane = 250f;

        [Header("Mouse Look")]
        [Min(0.01f)] public float horizontalSensitivity = 2.2f;
        [Min(0.01f)] public float verticalSensitivity = 2f;
        [Range(-89f, 0f)] public float minimumPitch = -75f;
        [Range(0f, 89f)] public float maximumPitch = 80f;
        [Min(0f)] public float lookSmoothing = 0.015f;

        [Header("Cursor And UI")]
        public KeyCode cursorReleaseKey = KeyCode.Escape;
        public bool lockCursorOnStart = true;
        public bool startWithUiOpen;

        [Header("First-Person Visual")]
        public bool hideKevinRenderers = true;

        private readonly Dictionary<Renderer, ShadowCastingMode>
            originalShadowModes =
                new Dictionary<Renderer, ShadowCastingMode>();

        private DialogueRunner[] dialogueRunners;
        private Vector2 smoothedLookInput;
        private float yaw;
        private float pitch;
        private bool manualCursorRelease;
        private bool externalUiBlocked;
        private bool externalLookEnabled = true;
        private static bool duplicateListenerWarningIssued;

        public bool IsInputBlocked =>
            manualCursorRelease || IsUiBlockingInput() ||
            !externalLookEnabled;

        private void Awake()
        {
            ResolveReferences();
            ConfigureCamera();
            EnsureSingleSceneCameraAndListener();
            dialogueRunners = FindObjectsByType<DialogueRunner>(
                FindObjectsInactive.Include);

            yaw = yawRoot != null
                ? NormalizeAngle(yawRoot.localEulerAngles.y)
                : 0f;
            pitch = pitchRoot != null
                ? Mathf.Clamp(
                    NormalizeAngle(pitchRoot.localEulerAngles.x),
                    minimumPitch,
                    maximumPitch)
                : 0f;
            ApplyViewRotation();
        }

        private void Start()
        {
            RefreshVisualPresentation();
            manualCursorRelease = !lockCursorOnStart;
            ApplyControlState();
        }

        private void Update()
        {
            HandleCursorToggle();

            bool blocked = IsInputBlocked;
            if (!blocked)
            {
                UpdateLook();
            }
            else
            {
                smoothedLookInput = Vector2.zero;
            }

            ApplyControlState();
        }

        private void OnDisable()
        {
            if (movement != null)
            {
                movement.SetInputEnabled(false);
            }

            if (interactionSensor != null)
            {
                interactionSensor.SetInputEnabled(false);
            }

            SetFirstPersonVisualHidden(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void SetUiBlocked(bool blocked)
        {
            externalUiBlocked = blocked;
            ApplyControlState();
        }

        public void SetLookEnabled(bool enabled)
        {
            externalLookEnabled = enabled;
            ApplyControlState();
        }

        public void RefreshVisualPresentation()
        {
            ResolveReferences();
            ApplyEyeHeight();
            SetFirstPersonVisualHidden(hideKevinRenderers);
        }

        public void SetFirstPersonVisualHidden(bool hidden)
        {
            if (visualLoader == null)
            {
                return;
            }

            List<Renderer> renderers = new List<Renderer>();
            if (visualLoader.visualRoot != null)
            {
                renderers.AddRange(
                    visualLoader.visualRoot.GetComponentsInChildren<Renderer>(
                        true));
            }

            if (visualLoader.fallbackRenderer != null &&
                !renderers.Contains(visualLoader.fallbackRenderer))
            {
                renderers.Add(visualLoader.fallbackRenderer);
            }

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!originalShadowModes.ContainsKey(renderer))
                {
                    originalShadowModes.Add(
                        renderer,
                        renderer.shadowCastingMode);
                }

                renderer.shadowCastingMode = hidden
                    ? ShadowCastingMode.ShadowsOnly
                    : originalShadowModes[renderer];
            }
        }

        private void ResolveReferences()
        {
            movement ??= GetComponent<SimpleTopDownPlayerController>();
            interactionSensor ??= GetComponent<InteractionSensor>();
            visualLoader ??= GetComponent<KevinVisualLoader>();

            if (firstPersonRig == null)
            {
                firstPersonRig = transform.Find("FirstPersonRig");
            }

            if (yawRoot == null && firstPersonRig != null)
            {
                yawRoot = firstPersonRig.Find("YawRoot");
            }

            if (pitchRoot == null && yawRoot != null)
            {
                pitchRoot = yawRoot.Find("PitchRoot");
            }

            if (controlledCamera == null && pitchRoot != null)
            {
                controlledCamera =
                    pitchRoot.GetComponentInChildren<Camera>(true);
            }

            if (controlledCamera == null)
            {
                controlledCamera = GetComponentInChildren<Camera>(true);
            }

            if (movement != null && controlledCamera != null)
            {
                movement.SetCameraTransform(controlledCamera.transform);
            }

            if (interactionSensor != null && controlledCamera != null)
            {
                interactionSensor.viewCamera = controlledCamera;
                interactionSensor.interactionOrigin = controlledCamera.transform;
            }
        }

        private void ConfigureCamera()
        {
            if (controlledCamera == null)
            {
                Debug.LogError(
                    "[Sashimi Boy] Kevin first-person rig has no Camera.",
                    this);
                enabled = false;
                return;
            }

            controlledCamera.gameObject.name = "Main Camera";
            controlledCamera.gameObject.tag = "MainCamera";
            controlledCamera.orthographic = false;
            controlledCamera.fieldOfView = fieldOfView;
            controlledCamera.nearClipPlane = nearClipPlane;
            controlledCamera.farClipPlane = Mathf.Max(
                farClipPlane,
                controlledCamera.nearClipPlane + 1f);
        }

        private void EnsureSingleSceneCameraAndListener()
        {
            if (controlledCamera == null)
            {
                return;
            }

            Camera[] cameras = FindObjectsByType<Camera>(
                FindObjectsInactive.Include);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || camera == controlledCamera ||
                    camera.gameObject.scene != gameObject.scene)
                {
                    continue;
                }

                camera.enabled = false;
                if (camera.CompareTag("MainCamera"))
                {
                    camera.gameObject.tag = "Untagged";
                }
            }

            AudioListener ownListener =
                controlledCamera.GetComponent<AudioListener>();
            if (ownListener == null)
            {
                ownListener =
                    controlledCamera.gameObject.AddComponent<AudioListener>();
            }

            ownListener.enabled = true;
            AudioListener[] listeners = FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include);
            int disabledCount = 0;
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener listener = listeners[i];
                if (listener == null || listener == ownListener ||
                    !listener.enabled ||
                    !listener.gameObject.activeInHierarchy)
                {
                    continue;
                }

                listener.enabled = false;
                disabledCount++;
            }

            if (disabledCount > 0 && !duplicateListenerWarningIssued)
            {
                duplicateListenerWarningIssued = true;
                Debug.LogWarning(
                    "[Sashimi Boy] Disabled duplicate AudioListener(s) " +
                    "while enabling the exploration first-person camera.");
            }
        }

        private void ApplyEyeHeight()
        {
            if (firstPersonRig == null)
            {
                return;
            }

            if (deriveEyeHeightFromVisual &&
                TryGetVisualBounds(out Bounds visualBounds))
            {
                float worldEyeY = visualBounds.min.y +
                    visualBounds.size.y * modelEyeHeightRatio;
                Vector3 eyePoint = transform.InverseTransformPoint(
                    new Vector3(
                        transform.position.x,
                        worldEyeY,
                        transform.position.z));
                SetRigLocalHeight(eyePoint.y);
                return;
            }

            CharacterController controller =
                GetComponent<CharacterController>();
            float localBottom = controller != null
                ? controller.center.y - controller.height * 0.5f
                : 0f;
            float scaleY = Mathf.Max(
                0.0001f,
                Mathf.Abs(transform.lossyScale.y));
            SetRigLocalHeight(localBottom + eyeHeight / scaleY);
        }

        private bool TryGetVisualBounds(out Bounds bounds)
        {
            bounds = default;
            if (visualLoader == null || visualLoader.visualRoot == null)
            {
                return false;
            }

            Renderer[] renderers =
                visualLoader.visualRoot.GetComponentsInChildren<Renderer>(
                    false);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found && bounds.size.y > 0.1f;
        }

        private void SetRigLocalHeight(float localHeight)
        {
            Vector3 localPosition = firstPersonRig.localPosition;
            localPosition.x = 0f;
            localPosition.y = localHeight;
            localPosition.z = 0f;
            firstPersonRig.localPosition = localPosition;
        }

        private void HandleCursorToggle()
        {
            bool uiBlocking = IsUiBlockingInput();
            if (!uiBlocking && Input.GetKeyDown(cursorReleaseKey))
            {
                manualCursorRelease = !manualCursorRelease;
            }

            if (!uiBlocking && manualCursorRelease &&
                Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                manualCursorRelease = false;
            }
        }

        private void UpdateLook()
        {
            Vector2 raw = new Vector2(
                Input.GetAxisRaw("Mouse X"),
                Input.GetAxisRaw("Mouse Y"));
            if (lookSmoothing <= 0f)
            {
                smoothedLookInput = raw;
            }
            else
            {
                float blend = 1f - Mathf.Exp(
                    -Time.unscaledDeltaTime / lookSmoothing);
                smoothedLookInput = Vector2.Lerp(
                    smoothedLookInput,
                    raw,
                    blend);
            }

            yaw = NormalizeAngle(
                yaw + smoothedLookInput.x * horizontalSensitivity);
            pitch = Mathf.Clamp(
                pitch - smoothedLookInput.y * verticalSensitivity,
                minimumPitch,
                maximumPitch);
            ApplyViewRotation();
        }

        private void ApplyViewRotation()
        {
            if (yawRoot != null)
            {
                yawRoot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            if (pitchRoot != null)
            {
                pitchRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        private void ApplyControlState()
        {
            bool blocked = IsInputBlocked;
            if (movement != null)
            {
                movement.SetInputEnabled(!blocked);
            }

            if (interactionSensor != null)
            {
                interactionSensor.SetInputEnabled(!blocked);
            }

            if (reticleRoot != null)
            {
                reticleRoot.SetActive(!blocked);
            }

            Cursor.lockState = blocked
                ? CursorLockMode.None
                : CursorLockMode.Locked;
            Cursor.visible = blocked;
        }

        private bool IsUiBlockingInput()
        {
            if (startWithUiOpen || externalUiBlocked)
            {
                return true;
            }

            if (dialogueRunners == null)
            {
                return false;
            }

            for (int i = 0; i < dialogueRunners.Length; i++)
            {
                DialogueRunner runner = dialogueRunners[i];
                if (runner != null && runner.IsRunning)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject();
        }

        private static float NormalizeAngle(float angle)
        {
            return Mathf.Repeat(angle + 180f, 360f) - 180f;
        }
    }
}
