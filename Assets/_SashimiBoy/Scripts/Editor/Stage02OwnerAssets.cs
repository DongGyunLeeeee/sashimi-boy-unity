using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class Stage02RockfishAuthoring
    {
        public const string OwnerSource="Assets/_SashimiBoy/Art/Source/DayWorld/Fish/Stage02";
        private static readonly string[] OwnerParts={"head","body","bone","fillet","fillet_half","piece"};

        public static void ApplyOwnerRevisionBatch()
        {
            DayWorldInteriorAuthoring.ApplyBatch();
            OwnerJudgementAuthoring.ApplyBatch();
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            var view=Find<Stage01ButcheryPresenter>(scene);
            BuildModels(view.plateSlots.Length);
            ApplyFish(view);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save owner Rockfish assembly.");
            AssetDatabase.SaveAssets();
            Debug.Log("[OwnerRevision] Centered existing interior doors and applied four PNG judgements plus all six supplied Rockfish models. Existing music and chart authoring were not invoked.");
        }

        private static Material OwnerMaterial(string part)
        {
            var material=Material("Owner_"+part,"Standard",Color.white);
            material.shader=Shader.Find(part=="fillet_half" ? "SashimiBoy/Stage01FixedFilletSurface" : "Standard");
            string path=OwnerSource+"/rockfish_"+part+"/rockfish_"+part;
            material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(path+"_basecolor.JPEG");
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(path+"_normal.JPEG"));
            material.EnableKeyword("_NORMALMAP");material.SetFloat("_BumpScale",.4f);
            material.SetFloat("_Metallic",0f);material.SetFloat("_Glossiness",.22f);
            if(part=="fillet_half") material.SetVector("_CutPlane",new Vector4(0,0,0,1));
            if(material.mainTexture==null)throw new InvalidOperationException("Missing Owner texture: "+path);
            EditorUtility.SetDirty(material);return material;
        }

        private static void BuildModels(int sliceCount)
        {
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // Each supplied scan has its own axes and units. Bake positive scales into derived meshes only.
            // The scan includes a rounded neck stub; overlap it with the body to close the initial seam.
            var head = OwnerMesh("head", Quaternion.Euler(-90,90,0), new Vector3(.34f,.20f,.32f), new Vector3(.325f,0,0));
            var body = OwnerMesh("body", Quaternion.identity, new Vector3(.70f,.20f,.32f), new Vector3(-.14f,0,0));
            var fillet = OwnerMesh("fillet", Quaternion.Euler(180,0,0), new Vector3(.70f,.085f,.30f), new Vector3(-.14f,0,0));
            var spine = OwnerMesh("bone", Quaternion.Euler(90,0,0)*Quaternion.Euler(0,0,35)*Quaternion.Euler(0,90,0),
                new Vector3(.66f,.035f,.19f), new Vector3(-.14f,.082f,0));
            var half = OwnerMesh("fillet_half", Quaternion.Euler(90,270,0), new Vector3(.70f,.060f,.15f), new Vector3(-.14f,0,0));
            var sliceMesh = OwnerMesh("piece", Quaternion.Euler(0,90,0), new Vector3(.064f,.018f,.14f), Vector3.zero);
            var skin = OwnerMaterial("body");
            var pinMaterial = Material("Bone", "Standard", new Color(.91f,.89f,.79f));
            var root = new GameObject("PF_Stage02_RockfishAssembly");
            try
            {
                var assembly = root.AddComponent<SalmonAssemblyView>();
                assembly.head = Piece(root.transform,"Head",SalmonAssemblyPieceRole.Head,head,OwnerMaterial("head"),true);
                assembly.body = Piece(root.transform,"Body",SalmonAssemblyPieceRole.Body,body,skin,true);
                assembly.fillet = Piece(root.transform,"Fillet",SalmonAssemblyPieceRole.Fillet,fillet,OwnerMaterial("fillet"),false);
                assembly.spine = Piece(root.transform,"Spine",SalmonAssemblyPieceRole.Spine,spine,OwnerMaterial("bone"),false);
                var fins = new GameObject("Fins"); fins.transform.SetParent(root.transform,false);
                foreach (int side in new[] {-1,1})
                    MeshObject(fins.transform,"OwnerBodyFin_"+side,Crop(body,
                        new Vector3(-.46f,-.01f,side<0?-.17f:.125f),new Vector3(.16f,.23f,side<0?-.125f:.17f),"OwnerFin_"+side),skin);
                assembly.fins = fins.AddComponent<SalmonAssemblyPieceView>();
                assembly.fins.Configure("Fins",SalmonAssemblyPieceRole.Fins,fins,false);
                assembly.headAnchor=Anchor(root.transform,"HeadAnchor",Vector3.zero);
                assembly.bodyAnchor=Anchor(root.transform,"BodyAnchor",Vector3.zero);
                assembly.finsAnchor=Anchor(root.transform,"FinsAnchor",Vector3.zero);
                assembly.spineAnchor=Anchor(root.transform,"SpineAnchor",Vector3.zero);
                assembly.filletAnchor=Anchor(root.transform,"FilletAnchor",Vector3.zero);
                var pins=new List<SalmonAssemblyPieceView>(); var anchors=new List<Transform>();
                for (int i=0;i<8;i++)
                {
                    var pin=Primitive(root.transform,"PinBone_"+i,PrimitiveType.Capsule,new Vector3(Mathf.Lerp(-.43f,.13f,i/7f),.071f,-.08f),new Vector3(.006f,.025f,.006f),pinMaterial);
                    pin.transform.localRotation=Quaternion.Euler(22f,0,25f);
                    var piece=pin.AddComponent<SalmonAssemblyPieceView>();piece.Configure("PinBone_"+i,SalmonAssemblyPieceRole.PinBone,pin,false);
                    pins.Add(piece);anchors.Add(Anchor(root.transform,"PinBoneAnchor_"+i,pin.transform.localPosition+Vector3.up*.02f));
                }
                assembly.pinBones=pins.ToArray();assembly.pinBoneAnchors=anchors.ToArray();
                assembly.knifeAttachmentAnchor=Anchor(root.transform,"KnifeAnchor",new Vector3(.21f,.16f,0));
                assembly.handAttachmentAnchor=Anchor(root.transform,"SupportAnchor",new Vector3(.05f,.12f,-.05f));
                assembly.pinBoneWorkAnchor=anchors[0];
                assembly.sashimiOutputAnchor=Anchor(root.transform,"SliceAnchor",new Vector3(.15f,.06f,-.08f));
                assembly.plateOutputAnchor=Anchor(root.transform,"PlateAnchor",new Vector3(-.55f,.02f,.35f));
                PrefabUtility.SaveAsPrefabAsset(root,Root+"/PF_Stage02_RockfishAssembly.prefab");
            }
            finally { Object.DestroyImmediate(root); }
            BuildOwnerHalf(half,sliceCount);
            var slice = new GameObject("PF_RockfishSashimiSlice");
            try
            {
                MeshObject(slice.transform,"OwnerRockfishPiece",sliceMesh,OwnerMaterial("piece"));
                PrefabUtility.SaveAsPrefabAsset(slice,Root+"/PF_RockfishSashimiSlice.prefab");
            }
            finally { Object.DestroyImmediate(slice); }
            AssetDatabase.SaveAssets();
        }

        private static Mesh OwnerMesh(string part, Quaternion rotation, Vector3 size, Vector3 centerAtFloor)
        {
            string path=OwnerSource+"/rockfish_"+part+"/rockfish_"+part+".fbx";
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(asset==null) throw new InvalidOperationException("Missing owner model: "+path);
            var source=Object.Instantiate(asset);
            var meshes=new List<Mesh>();
            try
            {
                source.transform.rotation=rotation;
                Bounds bounds=DayWorldAssetAuthoring.HomeGeometryBounds(source);
                // World-axis calibration avoids applying differently oriented FBX local scales to the wrong axes.
                var calibration=Matrix4x4.TRS(centerAtFloor,Quaternion.identity,
                    new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z))*
                    Matrix4x4.Translate(-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z));
                foreach(var filter in source.GetComponentsInChildren<MeshFilter>())
                    meshes.Add(Cluster(filter.sharedMesh,calibration*filter.transform.localToWorldMatrix));
                var result=new Mesh { name="Owner_"+part,indexFormat=IndexFormat.UInt32 };
                result.CombineMeshes(meshes.Select(m=>new CombineInstance{mesh=m,transform=Matrix4x4.identity}).ToArray(),true,false);
                result.RecalculateBounds();result.RecalculateTangents();
                Debug.Log("[OwnerRockfishDerived] "+part+" vertices="+result.vertexCount+" bounds="+result.bounds);
                return SaveMesh(result,"Owner_"+part);
            }
            finally
            {
                foreach(var mesh in meshes) Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(source);
            }
        }

        private static void BuildOwnerHalf(Mesh mesh,int count)
        {
            var root=new GameObject("PF_RockfishFilletHalf");
            try
            {
                var surface=root.AddComponent<Stage01FilletSurface>();
                var volume=MeshObject(root.transform,"OwnerRockfishFilletHalf",mesh,OwnerMaterial("fillet_half"));
                surface.sourceRenderers=volume.GetComponents<Renderer>();surface.workHeight=.058f;
                surface.cutPositions=Enumerable.Range(0,count).Select(i=>Right-(i+1)*(.66f/count)).ToArray();
                surface.cutCaps=new GameObject[count];
                var section=Material("Section","Standard",Color.white);
                section.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(FleshPath);EditorUtility.SetDirty(section);
                for(int i=0;i<count;i++)
                {
                    var cap=MeshObject(root.transform,"CutFace_"+i,OwnerCutFace(mesh,surface.cutPositions[i],i),section);
                    surface.cutCaps[i]=cap;cap.SetActive(false);
                }
                PrefabUtility.SaveAsPrefabAsset(root,Root+"/PF_RockfishFilletHalf.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Mesh OwnerCutFace(Mesh source,float x,int index)
        {
            var vertices=source.vertices;var triangles=source.triangles;var points=new List<Vector2>();
            for(int i=0;i<triangles.Length;i+=3) for(int edge=0;edge<3;edge++)
            {
                Vector3 a=vertices[triangles[i+edge]],b=vertices[triangles[i+(edge+1)%3]];
                if ((a.x<x)==(b.x<x) || Mathf.Abs(a.x-b.x)<.000001f) continue;
                var point=Vector3.Lerp(a,b,(x-a.x)/(b.x-a.x));points.Add(new Vector2(point.z,point.y));
            }
            points=points.Distinct().OrderBy(p=>p.x).ThenBy(p=>p.y).ToList();
            if(points.Count<3) throw new InvalidOperationException("Owner half has no cut section at "+x);
            var hull=new List<Vector2>();
            foreach(var point in points)
            { while(hull.Count>=2 && Cross(hull[hull.Count-2],hull[hull.Count-1],point)<=0) hull.RemoveAt(hull.Count-1);hull.Add(point); }
            int lower=hull.Count;
            for(int i=points.Count-2;i>=0;i--)
            { while(hull.Count>lower && Cross(hull[hull.Count-2],hull[hull.Count-1],points[i])<=0) hull.RemoveAt(hull.Count-1);hull.Add(points[i]); }
            hull.RemoveAt(hull.Count-1);
            var center=hull.Aggregate(Vector2.zero,(sum,p)=>sum+p)/hull.Count;
            float minZ=hull.Min(p=>p.x),maxZ=hull.Max(p=>p.x),minY=hull.Min(p=>p.y),maxY=hull.Max(p=>p.y);
            var positions=new List<Vector3>{new Vector3(x,center.y,center.x)};
            positions.AddRange(hull.Select(p=>new Vector3(x,p.y,p.x)));
            var uv=positions.Select(p=>new Vector2(Mathf.InverseLerp(minZ,maxZ,p.z),Mathf.InverseLerp(minY,maxY,p.y))).ToArray();
            var tri=new List<int>();
            for(int i=0;i<hull.Count;i++)tri.AddRange(new[]{0,1+(i+1)%hull.Count,1+i});
            var mesh=new Mesh { name="OwnerHalfCut_"+index };mesh.SetVertices(positions);mesh.uv=uv;mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
            return SaveMesh(mesh,mesh.name);
        }
        private static float Cross(Vector2 a,Vector2 b,Vector2 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);

        public static void InspectOwnerSourcesBatch()
        {
            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("InspectCamera",typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.16f,.18f);camera.fieldOfView=40;
            var light=new GameObject("InspectLight",typeof(Light)).GetComponent<Light>();
            light.type=LightType.Directional;light.intensity=1.25f;light.transform.rotation=Quaternion.Euler(45,-30,0);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
            foreach(string part in OwnerParts)
            {
                var source=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(OwnerSource+"/rockfish_"+part+"/rockfish_"+part+".fbx"));
                foreach(var renderer in source.GetComponentsInChildren<Renderer>())renderer.sharedMaterial=OwnerMaterial(part);
                foreach(int roll in new[]{0,90})
                {
                    source.transform.rotation=Quaternion.Euler(roll,90,0);
                    var bounds=DayWorldAssetAuthoring.HomeGeometryBounds(source);
                    Debug.Log("[OwnerRockfish] "+part+" roll="+roll+" bounds="+bounds+" vertices="+source.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.vertexCount));
                    camera.transform.position=bounds.center+new Vector3(.15f,1.0f,-1.2f)*bounds.size.magnitude;
                    camera.transform.LookAt(bounds.center);
                    CaptureCamera(camera,"Logs/DayWorld/OwnerAssetDoorWakeRevision/owner-"+part+"-roll"+roll+".png");
                }
                Object.DestroyImmediate(source);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
