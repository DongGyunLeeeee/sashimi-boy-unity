using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
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
            public Vector3 position,forward,fishMinimum,fishMaximum,viewportMinimum,viewportMaximum,rightPalm,leftPalm;
            public bool framingSatisfied;
        }
        [Serializable] private sealed class LegacyCameraReport
        {
            public string evidenceKind="Editor-authored stable phase/zero remaining tool action and synthetic note events; actual Game camera and fresh posed body. Not runtime input/music/save or Owner approval.";
            public List<LegacyCameraPose> poses=new List<LegacyCameraPose>();
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
                        report.poses.Add(new LegacyCameraPose{scene=scene,phase=phase,aspect=aspect,position=camera.transform.position,
                            forward=camera.transform.forward,fieldOfView=camera.fieldOfView,fishMinimum=fish.min,fishMaximum=fish.max,
                            viewportMinimum=minimum,viewportMaximum=maximum,rightPalm=camera.WorldToViewportPoint(cooking.body.right.palm.position),
                            leftPalm=camera.WorldToViewportPoint(cooking.body.left.palm.position),framingSatisfied=cooking.FramingSatisfied});
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
