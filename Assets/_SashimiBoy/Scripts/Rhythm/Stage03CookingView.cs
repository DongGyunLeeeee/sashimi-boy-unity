using System;
using UnityEngine;
namespace SashimiBoy
{
    [DefaultExecutionOrder(350)]
    public sealed class Stage03CookingView : MonoBehaviour
    {
        public KevinBodyRig body;
        public Camera gameCamera;
        public Transform knifeRoot,grip,contact;
        public Vector3 standingPosition;
        public float eyeHeight=1.64f,cookingPitch=48f;
        private Quaternion knifeRotation=Quaternion.identity;
        private Vector3 workPoint;
        private Vector3 supportPoint;
        private bool hasSupportPoint;
        private bool presentingPlate;
        private Vector3 plateFocus;
        private Renderer[] hiddenBodyRenderers;
        private bool[] originalRendererStates;
        private bool originalKnifeActive;
        private float originalFieldOfView;
        private bool motionActive, motionPaused, contactDelivered;
        private float motionElapsed, motionDuration;
        private int motionPhase;
        private Vector3 strokePoint, sweepAxis;
        private Quaternion strokeRotation=Quaternion.identity;
        private Action contactAction, finishedAction;
        private bool ready;
        public void SetWorkPoint(Vector3 value) {workPoint=value;ready=true;}
        public void SetSupportPoint(Vector3 value) {supportPoint=value;hasSupportPoint=true;}
        public void ClearSupportPoint() {hasSupportPoint=false;}
        public bool IsStriking=>motionActive;
        public bool MotionPaused=>motionPaused;
        public bool ContactDelivered=>contactDelivered;
        public float MotionProgress01=>motionActive?Mathf.Clamp01(motionElapsed/motionDuration):0f;
        public float RemainingStrokeSeconds=>motionActive?Mathf.Max(0,motionDuration-motionElapsed):0f;
        public int MotionPhase=>motionPhase;
        public Vector3 CurrentContactPoint=>motionActive?strokePoint:workPoint;
        public const float ContactFraction=.45f;
        public static float DurationForPhase(int phase)=>phase==0?.44f:phase<5?.52f:.46f;

