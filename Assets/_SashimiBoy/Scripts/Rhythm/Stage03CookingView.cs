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
        private Quaternion knifeRotation;
        private Vector3 workPoint;
        private Vector3 supportPoint;
        private bool hasSupportPoint;
        private bool presentingPlate;
        private Vector3 plateFocus;
        private Renderer[] hiddenBodyRenderers;
        private bool[] originalRendererStates;
        private bool originalKnifeActive;
        private float originalFieldOfView;
        private float pulse;
        private bool ready;
        public void SetWorkPoint(Vector3 value) {workPoint=value;ready=true;}
        public void SetSupportPoint(Vector3 value) {supportPoint=value;hasSupportPoint=true;}
        public void ClearSupportPoint() {hasSupportPoint=false;}
        public bool IsStriking=>pulse>0;
        public void Pulse() {pulse=.28f;}
        public void BeginPlatePresentation(Vector3 focus)
        {
            if(presentingPlate)return;
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
            if(!presentingPlate)return;
            if(hiddenBodyRenderers!=null)
                for(int i=0;i<hiddenBodyRenderers.Length;i++)
                    if(hiddenBodyRenderers[i]!=null)hiddenBodyRenderers[i].enabled=originalRendererStates[i];
            if(knifeRoot!=null)knifeRoot.gameObject.SetActive(originalKnifeActive);
            if(gameCamera!=null)gameCamera.fieldOfView=originalFieldOfView;
            presentingPlate=false;pulse=0;
        }
        private void Awake() {if(body!=null)body.SetHeadHidden(true);if(knifeRoot!=null)knifeRotation=knifeRoot.rotation;}
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
            pulse=Mathf.Max(0,pulse-Time.deltaTime);
            float vertical=pulse>0?-Mathf.Sin(pulse/.28f*Mathf.PI)*.055f:.035f;
            knifeRoot.rotation=knifeRotation;
            knifeRoot.position+=workPoint+Vector3.up*vertical-contact.position;
            var stance=standingPosition;stance.x=Mathf.Clamp(workPoint.x-.14f,-.65f,.55f);
            body.working=true;
            body.rightTarget=grip.position;
            body.rightRotation=knifeRoot.rotation*Quaternion.Euler(0,180,0)*body.right.handWorldRest;
            body.leftTarget=hasSupportPoint?supportPoint:workPoint+new Vector3(-.13f,.012f,-.035f);
            if(!hasSupportPoint)body.leftTarget=new Vector3(body.leftTarget.x,body.leftTarget.y,Mathf.Min(body.leftTarget.z,-.16f));
            body.leftRotation=Quaternion.Euler(0,90,0)*body.left.handWorldRest;
            body.ApplyPose();
            Vector3 preferred=Vector3.Lerp(body.transform.position,stance,1-Mathf.Exp(-12*Time.deltaTime));
            body.transform.position=Stage03CookingStance.FitRootPosition(body,preferred);
            body.ApplyPose();
            if(gameCamera!=null)
            {
                gameCamera.transform.position=body.eyeAnchor!=null?body.eyeAnchor.position:body.transform.position+new Vector3(0,eyeHeight,.27f);
                Vector3 focus=new Vector3(workPoint.x,workPoint.y-.04f,-.1f);
                gameCamera.transform.rotation=Quaternion.LookRotation(focus-gameCamera.transform.position,Vector3.up);
            }
        }
    }
}
