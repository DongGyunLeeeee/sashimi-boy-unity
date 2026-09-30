using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class Stage02RockfishAuthoring
    {
        private const float Left=-.49f, Right=.21f;

        // Weld a project-owned copy on a 4mm grid, keeping UV islands separate. Never changes the supplied FBX/importer.
        private static Mesh Cluster(Mesh source,Matrix4x4 matrix)
        {
            var v=source.vertices;var ns=source.normals;var tex=source.uv;var map=new int[v.Length];
            var keys=new Dictionary<(int,int,int,int,int),int>();var p=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var counts=new List<int>();
            var geometry=new Dictionary<(int,int,int),(Vector3 sum,int count)>();
            var geometryKeys=new List<(int,int,int)>();
            var normalMatrix=matrix.inverse.transpose;
            for(int i=0;i<v.Length;i++)
            {
                Vector3 point=matrix.MultiplyPoint3x4(v[i]);Vector2 t=tex[i];
                var spatial=(Mathf.RoundToInt(point.x/.004f),Mathf.RoundToInt(point.y/.004f),Mathf.RoundToInt(point.z/.004f));
                geometry.TryGetValue(spatial,out var g);geometry[spatial]=(g.sum+point,g.count+1);
                var key=(spatial.Item1,spatial.Item2,spatial.Item3,Mathf.FloorToInt(t.x*64),Mathf.FloorToInt(t.y*64));
                if(!keys.TryGetValue(key,out int k)){k=p.Count;keys.Add(key,k);p.Add(Vector3.zero);n.Add(Vector3.zero);uv.Add(Vector2.zero);counts.Add(0);geometryKeys.Add(spatial);}
                map[i]=k;p[k]+=point;n[k]+=normalMatrix.MultiplyVector(ns[i]);uv[k]+=t;counts[k]++;
            }
            for(int i=0;i<p.Count;i++){var g=geometry[geometryKeys[i]];p[i]=g.sum/g.count;n[i]=n[i].normalized;uv[i]/=counts[i];}
            var mesh=new Mesh{name="RockfishNormalized",indexFormat=IndexFormat.UInt32};mesh.SetVertices(p);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.subMeshCount=source.subMeshCount;
            for(int sub=0;sub<source.subMeshCount;sub++)
            {
                var tris=source.GetTriangles(sub);var result=new List<int>();
                var seen=new HashSet<(int,int,int)>();
                for(int i=0;i<tris.Length;i+=3)
                {
                    int a=map[tris[i]],b=map[tris[i+1]],c=map[tris[i+2]];
                    if(a==b||b==c||c==a||!seen.Add((a,b,c)))continue;result.AddRange(new[]{a,b,c});
                }
                mesh.SetTriangles(result,sub);
            }
            mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
        }

        private static Mesh Crop(Mesh source,Vector3 min,Vector3 max,string name)
        {var bounds=new Bounds();bounds.SetMinMax(min,max);return SaveMesh(GeneratedMeshCrop.Crop(source,Matrix4x4.identity,bounds,name),name);}
        private static Mesh SaveMesh(Mesh mesh,string name)
        {
            string path=Root+"/MS_Rockfish_"+name+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
            EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);return saved;
        }
        private static Material Material(string name,string shader,Color color)
        {
            string path=Root+"/MAT_Rockfish_"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}
            m.color=color;if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",.22f);EditorUtility.SetDirty(m);return m;
        }
        private static GameObject MeshObject(Transform parent,string name,Mesh mesh,params Material[] materials)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
            go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterials=materials;return go;
        }
        private static SalmonAssemblyPieceView Piece(Transform root,string name,SalmonAssemblyPieceRole role,Mesh mesh,Material material,bool visible)
        {
            var go=MeshObject(root,name,mesh,material);var p=go.AddComponent<SalmonAssemblyPieceView>();p.Configure(name,role,go,visible);return p;
        }
        private static GameObject Primitive(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=material;return go;
        }
        private static Transform Anchor(Transform parent,string name,Vector3 position)
        {var t=new GameObject(name).transform;t.SetParent(parent,false);t.localPosition=position;return t;}
    }
}
