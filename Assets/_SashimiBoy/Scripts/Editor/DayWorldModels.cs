using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class DayWorldAssetAuthoring
    {
        public static void BuildModels()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            Directory.CreateDirectory(Output); Directory.CreateDirectory("Logs/DayWorld/AssetInspection");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BuildNpc("Misuk","misook","미숙",1,1.62f,false);
            BuildNpc("Cheolsu","cheolsiua-jae","철수",1,1.74f,true);
            BuildNpc("Seongho","seonghoe","성호",2,1.83f,false);
            BuildNpc("Minjae","minjae","민재",2,1.76f,false);
            BuildWrapper("FishShop",Source+"/Buildings/FishShop/Exterior/modern_storefront.fbx",4.3f);
            BuildWrapper("EquipmentShop",Source+"/Buildings/EquipmentShop/Exterior/concrete_music_shop.fbx",4.8f);
            BuildWrapper("Club",Source+"/Buildings/Club/Exterior/brick_doorway.fbx",4.6f);
            BuildWrapper("DrumKit","Assets/_SashimiBoy/Art/Source/Environment/EquipmentShop/Equipment/ElectronicDrumKit/Models/ElectronicDrumKit.fbx",1.05f);
            BuildWrapper("Sofa","Assets/_SashimiBoy/Art/Source/Environment/EquipmentShop/Furniture/WoodenSofa/Models/WoodenSofa.fbx",.8f);
            BuildHomeModels();
            BuildVenueModels();
            DayWorldStreetAuthoring.BuildDoorModels();
            AssetDatabase.SaveAssets();
        }
        private static void BuildNpc(string id,string file,string name,int day,float height,bool seated)
        {
            string path=Source+"/Characters/"+id+"/"+file+".fbx";
            var original=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var root=new GameObject("PF_DayWorld_"+id);
            try
            {
                BindMaterials(original,path,id);
                Bounds bounds=BoundsOf(original);
                float scale=height/bounds.size.y;
                original.transform.localScale*=scale;
                bounds=BoundsOf(original);
                original.transform.position-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                float cut=height*(id=="Seongho"?.80f:.82f);
                float seatDrop=seated?.40f:0f;
                var visual=new GameObject("OwnerCharacter"+ (id=="Seongho"?"AndMotorcycle":"")).transform;
                visual.SetParent(root.transform,false);
                var headPivot=new GameObject("HeadBone").transform; headPivot.SetParent(visual,false);
                headPivot.localPosition=new Vector3(0f,cut-seatDrop,0f);
                Renderer headRenderer=null;
                foreach(var filter in original.GetComponentsInChildren<MeshFilter>())
                {
                    Mesh mesh=filter.sharedMesh;
                    Vector3[] positions=mesh.vertices.Select(p=>filter.transform.TransformPoint(p)).ToArray();
                    Vector3[] normals=mesh.normals.Select(n=>filter.transform.TransformDirection(n)).ToArray();
                    Vector2[] uv=mesh.uv;
                    for(int side=0;side<2;side++)
                    {
                        var points=new List<Vector3>();var ns=new List<Vector3>();var tex=new List<Vector2>();
                        var submeshes=new List<int[]>();
                        for(int sub=0;sub<mesh.subMeshCount;sub++)
                        {
                            var indices=new List<int>();int[] triangles=mesh.GetTriangles(sub);
                            for(int i=0;i<triangles.Length;i+=3)
                            {
                                var input=new List<HeadVertex>();
                                for(int j=0;j<3;j++) { int k=triangles[i+j];input.Add(new HeadVertex{p=positions[k],n=normals[k],uv=uv[k]}); }
                                var clipped=new List<HeadVertex>();
                                for(int j=0;j<3;j++)
                                {
                                    HeadVertex a=input[j],b=input[(j+1)%3];bool ai=side==1?a.p.y>=cut:a.p.y<=cut,bi=side==1?b.p.y>=cut:b.p.y<=cut;
                                    if(ai)clipped.Add(a);
                                    if(ai!=bi)clipped.Add(HeadVertex.Lerp(a,b,(cut-a.p.y)/(b.p.y-a.p.y)));
                                }
                                for(int j=1;j+1<clipped.Count;j++) foreach(var v in new[]{clipped[0],clipped[j],clipped[j+1]})
                                {
                                    Vector3 p=v.p;
                                    if(seated)
                                    {
                                        float hip=height*.53f,knee=height*.285f;
                                        if(p.y<hip && !(Mathf.Abs(p.x)>.20f && p.y>height*.36f))
                                        {
                                            if(p.y>=knee){p.z+=hip-p.y;p.y=hip-seatDrop;}
                                            else{p.z+=hip-knee;p.y+=hip-knee-seatDrop;}
                                        }
                                        else p.y-=seatDrop;
                                    }
                                    indices.Add(points.Count);points.Add(p);ns.Add(v.n);tex.Add(v.uv);
                                }
                            }
                            submeshes.Add(indices.ToArray());
                        }
                        var generated=new Mesh{name=id+(side==1?"_Head":"_Body"),indexFormat=IndexFormat.UInt32};
                        generated.SetVertices(points);generated.SetNormals(ns);generated.SetUVs(0,tex);generated.subMeshCount=submeshes.Count;
                        for(int sub=0;sub<submeshes.Count;sub++)generated.SetTriangles(submeshes[sub],sub);
                        if(side==1)
                        {
                            // The cut boundary stays on the body while the upper neck follows HeadBone.
                            // Two-bone skinning avoids an open seam when the provided head turns.
                            generated.bindposes=new[]{Matrix4x4.identity,headPivot.worldToLocalMatrix*visual.localToWorldMatrix};
                            generated.boneWeights=points.Select(p=>
                            {
                                float weight=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(cut-seatDrop,cut-seatDrop+.12f,p.y));
                                return new BoneWeight{boneIndex0=0,boneIndex1=1,weight0=1f-weight,weight1=weight};
                            }).ToArray();
                        }
                        if(seated)generated.RecalculateNormals();generated.RecalculateBounds();
                        string meshPath=Output+"/"+generated.name+".asset";
                        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                        if(saved==null){AssetDatabase.CreateAsset(generated,meshPath);saved=generated;}
                        else{EditorUtility.CopySerialized(generated,saved);Object.DestroyImmediate(generated);EditorUtility.SetDirty(saved);}
                        var go=new GameObject(side==1?"OwnerFace":"OwnerBody");
                        go.transform.SetParent(visual,false);
                        Renderer renderer;
                        if(side==1)
                        {
                            var skin=go.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=saved;skin.bones=new[]{visual,headPivot};skin.rootBone=visual;
                            skin.localBounds=saved.bounds;skin.updateWhenOffscreen=true;headRenderer=renderer=skin;
                        }
                        else {go.AddComponent<MeshFilter>().sharedMesh=saved;renderer=go.AddComponent<MeshRenderer>();}
                        renderer.sharedMaterials=filter.GetComponent<Renderer>().sharedMaterials;
                    }
                }
                original.SetActive(false);
                var npc=root.AddComponent<DayWorldNpc>();npc.npcId=id.ToLowerInvariant();npc.displayName=name;npc.day=day;
                npc.faceAnchor=new GameObject("FaceAnchor").transform;npc.faceAnchor.SetParent(headPivot,false);
                npc.faceAnchor.position=headRenderer.bounds.center+Vector3.forward*headRenderer.bounds.extents.z*.75f;
                if(id=="Seongho")npc.motorcycle=visual;
                root.AddComponent<DayWorldCharacterPose>().head=headPivot;
                var collider=root.AddComponent<BoxCollider>();collider.center=new Vector3(0f,(height-seatDrop)*.5f,0f);collider.size=new Vector3(.65f,height-seatDrop,id=="Seongho"?1.9f:.55f);
                PrefabUtility.SaveAsPrefabAsset(root,Output+"/PF_"+id+".prefab");
                RenderInspection(root,"Logs/DayWorld/AssetInspection/"+id+"-assembled.png");
            }
            finally {Object.DestroyImmediate(original);Object.DestroyImmediate(root);}
        }
        private struct HeadVertex
        {
            public Vector3 p,n;public Vector2 uv;
            public static HeadVertex Lerp(HeadVertex a,HeadVertex b,float t)=>new HeadVertex{p=Vector3.Lerp(a.p,b.p,t),n=Vector3.Lerp(a.n,b.n,t).normalized,uv=Vector2.Lerp(a.uv,b.uv,t)};
        }
        private static void BuildWrapper(string id,string path,float height)
        {
            var root=new GameObject("PF_DayWorld_"+id);
            try
            {
                var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),root.transform);
                BindMaterials(model,path,id);
                Bounds b=BoundsOf(model);model.transform.localScale*=height/b.size.y;b=BoundsOf(model);
                model.transform.position-=new Vector3(b.center.x,b.min.y,b.center.z);
                PrefabUtility.SaveAsPrefabAsset(root,Output+"/PF_"+id+".prefab");
                RenderInspection(root,"Logs/DayWorld/AssetInspection/"+id+"-assembled.png");
            }
            finally {Object.DestroyImmediate(root);}
        }
        public static Bounds BoundsOf(GameObject go)
        {
            var renderers=go.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0)throw new InvalidOperationException("No renderer: "+go.name);
            Bounds b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);return b;
        }
        private static void BindMaterials(GameObject model,string path,string id)
        {
            string folder=Path.GetDirectoryName(path).Replace('\\','/');if(folder.EndsWith("/Models"))folder=Path.GetDirectoryName(folder).Replace('\\','/');
            var files=Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Select(p=>p.Replace('\\','/')).Where(p=>!p.EndsWith(".meta")).ToArray();
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(source=>
                {
                    var part=Regex.Match(source.name,@"part_(\d+)");
                    string TexturePath(string kind)=>files.SingleOrDefault(p=>Regex.IsMatch(Path.GetFileNameWithoutExtension(p),part.Success?@"(?:^|_)part_"+part.Groups[1].Value+"_"+kind+"$":"_"+kind+"$",RegexOptions.IgnoreCase));
                    string key=id+(part.Success?"_part_"+part.Groups[1].Value:"");
                    string matPath=Output+"/MAT_"+key+".mat";
                    var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if(material==null){material=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(material,matPath);}
                    string basePath=TexturePath("basecolor");
                    if(basePath==null)throw new InvalidOperationException("Missing exact basecolor for "+id+"/"+source.name);
                    material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);material.color=Color.white;
                    string normal=TexturePath("normal");
                    if(normal!=null){material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normal));material.SetFloat("_BumpScale",.65f);material.EnableKeyword("_NORMALMAP");}
                    string metal=TexturePath("metallic"),rough=TexturePath("roughness");
                    if(metal!=null && rough!=null)
                    {
                        string mapPath=Output+"/MS_"+key+".png";
                        var m=ReadTexture(AssetDatabase.LoadAssetAtPath<Texture2D>(metal));var r=ReadTexture(AssetDatabase.LoadAssetAtPath<Texture2D>(rough));
                        var pixels=m.GetPixels();var roughness=r.GetPixels();
                        for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(pixels[i].r,0f,0f,1f-roughness[i].r);
                        m.SetPixels(pixels);m.Apply();File.WriteAllBytes(mapPath,m.EncodeToPNG());Object.DestroyImmediate(m);Object.DestroyImmediate(r);
                        AssetDatabase.ImportAsset(mapPath,ImportAssetOptions.ForceSynchronousImport);
                        var importer=(TextureImporter)AssetImporter.GetAtPath(mapPath);importer.sRGBTexture=false;importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.SaveAndReimport();
                        material.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath));material.EnableKeyword("_METALLICGLOSSMAP");material.SetFloat("_GlossMapScale",path.Contains("/Characters/")?.45f:.7f);
                    }
                    else{material.SetFloat("_Metallic",0f);material.SetFloat("_Glossiness",.3f);}
                    EditorUtility.SetDirty(material);return material;
                }).ToArray();
            }
        }
        private static Texture2D ReadTexture(Texture texture)
        {
            var target=RenderTexture.GetTemporary(256,256,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var old=RenderTexture.active;Graphics.Blit(texture,target);RenderTexture.active=target;
            var result=new Texture2D(256,256,TextureFormat.RGBA32,false,true);result.ReadPixels(new Rect(0,0,256,256),0,0);result.Apply();
            RenderTexture.active=old;RenderTexture.ReleaseTemporary(target);return result;
        }
    }
}