        // The controller advances one presentation clock. Judge timestamps and
        // score never depend on these animation frames or on camera rendering.
        public bool BeginStroke(int phase,Vector3 contactPoint,Vector3 direction,Action onContact,Action onFinished)
        {
            if(motionActive||presentingPlate||phase<0||phase>8)return false;
            motionPhase=phase;strokePoint=contactPoint;
            sweepAxis=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
            if(sweepAxis.sqrMagnitude<.5f)sweepAxis=Vector3.right;
            Vector3 authored=Vector3.ProjectOnPlane(knifeRotation*Vector3.forward,Vector3.up).normalized;
            if(authored.sqrMagnitude<.5f)authored=Vector3.right;
            // A cut line has two equivalent axis directions. Keep the hilt on
            // its authored side instead of turning it through the fish and
            // making the attached body/eyes chase a backwards knife grip.
            Vector3 bladeAxis=Vector3.Dot(authored,sweepAxis)<0f?-sweepAxis:sweepAxis;
            strokeRotation=Quaternion.AngleAxis(Vector3.SignedAngle(authored,bladeAxis,Vector3.up),Vector3.up)*knifeRotation;
            motionDuration=DurationForPhase(phase);motionElapsed=0;contactDelivered=false;
            contactAction=onContact;finishedAction=onFinished;motionActive=true;
            PoseKnife(0);return true;
        }
        public void SetPaused(bool paused)=>motionPaused=paused;
        public void CancelStroke()
        {
            motionActive=contactDelivered=false;motionElapsed=0;
            contactAction=finishedAction=null;
        }
        public void AdvancePresentation(float deltaSeconds)
        {
            if(!motionActive||motionPaused||deltaSeconds<=0||float.IsNaN(deltaSeconds)||float.IsInfinity(deltaSeconds))return;
            motionElapsed=Mathf.Min(motionDuration,motionElapsed+deltaSeconds);
            if(!contactDelivered&&motionElapsed>=motionDuration*ContactFraction)
            {
                // Apply the actual contact pose before exchanging geometry.
                contactDelivered=true;PoseKnife(ContactFraction);
                var contact=contactAction;contactAction=null;contact?.Invoke();
                if(!motionActive)return; // A failed/cancelled transaction owns no late callback.
            }
            if(motionElapsed>=motionDuration)
            {
                PoseKnife(1);motionActive=false;
                var finished=finishedAction;contactAction=finishedAction=null;finished?.Invoke();
            }
        }
        public static Vector3 StrokeOffset(int phase,float progress)
        {
            float u=Mathf.Clamp01(progress);
            float travel=phase==0?.11f:phase<5?.16f:.105f;
            float lift=phase==0?.052f:phase<5?.025f:.038f;
            Vector3 approach=new Vector3(-travel*.5f,lift,0);
            Vector3 follow=new Vector3(travel*.5f,phase<5?.002f:.006f,0);
            if(u<=ContactFraction)return Vector3.Lerp(approach,Vector3.zero,Mathf.SmoothStep(0,1,u/ContactFraction));
            if(u<=.78f)return Vector3.Lerp(Vector3.zero,follow,Mathf.SmoothStep(0,1,(u-ContactFraction)/(.78f-ContactFraction)));
            return Vector3.Lerp(follow,new Vector3(0,.035f,0),Mathf.SmoothStep(0,1,(u-.78f)/.22f));
        }
        private void PoseKnife(float progress)
        {
            if(knifeRoot==null||contact==null)return;
            Vector3 offset=motionActive?StrokeOffset(motionPhase,progress):new Vector3(0,.035f,0);
            float tilt=motionActive?(motionPhase==0?8f:motionPhase<5?26f:17f)*Mathf.Sin(progress*Mathf.PI):0;
            knifeRoot.rotation=motionActive?Quaternion.AngleAxis(tilt,Vector3.Cross(Vector3.up,sweepAxis))*strokeRotation:strokeRotation;
            Vector3 target=motionActive?strokePoint+sweepAxis*offset.x+Vector3.up*offset.y:workPoint+Vector3.up*.035f;
            if(motionActive&&progress>.78f)
                target=Vector3.Lerp(target,workPoint+Vector3.up*.035f,Mathf.SmoothStep(0,1,(progress-.78f)/.22f));
            knifeRoot.position+=target-contact.position;
        }
        public void BeginPlatePresentation(Vector3 focus)
        {
            if(presentingPlate)return;
            CancelStroke();
            presentingPlate=true;plateFocus=focus;
            if(body!=null)
            {
                body.working=false;
                hiddenBodyRenderers=body.GetComponentsInChildren<Renderer>(true);
                originalRendererStates=new bool[hiddenBodyRenderers.Length];
                for(int i=0;i<hiddenBodyRenderers.Length;i++)
                {originalRendererStates[i]=hiddenBodyRenderers[i].enabled;hiddenBodyRenderers[i].enabled=false;}
            }
            if(knifeRoot!=null){originalKnifeActive=knifeRoot.gameObject.activeSelf;knifeRoot.gameObject.SetActive(false);}
            if(gameCamera!=null)originalFieldOfView=gameCamera.fieldOfView;
        }
        public void ResetPresentation()
        {
            CancelStroke();motionPaused=false;strokeRotation=knifeRotation;
            if(!presentingPlate)return;
            if(hiddenBodyRenderers!=null)
                for(int i=0;i<hiddenBodyRenderers.Length;i++)
                    if(hiddenBodyRenderers[i]!=null)hiddenBodyRenderers[i].enabled=originalRendererStates[i];
            if(knifeRoot!=null)knifeRoot.gameObject.SetActive(originalKnifeActive);
            if(gameCamera!=null)gameCamera.fieldOfView=originalFieldOfView;
            presentingPlate=false;
        }
        private void Awake() {if(body!=null)body.SetHeadHidden(true);if(knifeRoot!=null)knifeRotation=knifeRoot.rotation;strokeRotation=knifeRotation;}
        private void LateUpdate()
        {
            if(presentingPlate)
            {
                if(gameCamera==null)return;
                float blend=1-Mathf.Exp(-5*Time.unscaledDeltaTime);
                Vector3 destination=plateFocus+new Vector3(0,1.5f,-.4f);
                Vector3 aim=plateFocus+new Vector3(.30f,0,-.08f);
                gameCamera.transform.position=Vector3.Lerp(gameCamera.transform.position,destination,blend);
                gameCamera.transform.rotation=Quaternion.Slerp(gameCamera.transform.rotation,
                    Quaternion.LookRotation(aim-destination,Vector3.up),blend);
                gameCamera.fieldOfView=Mathf.Lerp(gameCamera.fieldOfView,50f,blend);
                return;
            }
            if(!ready||body==null||knifeRoot==null||grip==null||contact==null)return;
            if(motionPaused)return;
            PoseKnife(MotionProgress01);
            var stance=standingPosition;stance.x=Mathf.Clamp(workPoint.x-.14f,-.65f,.55f);
            body.working=true;
            body.rightTarget=grip.position;
            body.rightRotation=knifeRoot.rotation*Quaternion.Euler(0,180,0)*body.right.handWorldRest;
            body.leftTarget=hasSupportPoint?supportPoint:workPoint+new Vector3(-.13f,.012f,-.035f);
            if(!hasSupportPoint)body.leftTarget=new Vector3(body.leftTarget.x,body.leftTarget.y,Mathf.Min(body.leftTarget.z,-.16f));
            body.leftRotation=Quaternion.Euler(0,90,0)*body.left.handWorldRest;
            Vector3 preferred=Vector3.Lerp(body.transform.position,stance,1-Mathf.Exp(-12*Time.deltaTime));
            body.ApplyWorkingPoseAtRoot(preferred);
            if(gameCamera!=null)
            {
                gameCamera.transform.position=body.eyeAnchor!=null?body.eyeAnchor.position:body.transform.position+new Vector3(0,eyeHeight,.27f);
                Vector3 focus=new Vector3(workPoint.x,workPoint.y-.04f,-.1f);
                gameCamera.transform.rotation=Quaternion.LookRotation(focus-gameCamera.transform.position,Vector3.up);
            }
        }
    }
}
