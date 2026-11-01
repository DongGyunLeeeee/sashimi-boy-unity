using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using Unity.Collections;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    // Opt-in editor evidence only. Authored targets and the generated skin are
    // real; input timestamps are synthetic and no Player/human PASS is implied.
    public static class KevinRiggedBodyDiagnostics
    {
        [Serializable] private sealed class Pose
        {
            public string context;
            public Vector3 root, rightTarget, leftTarget;
            public float rightError, leftError, rightUpper, rightLower, leftUpper, leftLower;
            public float workingLeanDegrees;
            public Vector3 rightShoulder, leftShoulder;
        }
        [Serializable] private sealed class FaceGeometry
        {
            public string id, meshPath, bakedVerticesSha256;
            public int vertices, bones, bindposes;
            public bool finite, allBonesAssigned;
            public Vector3 worldMinimum, worldMaximum;
        }
        [Serializable] private sealed class SurfaceTransform
        {
            public string capture, renderer;
            public Vector3 originalScale, cloneScale;
            public Vector3 originalMinimum, originalMaximum, cloneMinimum, cloneMaximum;
            public Vector3 bakedLocalMinimum, bakedLocalMaximum, bakedTransformPointMinimum, bakedTransformPointMaximum;
        }
        [Serializable] private sealed class Report
        {
            public string evidenceKind = "Authored-scene targets and freshly cloned posed skinned hierarchies rendered in isolated previews; separate CPU BakeMesh bounds are diagnostic only. Not physical input, runtime face switching, DSP, story/save or Owner approval.";
            public List<Pose> poses = new List<Pose>();
            public List<FaceGeometry> faces = new List<FaceGeometry>();
            public List<SurfaceTransform> surfaces = new List<SurfaceTransform>();
        }
        [Serializable] private sealed class LegacyCameraPose
        {
            public string scene;
            public int phase;
            public float aspect,fieldOfView;
            public Vector3 position,forward,fishMinimum,fishMaximum,viewportMinimum,viewportMaximum,rightPalm,leftPalm,allViewportMinimum,allViewportMaximum;
            public bool framingSatisfied,workAreaBelowHud;
            public int framingPointCount,outsidePointCount;
        }
        [Serializable] private sealed class LegacyCameraReport
        {
            public string evidenceKind="Editor-authored stable phase/zero remaining tool action and synthetic note events; actual Game camera and fresh posed body. Not runtime input/music/save or Owner approval.";
            public List<LegacyCameraPose> poses=new List<LegacyCameraPose>();
        }
        [Serializable] private sealed class StrokeCameraPose
        {
            public int phase,ordinal;
            public float requestedU,actualU,fieldOfView,rightError,leftError,cameraBodyForwardDot,authoredSweepDot;
            public bool striking,contactDelivered,supportMeshInspected;
            public int supportMeshVertices;
            public string supportHandBone;
            public float supportMeshPlaneGap,supportRuntimeGap,supportBoneGap,supportHandProjection,supportKnifeProjection,supportCachedKnifeProjection,supportCpuHandProjection,supportCpuBakeMaxError,supportBakeScalePathMaxError;
            public Vector3 supportRendererScale;
            public Vector3 root,eye,camera,cameraForward,focus,workPoint,contact,grip,gripContactOffset;
            public Vector3 authoredBlade,sweepAxis,actualBlade;
            public Vector3 rightTarget,leftTarget,rightUpper,rightLower,rightHand,rightPalm,leftUpper,leftLower,leftHand,leftPalm;
            public Vector3 rightWristTarget,leftWristTarget,rootScale;
            public Quaternion rootRotation,rightRotation,leftRotation;
        }
        [Serializable] private sealed class StrokeCameraReport
        {
            public string evidenceKind="Editor-only ordered actual BeginStroke/AdvancePresentation/LateUpdate samples, visual part transitions and fresh posed Game-camera renders. No DSP, judged gameplay, reward/save, fixed-stance feasibility or Owner PASS is asserted.";
            public string scope;
            public string supportGeometryScope="Selected first/quota/last strokes at six poses each; all nonzero hand/finger influenced vertices plus their incident-triangle vertices; generated body has the preserved four-weight representation. Unity6000.4 BakeMesh(true)+fullTRS is cross-checked against false+TR(noScale) and per-vertex CPU world skin. Positive plane gap proves separation only at the recorded poses, not human approval.";
            public List<StrokeCameraPose> poses=new List<StrokeCameraPose>();
        }

        public static void InspectStage03StrokeCamerasBatch()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode first.");
            for(int i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save unsaved scenes first.");
            Directory.CreateDirectory(KevinRiggedBodyAuthoring.EvidenceDirectory);
            EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/Stage03_Flounder.unity",OpenSceneMode.Single);
            var controller=Object.FindFirstObjectByType<Stage03GameController>();
            if(controller==null||controller.cooking==null)throw new InvalidOperationException("Stage3 authored controller/view missing.");
            // Runtime detachment reparents parts. Unpack only the temporary
            // loaded scene instance; never save this diagnostic scene/asset.
            var assemblyInstance=PrefabUtility.GetOutermostPrefabInstanceRoot(controller.assembly.gameObject);
            if(assemblyInstance!=null)PrefabUtility.UnpackPrefabInstance(assemblyInstance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var cooking=controller.cooking;var body=cooking.body;
            foreach(var surface in controller.fillets)Invoke(surface,"Awake");
            Invoke(cooking,"Awake");if(controller.hudView!=null)Invoke(controller.hudView,"Awake");controller.autoStart=false;Invoke(controller,"Start");cooking.gameCamera.aspect=16f/9f;
            controller.startPanel.SetActive(false);
            if(!controller.StartRun())throw new InvalidOperationException("Diagnostic AudioClock did not start.");
            Vector3 authored=Vector3.ProjectOnPlane(cooking.knifeRoot.forward,Vector3.up).normalized;
            bool all=Environment.GetEnvironmentVariable("SASHIMI_STROKE_ALL_PHASES")=="1";
            var report=new StrokeCameraReport{scope=all?"Eight sections, every authored note through actual public controller APIs; selected first/quota/last strokes rendered.":"First section through actual public controller APIs."};
            float[] samples={0f,.2f,.45f,.65f,.85f,1f};
            for(int phase=0;phase<(all?8:1);phase++)
            {
                if(controller.Run.Phase!=phase)throw new InvalidOperationException("Diagnostic phase did not advance: "+phase);
                var notes=controller.chart.phases[phase].noteTimesMs;
                int count=notes.Length;
                for(int ordinal=0;ordinal<count;ordinal++)
                {
                    Vector3 direction=(Vector3)controller.GetType().GetMethod("CutDirection",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,new object[]{phase});
                    if(!controller.SubmitAt(notes[ordinal]))throw new InvalidOperationException("Diagnostic actual controller hit rejected: "+phase+"/"+ordinal);
                    float duration=cooking.RemainingStrokeSeconds,previous=0;
                    foreach(float u in samples)
                    {
                        float delta=u==1f?cooking.RemainingStrokeSeconds:(u-previous)*duration;previous=u;
                        if(delta>0)controller.AdvancePresentation(delta);
                        Invoke(cooking,"LateUpdate");body.ApplyPose();if(controller.hudView!=null)controller.hudView.Refresh();
                        Vector3 currentWork=(Vector3)cooking.GetType().GetField("workPoint",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cooking);
                        Vector3 focus=currentWork+Vector3.down*.04f;
                        bool inspectGeometry=ordinal==0||ordinal==count-1||ordinal==controller.Run.RequiredHits-1;
                        var gap=inspectGeometry?InspectSupportMeshPlane(cooking):new SupportInspection();
                        report.poses.Add(new StrokeCameraPose{phase=phase,ordinal=ordinal,requestedU=u,actualU=cooking.MotionProgress01,
                            supportMeshInspected=inspectGeometry,supportMeshVertices=gap.vertices,supportMeshPlaneGap=gap.gap,supportHandBone=body.left.hand.name,supportRuntimeGap=cooking.SupportSurfaceGap,supportBoneGap=cooking.SupportBladeClearance,
                            supportHandProjection=gap.handMin,supportKnifeProjection=gap.toolMax,supportCachedKnifeProjection=gap.cachedToolMax,supportCpuHandProjection=gap.cpuHandMin,supportCpuBakeMaxError=gap.cpuBakeMaxError,supportBakeScalePathMaxError=gap.bakeScalePathMaxError,supportRendererScale=gap.rendererScale,
                            striking=cooking.IsStriking,contactDelivered=cooking.ContactDelivered,fieldOfView=cooking.gameCamera.fieldOfView,
                            root=body.transform.position,rootRotation=body.transform.rotation,rootScale=body.transform.lossyScale,
                            eye=body.eyeAnchor.position,camera=cooking.gameCamera.transform.position,cameraForward=cooking.gameCamera.transform.forward,
                            cameraBodyForwardDot=Vector3.Dot(cooking.gameCamera.transform.forward,body.transform.forward),
                            focus=focus,workPoint=currentWork,contact=cooking.contact.position,grip=cooking.grip.position,
                            authoredBlade=authored,sweepAxis=direction.normalized,actualBlade=cooking.knifeRoot.forward,
                            authoredSweepDot=Vector3.Dot(authored,direction.normalized),gripContactOffset=cooking.grip.position-cooking.contact.position,
                            rightTarget=body.rightTarget,leftTarget=body.leftTarget,rightRotation=body.rightRotation,leftRotation=body.leftRotation,
                            rightUpper=body.right.upper.position,rightLower=body.right.lower.position,rightHand=body.right.hand.position,rightPalm=body.right.palm.position,
                            leftUpper=body.left.upper.position,leftLower=body.left.lower.position,leftHand=body.left.hand.position,leftPalm=body.left.palm.position,
                            rightWristTarget=body.rightTarget-body.rightRotation*Vector3.Scale(body.right.palm.localPosition,body.right.hand.lossyScale),
                            leftWristTarget=body.leftTarget-body.leftRotation*Vector3.Scale(body.left.palm.localPosition,body.left.hand.lossyScale),
                            rightError=Vector3.Distance(body.right.palm.position,body.rightTarget),leftError=Vector3.Distance(body.left.palm.position,body.leftTarget)});
                        if(ordinal==0||ordinal==count-1||ordinal==controller.Run.RequiredHits-1)
                            CaptureStrokeGameCamera(cooking,"stroke-phase-"+phase+"-ordinal-"+ordinal+"-u"+Mathf.RoundToInt(u*100).ToString("000")+".png");

                    }
                }
                controller.AdvancePresentation(2f);
                controller.TickAt(controller.chart.phases[phase].endTimeMs+1);
                if(controller.Run.Failed||controller.Run.Phase!=phase+1)throw new InvalidOperationException("Diagnostic section deadline failed: "+phase);
            }
            controller.clock.Stop();
            File.WriteAllText(Path.Combine(KevinRiggedBodyAuthoring.EvidenceDirectory,"stage03-stroke-camera-evidence.json"),JsonUtility.ToJson(report,true));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Debug.Log("[OwnerFeedback87StrokeCamera] Editor stroke poses/rendering recorded, not runtime PASS: "+KevinRiggedBodyAuthoring.EvidenceDirectory);
        }

        // A positive separating-plane gap is a conservative actual-geometry
        // nonintersection proof for this stroke's longitudinal blade sweep.
        // Read the provided knife in the Editor without changing its importer.
        private sealed class SupportInspection
        {public int vertices;public float gap,handMin,toolMax,cachedToolMax,cpuHandMin,cpuBakeMaxError,bakeScalePathMaxError;public Vector3 rendererScale;}
        private static SupportInspection InspectSupportMeshPlane(Stage03CookingView cooking)
        {
            Vector3 axis=Vector3.ProjectOnPlane(cooking.knifeRoot.forward,Vector3.up).normalized;
            Vector3 normal=Vector3.Cross(Vector3.up,axis).normalized;
            if(Vector3.Dot(normal,Vector3.left)<-.01f)normal=-normal;
            float toolMax=float.NegativeInfinity,handMin=float.PositiveInfinity,cpuBakeMaxError=0,bakeScalePathMaxError=0;int count=0;Vector3 rendererScale=Vector3.zero;
            foreach(var filter in cooking.knifeRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                using(var data=MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
                using(var points=new NativeArray<Vector3>(data[0].vertexCount,Allocator.Temp))
                {
                    data[0].GetVertices(points);
                    foreach(var point in points)toolMax=Mathf.Max(toolMax,Vector3.Dot(filter.transform.TransformPoint(point),normal));
                }
            }
            Transform hand=cooking.body.left.hand;
            foreach(var renderer in cooking.body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!renderer.enabled||renderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly)continue;
                var mesh=renderer.sharedMesh;if(mesh==null)continue;
                var weights=mesh.boneWeights;var bones=renderer.bones;
                var selected=new bool[mesh.vertexCount];
                var perVertex=mesh.GetBonesPerVertex(); // Borrowed view owned by Mesh.
                foreach(byte influences in perVertex)if(influences>4)throw new InvalidOperationException("The support inspection requires the exact generated four-weight representation.");
                bool IsHand(int index)=>index>=0&&index<bones.Length&&bones[index]!=null&&(bones[index]==hand||bones[index].IsChildOf(hand));
                for(int i=0;i<weights.Length;i++)
                {
                    var w=weights[i];
                    float influence=(IsHand(w.boneIndex0)?w.weight0:0)+(IsHand(w.boneIndex1)?w.weight1:0)+
                        (IsHand(w.boneIndex2)?w.weight2:0)+(IsHand(w.boneIndex3)?w.weight3:0);
                    if(influence>0f)selected[i]=true;
                }
                if(!selected.Any(value=>value))continue;
                // Include whole incident triangles so a low-weight seam vertex
                // or the transition at the wrist cannot be silently omitted.
                var expanded=(bool[])selected.Clone();var triangles=mesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                    if(selected[triangles[i]]||selected[triangles[i+1]]||selected[triangles[i+2]])
                        expanded[triangles[i]]=expanded[triangles[i+1]]=expanded[triangles[i+2]]=true;
                var indices=Enumerable.Range(0,expanded.Length).Where(i=>expanded[i]).ToArray();
                var baked=new Mesh();var scaledBake=new Mesh();
                try
                {
                    // Unity 6000.4: true compensates Transform scale. False
                    // already includes that scale; use only TR for that path.
                    renderer.BakeMesh(baked,true);renderer.BakeMesh(scaledBake,false);
                    var positions=baked.vertices;var scaledPositions=scaledBake.vertices;
                    var unscaledTR=Matrix4x4.TRS(renderer.transform.position,renderer.transform.rotation,Vector3.one);
                    var bindposes=mesh.bindposes;var matrices=bones.Select((bone,i)=>bone.localToWorldMatrix*bindposes[i]).ToArray();
                    var sourcePositions=mesh.vertices;rendererScale=renderer.transform.lossyScale;
                    foreach(int index in indices)
                    {
                        Vector3 world=renderer.transform.TransformPoint(positions[index]);
                        Vector3 alternate=unscaledTR.MultiplyPoint3x4(scaledPositions[index]);
                        var w=weights[index];Vector3 point=sourcePositions[index],cpu=Vector3.zero;
                        if(w.weight0>0)cpu+=matrices[w.boneIndex0].MultiplyPoint3x4(point)*w.weight0;
                        if(w.weight1>0)cpu+=matrices[w.boneIndex1].MultiplyPoint3x4(point)*w.weight1;
                        if(w.weight2>0)cpu+=matrices[w.boneIndex2].MultiplyPoint3x4(point)*w.weight2;
                        if(w.weight3>0)cpu+=matrices[w.boneIndex3].MultiplyPoint3x4(point)*w.weight3;
                        cpuBakeMaxError=Mathf.Max(cpuBakeMaxError,Vector3.Distance(cpu,world));
                        bakeScalePathMaxError=Mathf.Max(bakeScalePathMaxError,Vector3.Distance(alternate,world));
                        handMin=Mathf.Min(handMin,Vector3.Dot(world,normal));count++;
                    }
                }
                finally{Object.DestroyImmediate(baked);Object.DestroyImmediate(scaledBake);}
            }
            if(count==0||float.IsNegativeInfinity(toolMax))throw new InvalidOperationException("Actual support-hand or provided knife geometry could not be inspected.");
            float cachedToolMax=float.NegativeInfinity;
            Vector3 absolute=new Vector3(Mathf.Abs(normal.x),Mathf.Abs(normal.y),Mathf.Abs(normal.z));
            foreach(var renderer in cooking.knifeRoot.GetComponentsInChildren<Renderer>(true))
            {var bounds=renderer.bounds;cachedToolMax=Mathf.Max(cachedToolMax,Vector3.Dot(bounds.center,normal)+Vector3.Dot(bounds.extents,absolute));}
            var surface=cooking.GetType().GetField("supportSurface",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cooking);
            float cpuHandMin=(float)surface.GetType().GetMethod("MinimumProjection").Invoke(surface,new object[]{cooking.body,normal});
            return new SupportInspection{vertices=count,gap=handMin-toolMax,handMin=handMin,toolMax=toolMax,cachedToolMax=cachedToolMax,cpuHandMin=cpuHandMin,cpuBakeMaxError=cpuBakeMaxError,bakeScalePathMaxError=bakeScalePathMaxError,rendererScale=rendererScale};
        }

        private static void CaptureStrokeGameCamera(Stage03CookingView cooking,string file)
        {
            Camera camera=cooking.gameCamera;
            var originals=cooking.body.GetComponentsInChildren<Renderer>(true);var states=originals.Select(r=>r.enabled).ToArray();
            var snapshot=CloneHierarchy(cooking.body.transform,cooking.body.gameObject.scene);
            var appearance=cooking.body.GetComponent<KevinAppearance>();
            if(appearance!=null)snapshot.GetComponent<KevinAppearance>()?.ApplyFace(appearance.SelectedFaceId);
            var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            var target=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var canvases=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.renderMode==RenderMode.ScreenSpaceOverlay)
                .Select(c=>(canvas:c,camera:c.worldCamera,distance:c.planeDistance)).ToArray();
            try
            {
                foreach(var renderer in originals)renderer.enabled=false;
                foreach(var item in canvases){item.canvas.renderMode=RenderMode.ScreenSpaceCamera;item.canvas.worldCamera=camera;item.canvas.planeDistance=.15f;}
                Canvas.ForceUpdateCanvases();camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(KevinRiggedBodyAuthoring.EvidenceDirectory,file),pixels.EncodeToPNG());
            }
            finally
            {
                foreach(var item in canvases){item.canvas.renderMode=RenderMode.ScreenSpaceOverlay;item.canvas.worldCamera=item.camera;item.canvas.planeDistance=item.distance;}
                for(int i=0;i<originals.Length;i++)originals[i].enabled=states[i];
                camera.targetTexture=oldTarget;RenderTexture.active=oldActive;
                Object.DestroyImmediate(snapshot);Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);
            }
        }

        public static void InspectLegacyGameCamerasBatch()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode first.");
            for(int i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save unsaved scenes first.");
            Directory.CreateDirectory(KevinRiggedBodyAuthoring.EvidenceDirectory);
            var report=new LegacyCameraReport();
            foreach(string scene in new[]{"Stage01_Salmon","Stage02_Rockfish"})
            {
                EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/"+scene+".unity",OpenSceneMode.Single);
                var cooking=Object.FindFirstObjectByType<Stage01CookingView>();var view=cooking.butchery;var timing=view.timing;
                Invoke(timing,"Awake");Invoke(view,"Awake");view.Bind(timing);Invoke(cooking,"Awake");
                for(int phase=0;phase<6;phase++)
                {
                    if((int)view.VisiblePhase!=phase)throw new InvalidOperationException("Stable editor phase did not advance: "+scene+"/"+phase);
                    view.GetType().GetField("actionTime",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,0f);
                    Invoke(view,"Update");view.handRoot.gameObject.SetActive(true);
                    foreach(float aspect in new[]{16f/9f,4f/3f})
                    {
                        var camera=cooking.gameCamera;camera.aspect=aspect;
                        Vector3 work=view.GetCueWorldPosition(timing.PhasePerformance.NoteCursor);
                        Vector3 stance=cooking.standingPosition;stance.x=Mathf.Clamp(work.x-.14f,-.65f,.55f);
                        cooking.body.transform.position=stance;
                        Invoke(cooking,"LateUpdate");
                        var root=phase<2?view.assembly.body.transform:phase<4?view.assembly.fillet.transform:view.filletHalf.transform;
                        var renderers=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
                        if(renderers.Length==0)throw new InvalidOperationException("Active fish geometry missing: "+scene+"/"+phase);
                        Bounds fish=renderers[0].bounds;foreach(var renderer in renderers)fish.Encapsulate(renderer.bounds);
                        Vector3 minimum=Vector3.one*float.PositiveInfinity,maximum=Vector3.one*float.NegativeInfinity;
                        for(int i=0;i<8;i++)
                        {
                            Vector3 corner=fish.center+Vector3.Scale(fish.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                            Vector3 viewport=camera.WorldToViewportPoint(corner);minimum=Vector3.Min(minimum,viewport);maximum=Vector3.Max(maximum,viewport);
                        }
                        var framing=(List<Vector3>)cooking.GetType().GetField("framingPoints",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cooking);
                        Vector3 allMin=Vector3.one*float.PositiveInfinity,allMax=Vector3.one*float.NegativeInfinity;
                        int outside=0;
                        foreach(var point in framing)
                        {
                            Vector3 viewport=camera.WorldToViewportPoint(point);allMin=Vector3.Min(allMin,viewport);allMax=Vector3.Max(allMax,viewport);
                            if(viewport.z<=camera.nearClipPlane||viewport.x<.02f||viewport.x>.98f||viewport.y<.02f||viewport.y>.98f)outside++;
                        }
                        report.poses.Add(new LegacyCameraPose{scene=scene,phase=phase,aspect=aspect,position=camera.transform.position,
                            forward=camera.transform.forward,fieldOfView=camera.fieldOfView,fishMinimum=fish.min,fishMaximum=fish.max,
                            viewportMinimum=minimum,viewportMaximum=maximum,rightPalm=camera.WorldToViewportPoint(cooking.body.right.palm.position),
                            leftPalm=camera.WorldToViewportPoint(cooking.body.left.palm.position),framingSatisfied=cooking.FramingSatisfied,
                            workAreaBelowHud=cooking.WorkAreaBelowHud,allViewportMinimum=allMin,allViewportMaximum=allMax,
                            framingPointCount=framing.Count,outsidePointCount=outside});
                        CaptureActualGameCamera(cooking,scene+"-phase-"+phase+"-"+(aspect>1.5f?"16x9":"4x3")+"-game.png");
                    }
                    var gate=timing.semanticBeatmap.chart.phases[phase];
                    if(phase<5)for(int note=gate.firstNoteId;note<=gate.lastNoteId;note++)
                        Invoke(timing,"ResolveGameplayInput",timing.notePatternProvider.RuntimeNotes[note].songTimeSeconds);
                }
            }
            File.WriteAllText(Path.Combine(KevinRiggedBodyAuthoring.EvidenceDirectory,"legacy-game-camera-evidence.json"),JsonUtility.ToJson(report,true));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Debug.Log("[OwnerFeedback87LegacyCamera] Authored stable Game-camera framing recorded, not runtime PASS: "+KevinRiggedBodyAuthoring.EvidenceDirectory);
        }

        private static void CaptureActualGameCamera(Stage01CookingView cooking,string file)
        {
            Camera camera=cooking.gameCamera;
            var originalRenderers=cooking.body.GetComponentsInChildren<Renderer>(true);
            var enabledStates=originalRenderers.Select(r=>r.enabled).ToArray();
            var snapshot=CloneHierarchy(cooking.body.transform,cooking.body.gameObject.scene);
            var sourceAppearance=cooking.body.GetComponent<KevinAppearance>();
            if(sourceAppearance!=null)snapshot.GetComponent<KevinAppearance>()?.ApplyFace(sourceAppearance.SelectedFaceId);
            var previousTarget=camera.targetTexture;var previousActive=RenderTexture.active;
            var target=new RenderTexture(1280,Mathf.RoundToInt(1280/camera.aspect),24);
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            var canvases=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c=>c.renderMode==RenderMode.ScreenSpaceOverlay)
                .Select(c=>(canvas:c,camera:c.worldCamera,distance:c.planeDistance)).ToArray();
            try
            {
                foreach(var renderer in originalRenderers)renderer.enabled=false;
                foreach(var item in canvases){item.canvas.renderMode=RenderMode.ScreenSpaceCamera;item.canvas.worldCamera=camera;item.canvas.planeDistance=.15f;}
                Canvas.ForceUpdateCanvases();camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(KevinRiggedBodyAuthoring.EvidenceDirectory,file),pixels.EncodeToPNG());
            }
            finally
            {
                foreach(var item in canvases){item.canvas.renderMode=RenderMode.ScreenSpaceOverlay;item.canvas.worldCamera=item.camera;item.canvas.planeDistance=item.distance;}
                for(int i=0;i<originalRenderers.Length;i++)originalRenderers[i].enabled=enabledStates[i];
                camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
                Object.DestroyImmediate(snapshot);Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);
            }
        }

        public static void InspectBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before pose inspection.");
            for (int i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save unsaved scenes before pose inspection.");
            Directory.CreateDirectory(KevinRiggedBodyAuthoring.EvidenceDirectory);
            var report = new Report();
            InspectFaces(report);
            foreach (string name in new[] { "Stage01_Salmon", "Stage02_Rockfish" }) InspectCooking(name,report);
            InspectStageThreeRest(report);
            InspectInstruments(report);
            File.WriteAllText(Path.Combine(KevinRiggedBodyAuthoring.EvidenceDirectory,"authored-pose-evidence.json"),JsonUtility.ToJson(report,true));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Debug.Log("[OwnerFeedback87BodyInspection] Captured supplied skin, four faces, authored Stage1/2 tool phases, Stage3 rest and all placed instrument strokes. Static diagnostic only. Evidence: "+KevinRiggedBodyAuthoring.EvidenceDirectory);
        }

        private static void InspectFaces(Report report)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KevinEmbodimentAuthoring.PrefabPath));
            try
            {
                var rig=actor.GetComponent<KevinBodyRig>();var appearance=actor.GetComponent<KevinAppearance>();
                rig.working=false;rig.SetHeadHidden(false);rig.ApplyPose();
                foreach(string id in new[]{"CuteFace","PlainFace","AmbiguousFace","WesternFace"})
                {
                    appearance.ApplyFace(id);
                    RecordFace(report,id,appearance.faceRenderer);
                    Capture(report,rig,new Vector3(1.3f,1.6f,1.7f),Vector3.up*1.05f,"face-"+id+"-body.png",52f);
                    Capture(report,rig,new Vector3(.55f,1.61f,.7f),new Vector3(0f,1.48f,0f),"face-"+id+"-neck.png",42f);
                }
                rig.working=true;rig.rightTarget=new Vector3(.24f,1.02f,-.4f);rig.leftTarget=new Vector3(-.13f,1.02f,-.4f);
                rig.rightRotation=Quaternion.Euler(0,180,0)*rig.right.handWorldRest;
                rig.leftRotation=Quaternion.Euler(0,90,0)*rig.left.handWorldRest;
                rig.ApplyWorkingPoseAtRoot(new Vector3(.12f,0,-.82f));
                Record(report,"prefab-grip-fixture",rig);
                Capture(report,rig,rig.right.palm.position+new Vector3(.16f,.14f,.24f),rig.right.palm.position,"right-fingers-fixture.png",42f);
                Capture(report,rig,rig.left.palm.position+new Vector3(-.16f,.14f,.24f),rig.left.palm.position,"support-fingers-fixture.png",42f);
            }
            finally{Object.DestroyImmediate(actor);}
        }

        private static void InspectCooking(string name,Report report)
        {
            EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/"+name+".unity",OpenSceneMode.Single);
            var cooking=Object.FindFirstObjectByType<Stage01CookingView>();
            if(cooking==null||cooking.body==null||cooking.butchery==null)throw new InvalidOperationException("Authored cooking view missing: "+name);
            var view=cooking.butchery;var timing=view.timing;
            Invoke(timing,"Awake");Invoke(view,"Awake");view.Bind(timing);Invoke(cooking,"Awake");
            for(int phase=0;phase<timing.semanticBeatmap.chart.phases.Length;phase++)
            {
                if((int)view.VisiblePhase!=phase)throw new InvalidOperationException("Synthetic inspection phase did not advance: "+name+"/"+phase);
                Invoke(view,"Update");
                // Preview the actual authored active tool rather than the demo
                // visibility, without starting playback or changing scene bytes.
                view.handRoot.gameObject.SetActive(true);
                Invoke(cooking,"LateUpdate");
                Record(report,name+"/"+phase, cooking.body);
                CaptureSide(report,cooking.body,name+"-phase-"+phase,view.knifeRoot);
                var gate=timing.semanticBeatmap.chart.phases[phase];
                if(phase==timing.semanticBeatmap.chart.phases.Length-1)break;
                for(int note=gate.firstNoteId;note<=gate.lastNoteId;note++)
                    Invoke(timing,"ResolveGameplayInput",timing.notePatternProvider.RuntimeNotes[note].songTimeSeconds);
            }
        }

        private static void InspectStageThreeRest(Report report)
        {
            EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/Stage03_Flounder.unity",OpenSceneMode.Single);
            var cooking=Object.FindFirstObjectByType<Stage03CookingView>();
            var controller=Object.FindFirstObjectByType<Stage03GameController>();
            if(cooking==null||controller==null)throw new InvalidOperationException("Stage3 authored view missing.");
            Invoke(cooking,"Awake");cooking.SetWorkPoint(controller.PreparationWorkPoint(0));Invoke(cooking,"LateUpdate");
            Record(report,"Stage03/rest-before-first-cut",cooking.body);
            CaptureSide(report,cooking.body,"Stage03-rest",cooking.knifeRoot);
        }

        private static void InspectInstruments(Report report)
        {
            EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/KevinHome.unity",OpenSceneMode.Single);
            foreach(var station in Object.FindObjectsByType<HomeEquipmentStation>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KevinEmbodimentAuthoring.PrefabPath));
                bool instrumentActive=station.equipmentVisual.activeSelf;
                try
                {
                    station.equipmentVisual.SetActive(true);
                    var rig=actor.GetComponent<KevinBodyRig>();
                    actor.transform.SetPositionAndRotation(station.playerPosition.position,station.playerPosition.rotation);
                    rig.working=true;rig.SetHeadHidden(false);Vector3 preferred=actor.transform.position;
                    rig.PrepareInstrumentPoseAtRoot(preferred,station.rightHandTarget.position+Vector3.up*.03f,
                        station.leftHandTarget.position+Vector3.up*.03f);
                    foreach(float stroke in new[]{0f,1f,-1f})
                    {
                        rig.rightTarget=station.rightHandTarget.position+Vector3.up*(.03f+Mathf.Max(0,stroke)*.08f);
                        rig.leftTarget=station.leftHandTarget.position+Vector3.up*(.03f+Mathf.Max(0,-stroke)*.07f);
                        rig.ApplyWorkingPoseAtRoot(preferred);Record(report,"instrument/"+station.equipmentId+"/"+stroke,rig);
                        string label="instrument-"+station.equipmentId+"-"+(stroke<0?"minus":stroke>0?"plus":"rest");
                        Capture(report,rig,station.practiceCamera.position,actor.transform.position+Vector3.up*1.05f,label+"-body.png",52f,station.equipmentVisual.transform);
                        Capture(report,rig,rig.right.palm.position+new Vector3(.18f,.13f,-.23f),rig.right.palm.position,label+"-grip.png",42f,station.equipmentVisual.transform);
                    }
                }
                finally{station.equipmentVisual.SetActive(instrumentActive);Object.DestroyImmediate(actor);}
            }
        }

        private static void Record(Report report,string context,KevinBodyRig rig)
        {
            report.poses.Add(new Pose{context=context,root=rig.transform.position,rightTarget=rig.rightTarget,leftTarget=rig.leftTarget,
                rightError=Vector3.Distance(rig.right.palm.position,rig.rightTarget),leftError=Vector3.Distance(rig.left.palm.position,rig.leftTarget),
                workingLeanDegrees=rig.workingLeanDegrees,rightShoulder=rig.right.upper.position,leftShoulder=rig.left.upper.position,
                rightUpper=Vector3.Distance(rig.right.upper.position,rig.right.lower.position),rightLower=Vector3.Distance(rig.right.lower.position,rig.right.hand.position),
                leftUpper=Vector3.Distance(rig.left.upper.position,rig.left.lower.position),leftLower=Vector3.Distance(rig.left.lower.position,rig.left.hand.position)});
        }
        private static void RecordFace(Report report,string id,SkinnedMeshRenderer renderer)
        {
            var baked=new Mesh();
            try
            {
                renderer.BakeMesh(baked);
                Vector3[] vertices=baked.vertices;
                if(vertices.Length==0)throw new InvalidOperationException("The selected face baked without vertices: "+id);
                var bounds=new Bounds(renderer.transform.TransformPoint(vertices[0]),Vector3.zero);
                bool finite=true;
                using var bytes=new MemoryStream();
                using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))
                    foreach(var vertex in vertices)
                    {
                        finite&=float.IsFinite(vertex.x)&&float.IsFinite(vertex.y)&&float.IsFinite(vertex.z);
                        bounds.Encapsulate(renderer.transform.TransformPoint(vertex));
                        writer.Write(vertex.x);writer.Write(vertex.y);writer.Write(vertex.z);
                    }
                using var sha=SHA256.Create();
                report.faces.Add(new FaceGeometry{id=id,meshPath=AssetDatabase.GetAssetPath(renderer.sharedMesh),vertices=vertices.Length,
                    bones=renderer.bones.Length,bindposes=renderer.sharedMesh.bindposes.Length,allBonesAssigned=renderer.bones.All(b=>b!=null),
                    finite=finite,worldMinimum=bounds.min,worldMaximum=bounds.max,
                    bakedVerticesSha256=BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant()});
                if(!finite)throw new InvalidOperationException("The selected face baked non-finite positions: "+id);
            }
            finally{Object.DestroyImmediate(baked);}
        }
        private static void CaptureSide(Report report,KevinBodyRig rig,string label,Transform extraGeometry=null)
        {
            var modes=rig.headRenderers.Select(r=>r.shadowCastingMode).ToArray();
            try
            {
                rig.SetHeadHidden(false);Vector3 origin=rig.transform.position;
                Capture(report,rig,origin+new Vector3(1.3f,1.6f,1.7f),origin+Vector3.up*1.05f,label+"-body.png",52f,extraGeometry);
                Capture(report,rig,rig.right.palm.position+new Vector3(.18f,.13f,.23f),rig.right.palm.position,label+"-grip.png",42f,extraGeometry);
            }
            finally{for(int i=0;i<modes.Length;i++)rig.headRenderers[i].shadowCastingMode=modes[i];}
        }
        private static void Invoke(object target,string method,params object[] arguments)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(target,arguments);
        private static void Capture(Report report,KevinBodyRig rig,Vector3 position,Vector3 focus,string file,float fov,Transform extraGeometry=null)
        {
            var preview=EditorSceneManager.NewPreviewScene();
            var camera=new GameObject("BodyDiagnosticCamera").AddComponent<Camera>();
            var light=new GameObject("BodyDiagnosticLight").AddComponent<Light>();
            var texture=new RenderTexture(1200,800,24);var pixels=new Texture2D(1200,800,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                SceneManager.MoveGameObjectToScene(camera.gameObject,preview);camera.scene=preview;
                SceneManager.MoveGameObjectToScene(light.gameObject,preview);
                var snapshot=CloneHierarchy(rig.transform,preview);
                // Apply the explicitly inspected choice after cloning. Its
                // nonserialized SelectedFaceId property defaults to CuteFace.
                var originalAppearance=rig.GetComponent<KevinAppearance>();
                if(originalAppearance!=null)snapshot.GetComponent<KevinAppearance>()?.ApplyFace(originalAppearance.SelectedFaceId);
                if(extraGeometry!=null)CloneHierarchy(extraGeometry,preview);
                camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(focus-position));camera.fieldOfView=fov;
                camera.nearClipPlane=.01f;camera.farClipPlane=20f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.12f,.14f);
                light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(35,-140,0);light.intensity=1.2f;
                camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
                pixels.ReadPixels(new Rect(0,0,1200,800),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(KevinRiggedBodyAuthoring.EvidenceDirectory,file),pixels.EncodeToPNG());
                RecordSurfaceTransforms(report,file,rig.transform,snapshot.transform);
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;
                Object.DestroyImmediate(texture);Object.DestroyImmediate(pixels);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
        private static GameObject CloneHierarchy(Transform original,Scene preview)
        {
            // Preserve the complete attached skeleton and its actual pose.
            // A new renderer InstanceID avoids same-frame skin cache reuse;
            // no BakeMesh scale convention is used to draw this evidence.
            var snapshot=Object.Instantiate(original.gameObject);
            SceneManager.MoveGameObjectToScene(snapshot,preview);
            snapshot.transform.SetPositionAndRotation(original.position,original.rotation);
            snapshot.transform.localScale=original.lossyScale;
            foreach(var behaviour in snapshot.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
            return snapshot;
        }
        private static void RecordSurfaceTransforms(Report report,string capture,Transform original,Transform snapshot)
        {
            var sources=original.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var copies=snapshot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if(sources.Length!=copies.Length)throw new InvalidOperationException("The diagnostic hierarchy lost a skin.");
            for(int i=0;i<sources.Length;i++)
            {
                var source=sources[i];var copy=copies[i];var baked=new Mesh();
                try
                {
                    source.BakeMesh(baked,false);
                    var points=baked.vertices;
                    if(points.Length==0)throw new InvalidOperationException("Diagnostic BakeMesh has no vertices: "+source.name);
                    var world=new Bounds(source.transform.TransformPoint(points[0]),Vector3.zero);
                    foreach(var point in points)world.Encapsulate(source.transform.TransformPoint(point));
                    report.surfaces.Add(new SurfaceTransform{capture=capture,renderer=source.name,
                        originalScale=source.transform.lossyScale,cloneScale=copy.transform.lossyScale,
                        originalMinimum=source.bounds.min,originalMaximum=source.bounds.max,
                        cloneMinimum=copy.bounds.min,cloneMaximum=copy.bounds.max,
                        bakedLocalMinimum=baked.bounds.min,bakedLocalMaximum=baked.bounds.max,
                        bakedTransformPointMinimum=world.min,bakedTransformPointMaximum=world.max});
                }
                finally{Object.DestroyImmediate(baked);}
            }
        }
    }
}
