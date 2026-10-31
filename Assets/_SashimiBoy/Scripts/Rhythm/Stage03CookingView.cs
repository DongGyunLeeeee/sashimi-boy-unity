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
        private float pulse;
        private bool ready;
        public void SetWorkPoint(Vector3 value) {workPoint=value;ready=true;}
        public void Pulse() {pulse=.28f;}
        private void Awake() {if(body!=null)body.SetHeadHidden(true);if(knifeRoot!=null)knifeRotation=knifeRoot.rotation;}
        private void LateUpdate()
        {
            if(!ready||body==null||knifeRoot==null||grip==null||contact==null)return;
            pulse=Mathf.Max(0,pulse-Time.deltaTime);
            float lift=pulse>0?Mathf.Sin(pulse/.28f*Mathf.PI)*.075f:0;
            knifeRoot.rotation=knifeRotation;
            knifeRoot.position+=workPoint+Vector3.up*(.025f+lift)-contact.position;
            var stance=standingPosition;stance.x=Mathf.Clamp(workPoint.x-.14f,-.65f,.55f);
            body.transform.position=Vector3.Lerp(body.transform.position,stance,1-Mathf.Exp(-12*Time.deltaTime));
            body.working=true;
            body.rightTarget=grip.position;
            body.rightRotation=knifeRoot.rotation*Quaternion.Euler(0,180,0)*body.right.handWorldRest;
            body.leftTarget=workPoint+new Vector3(-.13f,.012f,-.035f);
            body.leftTarget=new Vector3(body.leftTarget.x,body.leftTarget.y,Mathf.Min(body.leftTarget.z,-.16f));
            body.leftRotation=Quaternion.Euler(0,90,0)*body.left.handWorldRest;
            body.ApplyPose();
            if(gameCamera!=null)
            {
                gameCamera.transform.position=body.eyeAnchor!=null?body.eyeAnchor.position:body.transform.position+new Vector3(0,eyeHeight,.27f);
                gameCamera.transform.rotation=Quaternion.Euler(cookingPitch,0,0);
            }
        }
    }
}
