using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static class KevinEmbodimentAuthoring
    {
        public const string BodyPath = KevinRiggedBodyAuthoring.BodyPath;
        public const string FaceVariantId = "CuteFace";
        public const string FacePath = "Assets/_SashimiBoy/Art/Generated/Prefabs/Characters/PF_Character_Kevin_CuteFace.prefab";
        public const string Output = "Assets/_SashimiBoy/Art/Generated/Stage01Playable/Kevin";
        public const string PrefabPath = Output + "/PF_Kevin_Complete.prefab";

        public static void BuildBody()
        {
            KevinRiggedBodyAuthoring.BuildBodyPrefab();
            ReapplyCatalog();
            KevinCustomizationAuthoring.BuildFaces();
            Directory.CreateDirectory(KevinRiggedBodyAuthoring.EvidenceDirectory);
            var preview = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try { RenderModel(preview, Path.Combine(KevinRiggedBodyAuthoring.EvidenceDirectory, "Kevin-combined.png")); }
            finally { Object.DestroyImmediate(preview); }
        }
        public static void ReapplyCatalog()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var catalog = AssetDatabase.LoadAssetAtPath<KevinVariantCatalog>("Assets/_SashimiBoy/Art/Generated/Data/KevinVariantCatalog.asset");
            if (prefab == null || catalog == null) return;
            // Existing Scenes explicitly serialize the former provisional default. Keep its
            // reference as a compatibility alias so no Scene or saved world progress is rebuilt.
            foreach (string id in new[] { FaceVariantId, "AmbiguousFace" })
            {
                var entry = catalog.Find(id);
                if (entry == null) throw new InvalidOperationException("Existing Kevin catalog entry is required: " + id);
                entry.prefab = prefab; entry.avatar = prefab.GetComponentInChildren<Animator>().avatar;
                entry.heightMeters = 1.78f; entry.defaultScale = 1f; entry.isHumanoid = true;
                entry.hasAnimationClips = false; entry.idleClip = entry.walkClip = null;
                entry.notes = (id == FaceVariantId ? "" : "Legacy Scene default alias of CuteFace. ") +
                    "Owner CuteFace with a continuous skinned neck and the supplied RiggedBody; connected arm/finger cooking IK is retained.";
            }
            catalog.provisionalDefaultVariantId = FaceVariantId;
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
        }

        private struct Vertex
        {
            public Vector3 p, n; public Vector2 uv;
            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex { p = Vector3.Lerp(a.p,b.p,t), n = Vector3.Lerp(a.n,b.n,t).normalized, uv = Vector2.Lerp(a.uv,b.uv,t) };
        }

        internal static GameObject BuildOwnerHead(Transform head, Transform neck, Transform chest, string variantId = FaceVariantId)
        {
            // Cut across bare neck, above the source shirt/shoulders. Continue each cut edge
            // down inside the body collar, with neck/chest weights, instead of leaving an
            // open static head or discarding side triangles. Source vertices/UVs stay intact.
            const float referenceCut = 1.49f;
            const float neckCut = referenceCut, bridgeStep = .04f;
            var source = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FacePath.Replace("CuteFace", variantId)));
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
            var weights = new List<BoneWeight>();
            var cutPoints = new List<Vector3>();
            var unique = new Dictionary<(Vector3, Vector3, Vector2), int>();
            Material original = null;
            void Add(Vertex value, float extension = 0f)
            {
                Vector3 p = (value.p - Vector3.up * referenceCut) * 1.09f;
                if (variantId == FaceVariantId) p.y -= extension;
                else if (extension > 0f)
                    p = Vector3.Lerp(p, new Vector3(p.x * .35f, -.08f, p.z * .25f), extension / .08f);
                float upper = Mathf.Clamp01(p.y / .065f), lower = Mathf.Clamp01(-p.y / .08f);
                var key = (p, value.n, value.uv);
                if (variantId != FaceVariantId && unique.TryGetValue(key, out int index)) { indices.Add(index); return; }
                unique[key] = vertices.Count;
                indices.Add(vertices.Count); vertices.Add(p); normals.Add(value.n); uv.Add(value.uv);
                weights.Add(new BoneWeight { boneIndex0 = 0, weight0 = upper,
                    boneIndex1 = 1, weight1 = 1f - upper - lower, boneIndex2 = 2, weight2 = lower });
            }
            float DistanceToHead(Vector3 p)
            {
                // Preserve the low jaw at the front while clipping the source's shirt shoulders.
                if (variantId == FaceVariantId) return p.y - neckCut;
                return Mathf.Min(p.y - neckCut, Mathf.Max(p.y - 1.53f, .075f - Mathf.Abs(p.x)));
            }
            try
            {
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
                {
                    Mesh mesh = filter.sharedMesh; var p = mesh.vertices; var n = mesh.normals; var tex = mesh.uv; var tri = mesh.triangles;
                    original = filter.GetComponent<Renderer>().sharedMaterial;
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        var input = new List<Vertex>();
                        for (int j = 0; j < 3; j++) { int v = tri[i+j]; input.Add(new Vertex { p = filter.transform.TransformPoint(p[v]), n = filter.transform.TransformDirection(n[v]), uv = tex[v] }); }
                        var clipped = new List<Vertex>();
                        var cut = new List<Vertex>();
                        for (int j = 0; j < input.Count; j++)
                        {
                            Vertex a = input[j], b = input[(j+1)%input.Count];
                            float da = DistanceToHead(a.p), db = DistanceToHead(b.p);
                            bool ai = da >= 0f, bi = db >= 0f;
                            if (ai) clipped.Add(a);
                            if (ai != bi)
                            {
                                var edge = Vertex.Lerp(a,b,da/(da-db));
                                if (variantId == FaceVariantId) edge.p.y = neckCut;
                                clipped.Add(edge); cut.Add(edge);
                                if (edge.p.y < neckCut + .001f) cutPoints.Add(edge.p * 1.09f);
                            }
                        }
                        for (int j = 1; j+1 < clipped.Count; j++)
                            foreach (var value in new[] { clipped[0], clipped[j], clipped[j+1] })
                                Add(value);
                        if (cut.Count == 2)
                        {
                            // Orient the bridge to match the retained polygon's cut edge.
                            Vertex a = cut[0], b = cut[1];
                            if (Vector3.Dot(Vector3.Cross(b.p-a.p, Vector3.down), a.n+b.n) < 0f)
                                (a,b) = (b,a);
                            for (int ring = 0; ring < 2; ring++)
                            {
                                float top = ring * bridgeStep, bottom = top + bridgeStep;
                                Add(a,top); Add(b,top); Add(b,bottom);
                                Add(a,top); Add(b,bottom); Add(a,bottom);
                            }
                        }
                    }
                }
                if (variantId != FaceVariantId)
                {
                    // Whole-body source variants have different neck offsets. Seat each cut on the existing collar.
                    var reference = AssetDatabase.LoadAssetAtPath<Mesh>(Output + "/MS_OwnerHead.asset");
                    Bounds BoundsOf(Vector3[] points)
                    {
                        var bounds = new Bounds(points[0], Vector3.zero);
                        foreach (var point in points) bounds.Encapsulate(point);
                        return bounds;
                    }
                    Vector3 originalCenter = BoundsOf(reference.vertices.Where(p => Mathf.Abs(p.y) < .001f).ToArray()).center;
                    Vector3 cutCenter = BoundsOf(cutPoints.ToArray()).center;
                    Vector3 shift = new Vector3(originalCenter.x - cutCenter.x, 0f, originalCenter.z - cutCenter.z);
                    for (int i = 0; i < vertices.Count; i++) vertices[i] += shift;
                }
                var previousHead = head.Find("OwnerFace_" + variantId);
                var result = previousHead != null ? previousHead.gameObject : new GameObject("OwnerFace_" + variantId, typeof(SkinnedMeshRenderer));
                result.transform.SetParent(head, false);
                result.transform.position = neck.position + Vector3.up * .005f;
                result.transform.rotation = Quaternion.identity;
                result.transform.localScale = Vector3.one / head.lossyScale.x;
                var bones = new[] { head, neck, chest };
                var generated = new Mesh { name = "Kevin_" + variantId + "_ConnectedNeck", indexFormat = IndexFormat.UInt32 };
                generated.SetVertices(vertices); generated.SetNormals(normals); generated.SetUVs(0,uv); generated.SetTriangles(indices,0);
                generated.boneWeights = weights.ToArray();
                generated.bindposes = bones.Select(b => b.worldToLocalMatrix * result.transform.localToWorldMatrix).ToArray();
                generated.RecalculateBounds();
                string suffix = variantId == FaceVariantId ? "" : "_" + variantId;
                string path = Output + "/MS_OwnerHead" + suffix + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved == null) { AssetDatabase.CreateAsset(generated,path); saved = generated; }
                else { EditorUtility.CopySerialized(generated,saved); Object.DestroyImmediate(generated); }
                EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved);
                // Rebinding the approved faces to another body must not restyle their materials.
                var mat = AssetDatabase.LoadAssetAtPath<Material>(Output + "/MAT_OwnerHead" + suffix + ".mat");
                if (mat == null)
                {
                    mat = SaveMaterial("OwnerHead" + suffix, Color.white); mat.CopyPropertiesFromMaterial(original);
                    mat.DisableKeyword("_METALLICGLOSSMAP"); mat.SetFloat("_Metallic",0f); mat.SetFloat("_Glossiness",.15f); mat.SetFloat("_BumpScale",.2f);
                    EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat);
                }
                var renderer = result.GetComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = saved; renderer.sharedMaterial = mat; renderer.bones = bones;
                renderer.rootBone = chest; renderer.localBounds = saved.bounds; renderer.updateWhenOffscreen = true;
                return result;
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static Material SaveMaterial(string id, Color color)
        {
            string path = Output + "/MAT_" + id + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat,path); }
            mat.color = color; mat.SetFloat("_Metallic",0f); mat.SetFloat("_Glossiness",.18f);
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssetIfDirty(mat); return mat;
        }

        public static void InspectBatch()
        {
            Directory.CreateDirectory("Logs/Stage1Embodiment");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = (ModelImporter)AssetImporter.GetAtPath(BodyPath);
            importer.isReadable = true;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (string path in new[] { BodyPath, FacePath })
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var anim = root.GetComponent<Animator>();
                Debug.Log("[KevinInspect] " + path + " humanoid=" + (anim != null && anim.isHuman));
                foreach (var t in root.GetComponentsInChildren<Transform>())
                    Debug.Log("[KevinBone] " + t.name + " pos=" + t.position.ToString("F4") + " rotation=" + t.rotation.eulerAngles);
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    Debug.Log("[KevinMesh] " + r.name + " bounds=" + r.bounds + " materials=" + string.Join(",", r.sharedMaterials.Select(m => m.name + ":" + m.color)));
                    if (r is SkinnedMeshRenderer skin) Debug.Log("[KevinSkin] " + skin.name + " bones=" + string.Join(",", skin.bones.Select(b => b.name)));
                }
                RenderModel(root, "Logs/Stage1Embodiment/" + Path.GetFileNameWithoutExtension(path) + ".png");
                Object.DestroyImmediate(root);
            }
        }

        private static void RenderModel(GameObject root, string path)
        {
            var rs = root.GetComponentsInChildren<Renderer>();
            Bounds bounds = rs[0].bounds; foreach (var r in rs) bounds.Encapsulate(r.bounds);
            var c = new GameObject("InspectionCamera").AddComponent<Camera>();
            c.orthographic = true; c.orthographicSize = bounds.size.y * .58f;
            c.transform.position = bounds.center + new Vector3(0f, 0f, bounds.size.y * 2f);
            c.transform.LookAt(bounds.center); c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(.10f, .12f, .14f);
            var light = new GameObject("InspectionLight").AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35f, -140f, 0f); light.intensity = 1.2f;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.65f, .65f, .65f);
            var target = new RenderTexture(900, 1000, 24); c.targetTexture = target; c.Render();
            var old = RenderTexture.active; RenderTexture.active = target;
            var png = new Texture2D(900,1000,TextureFormat.RGB24,false); png.ReadPixels(new Rect(0,0,900,1000),0,0); png.Apply();
            File.WriteAllBytes(path,png.EncodeToPNG()); RenderTexture.active = old; c.targetTexture = null;
            Object.DestroyImmediate(target); Object.DestroyImmediate(png); Object.DestroyImmediate(c.gameObject); Object.DestroyImmediate(light.gameObject);
        }
    }
}
