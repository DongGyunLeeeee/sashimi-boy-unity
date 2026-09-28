using UnityEngine;

namespace SashimiBoy
{
    [DefaultExecutionOrder(300)]
    public sealed class Stage01CookingView : MonoBehaviour
    {
        public Stage01ButcheryPresenter butchery;
        public KevinBodyRig body;
        public Camera gameCamera;
        public Transform openFishFocus;
        public Vector3 standingPosition = new Vector3(.12f, 0f, -.82f);
        public float eyeHeight = 1.64f;
        public float cookingPitch = 48f;
        public Transform[] calibratedRoots;
        public Vector3[] originalPositions, originalScales;
        public float worldUnitScale = .27f;
        private float authoredFieldOfView;

        private void Awake() { body.SetHeadHidden(true); if(gameCamera != null) authoredFieldOfView=gameCamera.fieldOfView; }

        private void LateUpdate()
        {
            if (butchery == null || body == null) return;
            Vector3 work = butchery.GetCueWorldPosition(butchery.timing.PhasePerformance.NoteCursor);
            // Small stance shift keeps the knife within anatomical reach; the arm chain stays attached.
            Vector3 stance = standingPosition; stance.x = Mathf.Clamp(work.x - .14f, -.65f, .55f);
            if (butchery.timing.PhasePerformance.Completed) stance.x = -.4f;
            body.transform.position = Vector3.Lerp(body.transform.position,stance,1f-Mathf.Exp(-12f*Time.unscaledDeltaTime));
            bool active = butchery.handRoot != null && butchery.handRoot.gameObject.activeInHierarchy;
            body.working = active;
            if (active)
            {
                bool pulling = butchery.VisiblePhase == Semantics.FishPhase.PinBones;
                Quaternion toolRotation = pulling ? Quaternion.identity : butchery.knifeRoot.rotation;
                body.rightTarget = butchery.handRoot.position;
                body.rightRotation = toolRotation * Quaternion.Euler(0f, 180f, 0f) * body.right.handWorldRest;
                body.leftTarget = work + new Vector3(-.13f, .012f, -.035f);
                body.leftTarget.z = Mathf.Min(body.leftTarget.z,-.16f);
                body.leftRotation = Quaternion.Euler(0f, 90f, 0f) * body.left.handWorldRest;
            }
            if (gameCamera != null)
            {
                body.ApplyPose();
                gameCamera.transform.position = active && body.eyeAnchor != null ? body.eyeAnchor.position : body.transform.position + new Vector3(0f,eyeHeight,.27f);
                float yaw = 0f;
                bool openFish = openFishFocus != null && (butchery.VisiblePhase == Semantics.FishPhase.Fins || butchery.VisiblePhase == Semantics.FishPhase.Spine);
                if (openFish)
                {
                    // Kevin keeps looking at the fish while stepping sideways along the opened fillet.
                    // Eye position and cooking pitch still belong to the connected body.
                    Vector3 direction = openFishFocus.position - gameCamera.transform.position;
                    yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -15f, 15f);
                }
                float lens = authoredFieldOfView + (openFish ? 10f : 0f);
                // Preserve the work area's horizontal coverage when the Game window is narrower than 16:9.
                if (openFish && gameCamera.aspect < 16f/9f)
                    lens = 2f * Mathf.Atan(Mathf.Tan(lens*.5f*Mathf.Deg2Rad) * (16f/9f) / gameCamera.aspect) * Mathf.Rad2Deg;
                gameCamera.fieldOfView = lens;
                gameCamera.transform.rotation = Quaternion.Euler(cookingPitch + (openFish ? 3f : 0f), yaw, 0f);
            }
        }
    }
}
