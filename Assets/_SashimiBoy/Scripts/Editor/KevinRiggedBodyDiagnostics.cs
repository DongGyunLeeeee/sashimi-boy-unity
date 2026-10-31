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
        [Serializable] private sealed class Report
        {
            public string evidenceKind = "Authored-scene targets and actual CPU-baked posed skins rendered as isolated static previews; not physical input, runtime face switching, DSP, story/save or Owner approval.";
            public List<Pose> poses = new List<Pose>();
            public List<FaceGeometry> faces = new List<FaceGeometry>();
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
                    Capture(rig,new Vector3(1.3f,1.6f,1.7f),Vector3.up*1.05f,"face-"+id+"-body.png",52f);
                    Capture(rig,new Vector3(.55f,1.61f,.7f),new Vector3(0f,1.48f,0f),"face-"+id+"-neck.png",42f);
                }
                rig.working=true;rig.rightTarget=new Vector3(.24f,1.02f,-.4f);rig.leftTarget=new Vector3(-.13f,1.02f,-.4f);
                rig.rightRotation=Quaternion.Euler(0,180,0)*rig.right.handWorldRest;
                rig.leftRotation=Quaternion.Euler(0,90,0)*rig.left.handWorldRest;
                rig.ApplyWorkingPoseAtRoot(new Vector3(.12f,0,-.82f));
                Record(report,"prefab-grip-fixture",rig);
                Capture(rig,rig.right.palm.position+new Vector3(.16f,.14f,.24f),rig.right.palm.position,"right-fingers-fixture.png",42f);
                Capture(rig,rig.left.palm.position+new Vector3(-.16f,.14f,.24f),rig.left.palm.position,"support-fingers-fixture.png",42f);
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
                CaptureSide(cooking.body,name+"-phase-"+phase,view.knifeRoot);
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
            CaptureSide(cooking.body,"Stage03-rest",cooking.knifeRoot);
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
                        Capture(rig,station.practiceCamera.position,actor.transform.position+Vector3.up*1.05f,label+"-body.png",52f,station.equipmentVisual.transform);
                        Capture(rig,rig.right.palm.position+new Vector3(.18f,.13f,-.23f),rig.right.palm.position,label+"-grip.png",42f,station.equipmentVisual.transform);
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
        private static void CaptureSide(KevinBodyRig rig,string label,Transform extraGeometry=null)
        {
            var modes=rig.headRenderers.Select(r=>r.shadowCastingMode).ToArray();
            try
            {
                rig.SetHeadHidden(false);Vector3 origin=rig.transform.position;
                Capture(rig,origin+new Vector3(1.3f,1.6f,1.7f),origin+Vector3.up*1.05f,label+"-body.png",52f,extraGeometry);
                Capture(rig,rig.right.palm.position+new Vector3(.18f,.13f,.23f),rig.right.palm.position,label+"-grip.png",42f,extraGeometry);
            }
            finally{for(int i=0;i<modes.Length;i++)rig.headRenderers[i].shadowCastingMode=modes[i];}
        }
        private static void Invoke(object target,string method,params object[] arguments)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(target,arguments);
        private static void Capture(KevinBodyRig rig,Vector3 position,Vector3 focus,string file,float fov,Transform extraGeometry=null)
        {
            var preview=EditorSceneManager.NewPreviewScene();
            var bakedMeshes=new List<Mesh>();
            var camera=new GameObject("BodyDiagnosticCamera").AddComponent<Camera>();
            var light=new GameObject("BodyDiagnosticLight").AddComponent<Light>();
            var texture=new RenderTexture(1200,800,24);var pixels=new Texture2D(1200,800,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                SceneManager.MoveGameObjectToScene(camera.gameObject,preview);camera.scene=preview;
                SceneManager.MoveGameObjectToScene(light.gameObject,preview);
                CloneSurfaces(rig.transform,preview,bakedMeshes);
                if(extraGeometry!=null)CloneSurfaces(extraGeometry,preview,bakedMeshes);
                camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(focus-position));camera.fieldOfView=fov;
                camera.nearClipPlane=.01f;camera.farClipPlane=20f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.10f,.12f,.14f);
                light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(35,-140,0);light.intensity=1.2f;
                camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
                pixels.ReadPixels(new Rect(0,0,1200,800),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(KevinRiggedBodyAuthoring.EvidenceDirectory,file),pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;
                Object.DestroyImmediate(texture);Object.DestroyImmediate(pixels);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);
                EditorSceneManager.ClosePreviewScene(preview);
                foreach(var mesh in bakedMeshes)Object.DestroyImmediate(mesh);
            }
        }
        private static void CloneSurfaces(Transform root,Scene preview,List<Mesh> bakedMeshes)
        {
            foreach(var original in root.GetComponentsInChildren<Renderer>(true))
            {
                if(!original.enabled||!original.gameObject.activeInHierarchy)continue;
                Mesh mesh;
                if(original is SkinnedMeshRenderer skin)
                {
                    mesh=new Mesh{name=skin.name+"_ActualBakedPose"};skin.BakeMesh(mesh);bakedMeshes.Add(mesh);
                }
                else
                {
                    var filter=original.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;
                    mesh=filter.sharedMesh;
                }
                var snapshot=new GameObject(original.name+"_StaticDiagnostic",typeof(MeshFilter),typeof(MeshRenderer));
                SceneManager.MoveGameObjectToScene(snapshot,preview);
                snapshot.transform.SetPositionAndRotation(original.transform.position,original.transform.rotation);
                snapshot.transform.localScale=original.transform.lossyScale;
                snapshot.GetComponent<MeshFilter>().sharedMesh=mesh;
                snapshot.GetComponent<MeshRenderer>().sharedMaterials=original.sharedMaterials;
            }
        }
    }
}
