using System.Collections.Generic;
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
        private readonly List<Vector3> framingPoints = new List<Vector3>(96);
        private Renderer[] bodyRenderers,filletRenderers,halfRenderers,knifeRenderers,tweezerRenderers;
        private int fittedPhase=-1;
        private float fittedPitch,fittedLens,fittedAspect;
        public bool FramingSatisfied { get; private set; }

        private void Awake()
        {
            body.SetHeadHidden(true);if(gameCamera!=null)authoredFieldOfView=gameCamera.fieldOfView;
            if(butchery==null||butchery.assembly==null)return;
            bodyRenderers=Renderers(butchery.assembly.body.transform);filletRenderers=Renderers(butchery.assembly.fillet.transform);
            halfRenderers=Renderers(butchery.filletHalf!=null?butchery.filletHalf.transform:null);
            knifeRenderers=Renderers(butchery.knifeRoot);tweezerRenderers=Renderers(butchery.tweezersRoot);
        }

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
            // Both the retained body and the provided replacement use their
            // actual wrist reach. No tool target, bone length or root height is
            // changed to make the longer fillet or later tool phases reachable.
            if (active) body.ApplyWorkingPoseAtRoot(body.transform.position);
            else body.ApplyPose();
            if (gameCamera != null)
            {
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
                float pitch=cookingPitch+(openFish?3f:0f);
                if(active)
                {
                    CollectFramingPoints();
                    FitWorkingView(yaw,pitch,lens);
                }
                else
                {
                    gameCamera.fieldOfView=lens;
                    gameCamera.transform.rotation=Quaternion.Euler(pitch,yaw,0f);
                    FramingSatisfied=true;
                }
            }
        }

        private void CollectFramingPoints()
        {
            framingPoints.Clear();
            int phase=(int)butchery.VisiblePhase;
            AddVisibleBounds(phase<2?bodyRenderers:phase<4?filletRenderers:halfRenderers);
            AddVisibleBounds(knifeRenderers);
            AddVisibleBounds(tweezerRenderers);
            AddBounds(new Bounds(body.right.palm.position,Vector3.one*.055f));
            AddBounds(new Bounds(body.left.palm.position,Vector3.one*.055f));
            foreach(var finger in body.fingerBones)if(finger!=null)framingPoints.Add(finger.position);
        }

        private static Renderer[] Renderers(Transform root)=>root!=null?root.GetComponentsInChildren<Renderer>(true):System.Array.Empty<Renderer>();
        private void AddVisibleBounds(Renderer[] renderers)
        {
            if(renderers==null)return;
            foreach(var renderer in renderers)
                if(renderer!=null&&renderer.enabled&&renderer.gameObject.activeInHierarchy)AddBounds(renderer.bounds);
        }
        private void AddBounds(Bounds bounds)
        {
            for(int i=0;i<8;i++)framingPoints.Add(bounds.center+Vector3.Scale(bounds.extents,
                new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
        }

        // The short supplied arms can put the eyes above the near edge of the
        // work surface. Keep those real eyes, targets and anatomy; turn the
        // camera down enough to see the current fish and attached tool hands.
        private void FitWorkingView(float yaw,float authoredPitch,float authoredLens)
        {
            const float minimumPitch=42f,maximumPitch=105f,maximumLens=100f;
            authoredLens=Mathf.Min(authoredLens,maximumLens);
            int phase=(int)butchery.VisiblePhase;
            if(RequiredLens(authoredPitch,yaw)<=authoredLens)
            {ApplyFraming(authoredPitch,yaw,authoredLens,phase);return;}
            // Reuse a valid pose throughout a phase rather than re-searching or
            // nodding the camera on every small knife pulse.
            if(fittedPhase==phase&&Mathf.Abs(fittedAspect-gameCamera.aspect)<.001f&&RequiredLens(fittedPitch,yaw)<=fittedLens)
            {ApplyFraming(fittedPitch,yaw,fittedLens,phase);return;}
            float selectedPitch=authoredPitch,selectedLens=authoredLens;
            float bestDistance=float.PositiveInfinity;
            // Retain the authored lens whenever a bounded pitch can fit it.
            for(float pitch=minimumPitch;pitch<=maximumPitch;pitch+=1f)
            {
                float required=RequiredLens(pitch,yaw);
                float distance=Mathf.Abs(pitch-authoredPitch);
                if(required<=authoredLens&&distance<bestDistance)
                {selectedPitch=pitch;bestDistance=distance;}
            }
            if(float.IsPositiveInfinity(bestDistance))
            {
                float bestCost=float.PositiveInfinity;
                for(float pitch=minimumPitch;pitch<=maximumPitch;pitch+=1f)
                {
                    float required=RequiredLens(pitch,yaw);
                    float cost=required+.35f*Mathf.Abs(pitch-authoredPitch);
                    if(cost>=bestCost)continue;
                    bestCost=cost;selectedPitch=pitch;selectedLens=Mathf.Clamp(required+.5f,authoredLens,maximumLens);
                }
            }
            ApplyFraming(selectedPitch,yaw,selectedLens,phase);
        }
        private void ApplyFraming(float pitch,float yaw,float lens,int phase)
        {
            gameCamera.fieldOfView=lens;gameCamera.transform.rotation=Quaternion.Euler(pitch,yaw,0f);
            fittedPhase=phase;fittedPitch=pitch;fittedLens=lens;fittedAspect=gameCamera.aspect;
            FramingSatisfied=RequiredLens(pitch,yaw)<=lens;
        }
        private float RequiredLens(float pitch,float yaw)
        {
            Quaternion inverse=Quaternion.Inverse(Quaternion.Euler(pitch,yaw,0f));
            float tangent=0f;
            foreach(var point in framingPoints)
            {
                Vector3 local=inverse*(point-gameCamera.transform.position);
                if(local.z<=gameCamera.nearClipPlane+.005f)return float.PositiveInfinity;
                // Aim inside 4% so the existing strict 2% regression still
                // holds while hands, cuts and camera stance move each frame.
                tangent=Mathf.Max(tangent,Mathf.Max(Mathf.Abs(local.y)/(.92f*local.z),
                    Mathf.Abs(local.x)/(.92f*gameCamera.aspect*local.z)));
            }
            return 2f*Mathf.Atan(tangent)*Mathf.Rad2Deg;
        }
    }
}
