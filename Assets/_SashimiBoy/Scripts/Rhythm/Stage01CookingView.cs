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
        private float fittedPitch,fittedYaw,fittedLens,fittedAspect;
        private int fishPointCount,yawCount;
        private readonly float[] yawCandidates=new float[10];
        public bool FramingSatisfied { get; private set; }
        public bool WorkAreaBelowHud { get; private set; }

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
            fishPointCount=framingPoints.Count;
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
            authoredLens=Mathf.Min(authoredLens,100f);
            int phase=(int)butchery.VisiblePhase;
            if(RequiredLens(authoredPitch,yaw,true)<=authoredLens)
            {ApplyFraming(authoredPitch,yaw,authoredLens,phase);return;}
            // Reuse a valid pose throughout a phase rather than re-searching or
            // nodding the camera on every small knife pulse.
            if(fittedPhase==phase&&Mathf.Abs(fittedAspect-gameCamera.aspect)<.001f&&RequiredLens(fittedPitch,fittedYaw,true)<=fittedLens)
            {ApplyFraming(fittedPitch,fittedYaw,fittedLens,phase);return;}
            BuildYawCandidates(yaw);
            if(TryFindFraming(yaw,authoredPitch,authoredLens,true,out var selected))
            {ApplyFraming(selected.x,selected.y,selected.z,phase);return;}
            // Keep the strict geometry framing when fitting below the overlay
            // is physically incompatible with the fixed eyes and 100-degree cap.
            TryFindFraming(yaw,authoredPitch,authoredLens,false,out selected);
            ApplyFraming(selected.x,selected.y,selected.z,phase);
        }

        private void BuildYawCandidates(float authoredYaw)
        {
            yawCount=0;AddYaw(authoredYaw);if(fittedPhase>=0)AddYaw(fittedYaw);
            Bounds all=new Bounds(framingPoints[0],Vector3.zero),fish=all;
            Vector3 rays=Vector3.zero;
            for(int i=0;i<framingPoints.Count;i++)
            {
                all.Encapsulate(framingPoints[i]);if(i<fishPointCount)fish.Encapsulate(framingPoints[i]);
                rays+=(framingPoints[i]-gameCamera.transform.position).normalized;
            }
            Vector3 direction=all.center-gameCamera.transform.position;
            float center=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            AddYaw(center);AddYaw(Mathf.DeltaAngle(0,center+180f));
            AddYaw(Mathf.Atan2(rays.x,rays.z)*Mathf.Rad2Deg);
            Vector3 fishDirection=fish.center-gameCamera.transform.position;
            AddYaw(Mathf.Atan2(fishDirection.x,fishDirection.z)*Mathf.Rad2Deg);
            AddYaw(center-15f);AddYaw(center+15f);AddYaw(Mathf.LerpAngle(authoredYaw,center,.5f));
        }
        private void AddYaw(float yaw)
        {
            yaw=Mathf.Clamp(Mathf.DeltaAngle(0,yaw),-80f,80f);
            for(int i=0;i<yawCount;i++)if(Mathf.Abs(yawCandidates[i]-yaw)<.5f)return;
            if(yawCount<yawCandidates.Length)yawCandidates[yawCount++]=yaw;
        }
        private bool TryFindFraming(float authoredYaw,float authoredPitch,float authoredLens,bool belowHud,out Vector3 selected)
        {
            selected=new Vector3(authoredPitch,authoredYaw,authoredLens);
            float bestCost=float.PositiveInfinity,bestOutside=float.PositiveInfinity;
            Vector3 outside=selected;
            for(int i=0;i<yawCount;i++)
                for(float pitch=42f;pitch<=105f;pitch+=2f)
                    ConsiderFraming(pitch,yawCandidates[i],authoredYaw,authoredPitch,authoredLens,belowHud,
                        ref selected,ref bestCost,ref outside,ref bestOutside);
            if(float.IsPositiveInfinity(bestCost)) {selected=outside;return false;}
            // Refine only the winning neighborhood rather than running a full
            // two-dimensional grid at every rhythm frame.
            Vector3 coarse=selected;
            for(float yaw=coarse.y-6f;yaw<=coarse.y+6f;yaw+=3f)
                for(float pitch=coarse.x-2f;pitch<=coarse.x+2f;pitch+=1f)
                    ConsiderFraming(Mathf.Clamp(pitch,42f,105f),Mathf.Clamp(yaw,-80f,80f),authoredYaw,authoredPitch,authoredLens,belowHud,
                        ref selected,ref bestCost,ref outside,ref bestOutside);
            return true;
        }
        private void ConsiderFraming(float pitch,float yaw,float authoredYaw,float authoredPitch,float authoredLens,bool belowHud,
            ref Vector3 selected,ref float bestCost,ref Vector3 outside,ref float bestOutside)
        {
            float required=RequiredLens(pitch,yaw,belowHud);
            float lens=Mathf.Max(authoredLens,required+.5f);
            float cost=lens+.35f*Mathf.Abs(pitch-authoredPitch)+.12f*Mathf.Abs(yaw-authoredYaw);
            if(cost<bestOutside){bestOutside=cost;outside=new Vector3(pitch,yaw,Mathf.Min(lens,100f));}
            if(lens<=100f&&cost<bestCost){bestCost=cost;selected=new Vector3(pitch,yaw,lens);}
        }
        private void ApplyFraming(float pitch,float yaw,float lens,int phase)
        {
            gameCamera.fieldOfView=lens;gameCamera.transform.rotation=Quaternion.Euler(pitch,yaw,0f);
            fittedPhase=phase;fittedPitch=pitch;fittedYaw=yaw;fittedLens=lens;fittedAspect=gameCamera.aspect;
            FramingSatisfied=RequiredLens(pitch,yaw,false)<=lens;
            WorkAreaBelowHud=RequiredLens(pitch,yaw,true)<=lens;
        }
        private float RequiredLens(float pitch,float yaw,bool belowHud)
        {
            Quaternion inverse=Quaternion.Inverse(Quaternion.Euler(pitch,yaw,0f));
            float tangent=0f;
            for(int i=0;i<framingPoints.Count;i++)
            {
                Vector3 local=inverse*(framingPoints[i]-gameCamera.transform.position);
                if(local.z<=gameCamera.nearClipPlane+.005f)return float.PositiveInfinity;
                // Aim inside 4% so the existing strict 2% regression still
                // holds while hands, cuts and camera stance move each frame.
                float vertical=belowHud&&i<fishPointCount&&local.y>0f?.20f:.92f;
                tangent=Mathf.Max(tangent,Mathf.Max(Mathf.Abs(local.y)/(vertical*local.z),
                    Mathf.Abs(local.x)/(.92f*gameCamera.aspect*local.z)));
            }
            return 2f*Mathf.Atan(tangent)*Mathf.Rad2Deg;
        }
    }
}
