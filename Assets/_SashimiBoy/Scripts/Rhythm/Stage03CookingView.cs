using System;
using System.Collections.Generic;
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
        private bool cameraPoseSet;
        private Vector3 fixedCameraPosition;
        private Quaternion fixedCameraRotation;
        public Vector3 SafeSupportTarget {get;private set;}
        public float SupportBladeClearance {get;private set;}
        public float SupportSurfaceGap {get;private set;}=float.NegativeInfinity;
        public int SupportMeshVertexCount=>supportSurface==null?0:supportSurface.VertexCount;
        private SupportSurface supportSurface;
        private Renderer[] knifeRenderers;
        private Transform[] supportJoints;
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
        public static float DurationForPhase(int phase)=>phase==3?.24f:phase==1||phase==4?.23f:phase>=6?.20f:.22f;

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
            float travel=phase==0?.06f:phase==1||phase==4?.105f:phase==2||phase==5?.08f:phase==3?0f:.055f;
            float lift=phase==1||phase==4?.012f:phase==3?.12f:phase==8?.065f:.035f;
            Vector3 approach=new Vector3(-travel*.5f,lift,0);
            Vector3 follow=new Vector3(travel*.5f,phase==1||phase==4?.002f:.006f,0);
            if(u<=ContactFraction)return Vector3.Lerp(approach,Vector3.zero,Mathf.SmoothStep(0,1,u/ContactFraction));
            if(u<=.78f)return Vector3.Lerp(Vector3.zero,follow,Mathf.SmoothStep(0,1,(u-ContactFraction)/(.78f-ContactFraction)));
            return Vector3.Lerp(follow,new Vector3(0,.035f,0),Mathf.SmoothStep(0,1,(u-.78f)/.22f));
        }
        private void PoseKnife(float progress)
        {
            if(knifeRoot==null||contact==null)return;
            Vector3 offset=motionActive?StrokeOffset(motionPhase,progress):new Vector3(0,.035f,0);
            float tilt=motionActive?(motionPhase==1||motionPhase==4?4f:motionPhase==3?0f:motionPhase==8?0f:12f)*Mathf.Sin(progress*Mathf.PI):0;
            Vector3 bladeForward=Vector3.ProjectOnPlane(strokeRotation*Vector3.forward,Vector3.up).normalized;
            // Pitch follows the selected blade facing too: a reversed draw
            // must not press the hilt (and Kevin's wrist) below the cut plane.
            knifeRoot.rotation=motionActive?Quaternion.AngleAxis(tilt,Vector3.Cross(Vector3.up,bladeForward))*strokeRotation:strokeRotation;
            Vector3 target=motionActive?strokePoint+sweepAxis*offset.x+Vector3.up*offset.y:workPoint+Vector3.up*.035f;
            if(motionActive&&motionPhase==3)target=strokePoint+Vector3.up*.16f; // Knife stays clear while the fish rolls.
            if(motionActive&&motionPhase==8)target+=Vector3.up*.07f; // Finishing beats keep the edge above the completed work.
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
            CancelStroke();motionPaused=false;strokeRotation=knifeRotation;cameraPoseSet=false;
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
            // Bend at the waist so the supplied shorter arms can work with
            // the grounded feet farther back, keeping the eyes behind the cut.
            body.workingLeanDegrees=40f;
            body.rightTarget=grip.position;
            body.rightRotation=knifeRoot.rotation*Quaternion.Euler(0,180,0)*body.right.handWorldRest;
            Vector3 requested=hasSupportPoint?supportPoint:workPoint+new Vector3(-.14f,.012f,0);
            Vector3 edgeDirection=Vector3.ProjectOnPlane(knifeRoot.forward,Vector3.up).normalized;
            Vector3 safeNormal=Vector3.Cross(Vector3.up,edgeDirection).normalized;
            if(Vector3.Dot(safeNormal,Vector3.left)<-.01f)safeNormal=-safeNormal;
            Vector3 edgePoint=motionActive?strokePoint:workPoint;
            float separation=Vector3.Dot(requested-edgePoint,safeNormal);
            // The palm and curled fingers remain on the side of the blade's
            // swept plane. Sliding along the knife is not a clearance measure.
            requested+=safeNormal*Mathf.Max(0,.155f-separation);
            if(motionActive&&motionPhase==3)requested=edgePoint+safeNormal*.18f+Vector3.up*.08f;
            SafeSupportTarget=requested;SupportBladeClearance=Vector3.Dot(requested-edgePoint,safeNormal);
            body.leftTarget=requested;
            body.leftRotation=Quaternion.Euler(0,90,0)*body.left.handWorldRest;
            Vector3 preferred=Vector3.Lerp(body.transform.position,stance,1-Mathf.Exp(-12*Time.deltaTime));
            body.ApplyWorkingPoseAtRoot(preferred);
            if(supportSurface==null)
            {
                supportSurface=new SupportSurface(body);
                supportJoints=body.left.hand.GetComponentsInChildren<Transform>();
                knifeRenderers=knifeRoot.GetComponentsInChildren<Renderer>(true);
                if(knifeRenderers.Length==0)throw new InvalidOperationException("Stage3 requires actual provided knife renderer geometry.");
            }
            supportSurface.ValidateForPose(body);
            // Joints alone omit the flesh around curled fingers. Evaluate only
            // the cached hand-influenced mesh and its incident triangles using
            // the exact four-weight skin, then re-fit the same grounded rig.
            MeasureSupport(edgePoint,safeNormal);
            for(int pass=0;pass<6;pass++)
            {
                float correction=Mathf.Max(.075f-SupportBladeClearance,.0205f-SupportSurfaceGap);
                if(correction<=0f)break;
                body.leftTarget+=safeNormal*correction;
                body.ApplyWorkingPoseAtRoot(preferred);
                MeasureSupport(edgePoint,safeNormal);
            }
            SafeSupportTarget=body.leftTarget;
            if(gameCamera!=null)
            {
                if(!cameraPoseSet)
                {
                    // Author one stable view of the complete board. Neither the
                    // grounded reach solver nor per-cut grip rotation owns it.
                    if(body.eyeAnchor==null)throw new InvalidOperationException("Stage3 requires the supplied body's real eye anchor.");
                    fixedCameraPosition=new Vector3(0,body.eyeAnchor.position.y,-.20f);
                    fixedCameraRotation=Quaternion.LookRotation(new Vector3(0,workPoint.y-.04f,-.07f)-fixedCameraPosition,Vector3.up);
                    cameraPoseSet=true;
                }
                gameCamera.transform.SetPositionAndRotation(fixedCameraPosition,fixedCameraRotation);
            }
        }

        private void MeasureSupport(Vector3 edgePoint,Vector3 normal)
        {
            SupportBladeClearance=float.PositiveInfinity;
            foreach(var joint in supportJoints)
                SupportBladeClearance=Mathf.Min(SupportBladeClearance,Vector3.Dot(joint.position-edgePoint,normal));
            float toolMax=float.NegativeInfinity;
            Vector3 absolute=new Vector3(Mathf.Abs(normal.x),Mathf.Abs(normal.y),Mathf.Abs(normal.z));
            foreach(var renderer in knifeRenderers)
            {
                if(renderer==null)throw new InvalidOperationException("The cached Stage3 knife renderer changed during play.");
                Bounds bounds=renderer.bounds;
                toolMax=Mathf.Max(toolMax,Vector3.Dot(bounds.center,normal)+Vector3.Dot(bounds.extents,absolute));
            }
            SupportSurfaceGap=supportSurface.MinimumProjection(body,normal)-toolMax;
            if(float.IsNaN(SupportSurfaceGap)||float.IsInfinity(SupportSurfaceGap))
                throw new InvalidOperationException("Stage3 support surface or knife bounds produced a non-finite separation.");
        }

        private sealed class SupportSurface
        {
            // Pinned to the currently provided, source-preserving generated body.
            // Asset/weight changes must fail visibly until their geometry contract
            // is independently checked; silently using a partial hand is unsafe.
            private const int SourceVertices=18976,ExpectedSelectedVertices=1190;
            private readonly SkinnedMeshRenderer renderer;
            private readonly Mesh mesh;
            private readonly Transform hand;
            private readonly Transform[] bones;
            private readonly Matrix4x4[] bindposes,matrices;
            private readonly int[] usedBones;
            private readonly Vector3[] vertices;
            private readonly BoneWeight[] weights;
            public int VertexCount=>vertices.Length;

            public SupportSurface(KevinBodyRig body)
            {
                hand=body.left.hand;
                foreach(var candidate in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(candidate.sharedMesh!=null&&candidate.sharedMesh.name=="MS_RiggedBody_WithoutHead")
                    {
                        if(renderer!=null)throw new InvalidOperationException("Multiple supplied Stage3 body surfaces were bound.");
                        renderer=candidate;
                    }
                if(renderer==null||hand==null)throw new InvalidOperationException("The supplied Stage3 body/support hand is missing.");
                mesh=renderer.sharedMesh;
                if(!mesh.isReadable||mesh.vertexCount!=SourceVertices)
                    throw new InvalidOperationException("The supplied Stage3 body geometry changed; expected 18976 readable vertices.");
                using(var influenceCounts=mesh.GetBonesPerVertex())
                {
                    if(influenceCounts.Length!=SourceVertices)throw new InvalidOperationException("Missing exact per-vertex skin influence counts.");
                    foreach(byte count in influenceCounts)
                        if(count==0||count>4)throw new InvalidOperationException("Stage3 support clearance requires the preserved one-to-four-weight skin representation.");
                }
                bones=renderer.bones;bindposes=mesh.bindposes;
                if(bones.Length==0||bindposes.Length!=bones.Length)throw new InvalidOperationException("Invalid supplied body bone/bindpose identity.");
                var allWeights=mesh.boneWeights;var allVertices=mesh.vertices;
                if(allWeights.Length!=SourceVertices)throw new InvalidOperationException("The supplied Stage3 four-weight vertex array changed.");
                var selected=new bool[SourceVertices];
                bool IsHand(int index)=>index>=0&&index<bones.Length&&bones[index]!=null&&(bones[index]==hand||bones[index].IsChildOf(hand));
                for(int i=0;i<allWeights.Length;i++)
                {
                    var w=allWeights[i];
                    if((IsHand(w.boneIndex0)&&w.weight0>0)||(IsHand(w.boneIndex1)&&w.weight1>0)||
                        (IsHand(w.boneIndex2)&&w.weight2>0)||(IsHand(w.boneIndex3)&&w.weight3>0))selected[i]=true;
                }
                var expanded=(bool[])selected.Clone();var triangles=mesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                    if(selected[triangles[i]]||selected[triangles[i+1]]||selected[triangles[i+2]])
                        expanded[triangles[i]]=expanded[triangles[i+1]]=expanded[triangles[i+2]]=true;
                var points=new List<Vector3>();var influences=new List<BoneWeight>();var needed=new bool[bones.Length];
                void NeedBone(int index,float weight)
                {
                    if(weight<=0)return;
                    if(index<0||index>=bones.Length||bones[index]==null)throw new InvalidOperationException("Missing nonzero support skin bone binding.");
                    needed[index]=true;
                }
                for(int i=0;i<expanded.Length;i++)if(expanded[i])
                {
                    var w=allWeights[i];
                    float sum=w.weight0+w.weight1+w.weight2+w.weight3;
                    if(Mathf.Abs(sum-1f)>.0001f)throw new InvalidOperationException("The supplied support skin weights are not normalized.");
                    NeedBone(w.boneIndex0,w.weight0);NeedBone(w.boneIndex1,w.weight1);NeedBone(w.boneIndex2,w.weight2);NeedBone(w.boneIndex3,w.weight3);
                    points.Add(allVertices[i]);influences.Add(w);
                }
                if(points.Count!=ExpectedSelectedVertices)throw new InvalidOperationException("The exact support-hand plus incident-triangle selection changed; expected 1190 vertices, got "+points.Count+".");
                vertices=points.ToArray();weights=influences.ToArray();matrices=new Matrix4x4[bones.Length];
                var used=new List<int>();for(int i=0;i<needed.Length;i++)if(needed[i])used.Add(i);usedBones=used.ToArray();
                ValidateForPose(body);
            }

            public void ValidateForPose(KevinBodyRig body)
            {
                if(renderer==null||renderer.sharedMesh!=mesh||mesh.vertexCount!=SourceVertices||body.left.hand!=hand)
                    throw new InvalidOperationException("Stage3 support skin source/hand identity changed during play.");
                if((renderer.quality!=SkinQuality.Auto&&renderer.quality!=SkinQuality.Bone4)||
                    (renderer.quality==SkinQuality.Auto&&QualitySettings.skinWeights!=SkinWeights.FourBones&&QualitySettings.skinWeights!=SkinWeights.Unlimited))
                    throw new InvalidOperationException("Stage3 support clearance requires actual four-weight skin rendering quality.");
                // bones[] is a serialized reference array; transforms may rotate,
                // but no referenced skin bone may be removed or substituted.
                var currentBones=renderer.bones;
                if(currentBones.Length!=bones.Length)throw new InvalidOperationException("Stage3 support bone array changed during play.");
                for(int i=0;i<bones.Length;i++)if(currentBones[i]!=bones[i]||bones[i]==null)
                    throw new InvalidOperationException("Stage3 support bone identity changed during play.");
            }

            public float MinimumProjection(KevinBodyRig body,Vector3 normal)
            {
                if(renderer==null||renderer.sharedMesh!=mesh||mesh.vertexCount!=SourceVertices||body.left.hand!=hand)
                    throw new InvalidOperationException("Stage3 support skin source/hand identity changed during a pose solve.");
                foreach(int index in usedBones)matrices[index]=bones[index].localToWorldMatrix*bindposes[index];
                float minimum=float.PositiveInfinity;
                for(int i=0;i<vertices.Length;i++)
                {
                    Vector3 point=vertices[i];var w=weights[i];Vector3 world=Vector3.zero;
                    if(w.weight0>0)world+=matrices[w.boneIndex0].MultiplyPoint3x4(point)*w.weight0;
                    if(w.weight1>0)world+=matrices[w.boneIndex1].MultiplyPoint3x4(point)*w.weight1;
                    if(w.weight2>0)world+=matrices[w.boneIndex2].MultiplyPoint3x4(point)*w.weight2;
                    if(w.weight3>0)world+=matrices[w.boneIndex3].MultiplyPoint3x4(point)*w.weight3;
                    minimum=Mathf.Min(minimum,Vector3.Dot(world,normal));
                }
                return minimum;
            }
        }
    }
}
