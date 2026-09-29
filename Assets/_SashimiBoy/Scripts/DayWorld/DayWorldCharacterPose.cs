using UnityEngine;
namespace SashimiBoy
{
    public sealed class DayWorldCharacterPose : MonoBehaviour
    {
        public Transform head;
        private Quaternion headRest;
        private void Awake() { if(head != null) headRest=head.localRotation; }
        private void LateUpdate()
        {
            if(head != null) head.localRotation=headRest*Quaternion.Euler(Mathf.Sin(Time.time*.85f)*.7f,Mathf.Sin(Time.time*.47f)*1.2f,0f);
        }
    }
}
