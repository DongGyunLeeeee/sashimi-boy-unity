using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public sealed class DayWorldImportSettings : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(DayWorldAssetAuthoring.Source + "/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(DayWorldAssetAuthoring.Source + "/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            if (Path.GetFileNameWithoutExtension(assetPath).EndsWith("_normal", StringComparison.OrdinalIgnoreCase))
                importer.textureType = TextureImporterType.NormalMap;
            else if (assetPath.IndexOf("_roughness", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     assetPath.IndexOf("_metallic", StringComparison.OrdinalIgnoreCase) >= 0)
            { importer.sRGBTexture = false; importer.isReadable = true; }
        }
    }

    public static partial class DayWorldAssetAuthoring
    {
        public const string Source = "Assets/_SashimiBoy/Art/Source/DayWorld";
        public const string Output = "Assets/_SashimiBoy/Art/Generated/DayWorld";
        public static void InspectBatch()
        {
            Directory.CreateDirectory("Logs/DayWorld/AssetInspection");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (string path in Directory.GetFiles(Source, "*.fbx", SearchOption.AllDirectories))
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                try
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                        Debug.Log("[DayWorldMesh] " + path + " " + renderer.name + " bounds=" + renderer.bounds +
                                  " materials=" + string.Join(",", renderer.sharedMaterials.Select(m=>m == null ? "NULL" : m.name)));
                    Debug.Log("[DayWorldBones] " + path + " skins=" + root.GetComponentsInChildren<SkinnedMeshRenderer>().Length);
                    if (path.Contains("Characters"))
                    {
                        RenderInspection(root, "Logs/DayWorld/AssetInspection/" + root.name.Replace("(Clone)", "") + "-raw.png");
                        root.transform.rotation = Quaternion.Euler(90f,180f,0f);
                        RenderInspection(root, "Logs/DayWorld/AssetInspection/" + root.name.Replace("(Clone)", "") + "-upright.png");
                    }
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[DayWorld] Asset inspection complete.");
        }

        private static void RenderInspection(GameObject root, string path)
        {
            // Immediate Editor Camera.Render does not wait for GPU skinning. Bake temporary
            // preview meshes so this source-inspection image also contains the attached heads.
            var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>();
            var previews=new GameObject[skins.Length];var meshes=new Mesh[skins.Length];
            var enabled=skins.Select(s=>s.enabled).ToArray();
            try
            {
                for(int i=0;i<skins.Length;i++)
                {
                    meshes[i]=new Mesh();skins[i].BakeMesh(meshes[i]);
                    previews[i]=new GameObject("TemporarySkinPreview",typeof(MeshFilter),typeof(MeshRenderer));
                    previews[i].transform.SetParent(skins[i].transform,false);
                    previews[i].GetComponent<MeshFilter>().sharedMesh=meshes[i];
                    previews[i].GetComponent<Renderer>().sharedMaterials=skins[i].sharedMaterials;
                    previews[i].GetComponent<Renderer>().enabled=enabled[i];skins[i].enabled=false;
                }
                typeof(KevinEmbodimentAuthoring).GetMethod("RenderModel", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { root, path });
            }
            finally
            {
                for(int i=0;i<skins.Length;i++){skins[i].enabled=enabled[i];if(previews[i]!=null)Object.DestroyImmediate(previews[i]);if(meshes[i]!=null)Object.DestroyImmediate(meshes[i]);}
            }
        }
    }
}
