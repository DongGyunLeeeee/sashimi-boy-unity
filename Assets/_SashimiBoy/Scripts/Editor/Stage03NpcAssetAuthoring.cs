using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    // Issue83 visual assets only. Imported nodes/source bytes remain authoritative.
    public static class Stage03NpcAssetAuthoring
    {
        public const string GeneratedRoot = "Assets/_SashimiBoy/Art/Generated/Stage03NpcAssets";
        public const string ManifestPath = "Docs/Assets/STAGE03_NPC_ASSET_MANIFEST.json";
        public const string ReportPath = GeneratedRoot + "/Stage03NpcAuthoringReport.json";
        public const float PreviewHeight = 1.75f;
        private const string SourceRoot = "Assets/_SashimiBoy/Art/Source/DayWorld/Characters/";
        private static readonly string[] Characters = { "Junseok", "Jaehoon" };
        private static readonly string[] Roles = { "Model", "basecolor", "normal", "metallic", "roughness", "rm" };

        [Serializable] private sealed class Manifest { public int issue; public Entry[] assets; }
        [Serializable] private sealed class Entry
        { public string character, role, path, sha256; public long bytes; }
        [Serializable] public sealed class CameraView
        { public string name; public Vector3 direction; public int width = 768, height = 1024; }
        [Serializable] public sealed class CharacterReport
        {
            public string character, sourceModelPath, baseColorPath, normalSourcePath, normalCopyPath;
            public string metallicSourcePath, roughnessSourcePath, rmSourcePath, materialPath, prefabPath, packedMapPath;
            public string sourceModelSha256, normalCopySha256, packedMapSha256;
            public Vector3 importedBoundsMin, importedBoundsMax, normalizedBoundsMin, normalizedBoundsMax;
            public float yawDegrees, uniformScale, previewHeight;
            public int meshCount, packedWidth, packedHeight;
            public long sourceVertices, sourceTriangles;
            public bool sourceBytesPreserved, rmBound, runtimePerformanceVerified;
        }
        [Serializable] public sealed class AuthoringReport
        {
            public int issue = 83, sourceFileCount = 12;
            public string algorithm = "Stage03NpcAssets-1";
            public string coordinateContract = "+Y up, +Z visual front after explicit calibration; positive uniform scale; feet at Y0, centered XZ. Preview height is not final world placement approval.";
            public string materialContract = "Standard metallic R=source metallic R; smoothness A=255-source roughness R. Decode scalar source file bytes linearly. Supplied rm is preserved and unbound.";
            public string geometryContract = "Complete imported hierarchy and source mesh references retained; no decimation, rigging, head cutting, animation or gameplay components generated.";
            public CharacterReport[] characters;
            public CameraView[] cameraViews = Views();
        }

        [MenuItem("Sashimi Boy/Stage 03/Generate NPC Preview Assets")]
        public static void Generate()
        {
            RequireEditor();
            using var scenes = new SceneStateGuard();
            using var console = new ConsoleFailureScope();
            var manifest = ReadManifest();
            VerifySources(manifest);
            EnsureFolder(GeneratedRoot + "/Materials");
            EnsureFolder(GeneratedRoot + "/Textures");
            EnsureFolder(GeneratedRoot + "/Prefabs");
            var reports = new List<CharacterReport>();
            foreach (string id in Characters)
            {
                Entry Get(string role) => manifest.assets.Single(e => e.character == id && e.role == role);
                var report = new CharacterReport
                {
                    character = id, sourceModelPath = Get("Model").path, baseColorPath = Get("basecolor").path,
                    normalSourcePath = Get("normal").path, metallicSourcePath = Get("metallic").path,
                    roughnessSourcePath = Get("roughness").path, rmSourcePath = Get("rm").path,
                    sourceModelSha256 = Get("Model").sha256,
                    normalCopyPath = GeneratedRoot + "/Textures/" + id + "_Normal" + Path.GetExtension(Get("normal").path),
                    packedMapPath = GeneratedRoot + "/Textures/" + id + "_MetallicSmoothness.png",
                    materialPath = GeneratedRoot + "/Materials/M_" + id + ".mat",
                    prefabPath = GeneratedRoot + "/Prefabs/PF_" + id + ".prefab",
                    previewHeight = PreviewHeight, yawDegrees = YawDegrees(id), rmBound = false,
                    runtimePerformanceVerified = false
                };
                Material material = BuildMaterial(report);
                BuildWrapper(report, material);
                report.normalCopySha256 = Hash(report.normalCopyPath);
                report.packedMapSha256 = Hash(report.packedMapPath);
                report.sourceBytesPreserved = true;
                reports.Add(report);
            }
            VerifySources(manifest);
            WriteIfDifferent(ReportPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                new AuthoringReport { characters = reports.ToArray() }, true) + "\n"));
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Stage03NpcAssets] Generated two source-backed visual wrappers. World/story/rigging and target-device performance are not verified.");
        }

        public static void GenerateAndCaptureBatch() { Generate(); CaptureBatch(); }

        public static void CaptureBatch()
        {
            RequireEditor();
            Require(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                "NPC previews require a graphics device; omit -nographics.");
            string output = ExternalOutput();
            using var scenes = new SceneStateGuard();
            using var console = new ConsoleFailureScope();
            using var preview = new IsolatedScene();
            var cameraObject = preview.Create("NpcPreviewCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false; camera.scene = preview.Scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .14f, .17f);
            camera.orthographic = true; camera.nearClipPlane = .001f; camera.farClipPlane = 100f;
            AddLight(preview, "Key", Quaternion.Euler(35f, -35f, 0f), 1f);
            AddLight(preview, "Fill", Quaternion.Euler(30f, 145f, 0f), .6f);
            AddLight(preview, "Rim", Quaternion.Euler(65f, 210f, 0f), .25f);
            foreach (string id in Characters)
            {
                string path = GeneratedRoot + "/Prefabs/PF_" + id + ".prefab";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(asset != null, "Generate the NPC prefab before capture: " + id);
                var instance = preview.Instantiate(asset);
                try
                {
                    Bounds bounds = LocalBounds(instance.transform);
                    foreach (var view in Views())
                    {
                        Texture2D image = Render(camera, bounds, view);
                        try { WriteCaptureFile(Path.Combine(output, id + "_" + view.name + ".png"), image.EncodeToPNG()); }
                        finally { Object.DestroyImmediate(image); }
                    }
                }
                finally { Object.DestroyImmediate(instance); }
            }
            WriteCaptureFile(Path.Combine(output, "NpcCaptureViews.json"), new System.Text.UTF8Encoding(false).GetBytes(
                JsonUtility.ToJson(new CaptureReport { cameraViews = Views() }, true)));
            Debug.Log("[Stage03NpcAssets] Front/side/back previews captured. Owner visual confirmation remains pending.");
        }

        [Serializable] private sealed class CaptureReport
        { public int issue = 83; public CameraView[] cameraViews; }
        private static CameraView[] Views() => new[]
        {
            new CameraView { name = "front", direction = Vector3.forward },
            new CameraView { name = "side", direction = Vector3.right },
            new CameraView { name = "back", direction = Vector3.back }
        };
        // Explicit per-model calibration. FBX FrontAxis is not proof of the face direction.
        // Change only after inspecting actual imported front/back/side evidence.
        private static float YawDegrees(string id) => id == "Junseok" ? 0f : 0f;

        private static Manifest ReadManifest()
        {
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Absolute(ManifestPath)));
            Require(manifest != null && manifest.issue == 83 && manifest.assets != null && manifest.assets.Length == 12,
                "Issue83 requires its separate twelve-file manifest.");
            Require(manifest.assets.Select(e => e.path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 12,
                "NPC manifest has duplicate paths.");
            foreach (string id in Characters) foreach (string role in Roles)
            {
                var matches = manifest.assets.Where(e => e.character == id && e.role == role).ToArray();
                Require(matches.Length == 1, "Require exactly one NPC input: " + id + "/" + role);
                Entry entry = matches[0];
                Require(entry.path.StartsWith(SourceRoot + id + "/", StringComparison.Ordinal) &&
                        !entry.path.Contains('\\') && !entry.path.Split('/').Any(p => p == "." || p == "..") &&
                        !Path.IsPathRooted(entry.path), "NPC input must retain its explicit repository-relative character path.");
            }
            return manifest;
        }
        private static void VerifySources(Manifest manifest)
        {
            foreach (Entry entry in manifest.assets)
            {
                string path = Absolute(entry.path);
                Require(File.Exists(path) && new FileInfo(path).Length == entry.bytes && Hash(entry.path) == entry.sha256,
                    "NPC source byte/hash mismatch: " + entry.path);
            }
        }

        private static Material BuildMaterial(CharacterReport report)
        {
            WriteIfDifferent(report.normalCopyPath, File.ReadAllBytes(Absolute(report.normalSourcePath)));
            AssetDatabase.ImportAsset(report.normalCopyPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureTexture(report.normalCopyPath, true);
            BuildPackedMap(report);
            Shader shader = Shader.Find("Standard");
            Require(shader != null, "Built-in Standard shader is required.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(report.materialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, report.materialPath); }
            material.shader = shader; material.color = Color.white;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(report.baseColorPath);
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(report.normalCopyPath));
            material.SetFloat("_BumpScale", 1f); material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(report.packedMapPath));
            material.SetFloat("_Metallic", 1f); material.SetFloat("_Glossiness", 1f); material.SetFloat("_GlossMapScale", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f); material.EnableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.SetTexture("_OcclusionMap", null); material.SetTexture("_EmissionMap", null);
            material.SetColor("_EmissionColor", Color.black); material.DisableKeyword("_EMISSION");
            Require(material.mainTexture != null && material.GetTexture("_BumpMap") != null && material.GetTexture("_MetallicGlossMap") != null,
                "NPC material has an unresolved explicit texture: " + report.character);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }
        private static void ConfigureTexture(string path, bool normal)
        {
            Require(path.StartsWith(GeneratedRoot + "/Textures/", StringComparison.Ordinal), "Only generated texture importers may change.");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer != null, "Generated texture importer is unavailable: " + path);
            TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool changed = importer.textureType != type || importer.sRGBTexture || importer.isReadable ||
                importer.convertToNormalmap || importer.maxTextureSize != 4096 ||
                (!normal && (importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.alphaIsTransparency ||
                              importer.textureCompression != TextureImporterCompression.Uncompressed));
            if (!changed) return;
            importer.textureType = type; importer.sRGBTexture = false; importer.isReadable = false;
            importer.convertToNormalmap = false; importer.maxTextureSize = 4096;
            if (!normal)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
            }
            importer.SaveAndReimport();
        }
        private static void BuildPackedMap(CharacterReport report)
        {
            Texture2D metallic = null, roughness = null, output = null;
            try
            {
                metallic = ReadScalar(report.metallicSourcePath); roughness = ReadScalar(report.roughnessSourcePath);
                Color32[] m = metallic.GetPixels32(), r = roughness.GetPixels32();
                var pixels = new Color32[m.Length];
                for (int y = 0; y < metallic.height; y++) for (int x = 0; x < metallic.width; x++)
                {
                    int rx = Mathf.Min(roughness.width - 1, x * roughness.width / metallic.width);
                    int ry = Mathf.Min(roughness.height - 1, y * roughness.height / metallic.height);
                    int index = y * metallic.width + x;
                    pixels[index] = new Color32(m[index].r, 0, 0, (byte)(255 - r[ry * roughness.width + rx].r));
                }
                output = new Texture2D(metallic.width, metallic.height, TextureFormat.RGBA32, false, true);
                output.SetPixels32(pixels); output.Apply(false, false);
                report.packedWidth = metallic.width; report.packedHeight = metallic.height;
                WriteIfDifferent(report.packedMapPath, output.EncodeToPNG());
                AssetDatabase.ImportAsset(report.packedMapPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureTexture(report.packedMapPath, false);
            }
            finally
            {
                if (output != null) Object.DestroyImmediate(output);
                if (roughness != null) Object.DestroyImmediate(roughness);
                if (metallic != null) Object.DestroyImmediate(metallic);
            }
        }
        private static Texture2D ReadScalar(string path)
        {
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                Require(ImageConversion.LoadImage(texture, File.ReadAllBytes(Absolute(path)), false),
                    "Could not decode NPC scalar source bytes: " + path);
                Texture2D result = texture; texture = null;
                return result;
            }
            finally { if (texture != null) Object.DestroyImmediate(texture); }
        }

        private static void BuildWrapper(CharacterReport report, Material material)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(report.sourceModelPath);
            Require(source != null, "NPC model has not imported: " + report.character);
            using var preview = new IsolatedScene();
            var root = preview.Create("PF_" + report.character);
            var frame = preview.Create("NormalizedModel"); frame.transform.SetParent(root.transform, false);
            var model = preview.Instantiate(source);
            // Preserve ALL imported node transforms; only this new parent supplies calibration.
            model.transform.SetParent(frame.transform, false);
            frame.transform.localRotation = Quaternion.Euler(0f, report.yawDegrees, 0f);
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            Require(filters.Length > 0 && model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0,
                "Issue83 expects supplied static meshes; do not manufacture a rig.");
            foreach (var filter in filters)
            {
                Mesh mesh = filter.sharedMesh;
                Require(mesh != null && mesh.vertexCount > 0, "Empty NPC source mesh.");
                report.meshCount++; report.sourceVertices += mesh.vertexCount;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Require(mesh.GetTopology(sub) == MeshTopology.Triangles, "NPC geometry must use triangles.");
                    report.sourceTriangles += (long)mesh.GetIndexCount(sub) / 3;
                }
            }
            Require(model.GetComponentsInChildren<Collider>(true).Length == 0 &&
                    model.GetComponentsInChildren<Animator>(true).Length == 0 &&
                    model.GetComponentsInChildren<Animation>(true).Length == 0,
                "Issue83 preview inputs must not add collider/animation components.");
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                Require(renderer.sharedMaterials.Length > 0, "NPC renderer has no material slots.");
                renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            }
            Bounds initial = LocalBounds(root.transform);
            report.importedBoundsMin = initial.min; report.importedBoundsMax = initial.max;
            Require(Finite(initial.size) && initial.size.y > .000001f, "NPC imported height is invalid.");
            float scale = PreviewHeight / initial.size.y;
            Require(Finite(scale) && scale > 0f, "NPC positive normalization scale is invalid.");
            frame.transform.localScale = Vector3.one * scale;
            Bounds scaled = LocalBounds(root.transform);
            frame.transform.localPosition -= new Vector3(scaled.center.x, scaled.min.y, scaled.center.z);
            Bounds final = LocalBounds(root.transform);
            report.uniformScale = scale; report.normalizedBoundsMin = final.min; report.normalizedBoundsMax = final.max;
            Require(Mathf.Abs(final.min.y) < .0001f && Mathf.Abs(final.center.x) < .0001f && Mathf.Abs(final.center.z) < .0001f &&
                    Mathf.Abs(final.size.y - PreviewHeight) < .0001f, "NPC normalization did not reach its contract.");
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                Require(Finite(transform.localScale) && Mathf.Min(transform.localScale.x, transform.localScale.y, transform.localScale.z) > 0f,
                    "NPC wrapper may not contain negative or zero scales.");
            Require(PrefabUtility.SaveAsPrefabAsset(root, report.prefabPath) != null, "Could not save the NPC preview wrapper.");
        }
        private static Bounds LocalBounds(Transform root)
        {
            bool found = false; var bounds = new Bounds();
            Matrix4x4 inverse = root.worldToLocalMatrix;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Require(filter.sharedMesh != null, "Missing NPC mesh reference.");
                Matrix4x4 matrix = inverse * filter.transform.localToWorldMatrix;
                foreach (Vector3 point in Corners(filter.sharedMesh.bounds))
                {
                    Vector3 p = matrix.MultiplyPoint3x4(point);
                    Require(Finite(p), "NPC bounds are nonfinite.");
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                }
            }
            Require(found, "NPC wrapper has no mesh bounds."); return bounds;
        }
        private static Vector3[] Corners(Bounds b)
        {
            var result = new Vector3[8]; int index = 0;
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
                result[index++] = b.center + Vector3.Scale(b.extents, new Vector3(x, y, z));
            return result;
        }

        private static void AddLight(IsolatedScene scene, string name, Quaternion rotation, float intensity)
        {
            var go = scene.Create(name, typeof(Light)); go.transform.rotation = rotation;
            var light = go.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = intensity; light.shadows = LightShadows.None;
        }
        private static Texture2D Render(Camera camera, Bounds bounds, CameraView view)
        {
            RenderTexture previousActive = RenderTexture.active, previousTarget = camera.targetTexture;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture target = null; Texture2D result = null;
            try
            {
                float radius = Mathf.Max(bounds.size.magnitude * .5f, .01f);
                camera.transform.position = bounds.center + view.direction.normalized * radius * 4f;
                camera.transform.LookAt(bounds.center); camera.aspect = (float)view.width / view.height;
                Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
                foreach (var p in Corners(bounds))
                { Vector3 c = camera.transform.InverseTransformPoint(p); min = Vector3.Min(min, c); max = Vector3.Max(max, c); }
                camera.orthographicSize = Mathf.Max((max.y - min.y) * .5f, (max.x - min.x) * .5f / camera.aspect) * 1.12f;
                target = new RenderTexture(view.width, view.height, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                target.Create(); camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                result = new Texture2D(view.width, view.height, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0f, 0f, view.width, view.height), 0, 0, false); result.Apply(false, false);
                var answer = result; result = null; return answer;
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive; GL.sRGBWrite = previousSrgb;
                if (result != null) Object.DestroyImmediate(result);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }
        private sealed class IsolatedScene : IDisposable
        {
            public Scene Scene { get; } = EditorSceneManager.NewPreviewScene();
            public GameObject Create(string name, params Type[] components)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
                try { SceneManager.MoveGameObjectToScene(go, Scene); go.hideFlags = HideFlags.None; return go; }
                catch { Object.DestroyImmediate(go); throw; }
            }
            public GameObject Instantiate(GameObject source)
            { return (GameObject)PrefabUtility.InstantiatePrefab(source, Scene); }
            public void Dispose() { if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene); }
        }
        private sealed class SceneStateGuard : IDisposable
        {
            private readonly Scene[] scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            private readonly int active = SceneManager.GetActiveScene().handle;
            private readonly bool[] dirty;
            private readonly int[][] roots;
            public SceneStateGuard()
            {
                dirty = scenes.Select(s => s.isDirty).ToArray();
                roots = scenes.Select(s => s.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(i => i).ToArray()).ToArray();
            }
            public void Dispose()
            {
                Require(SceneManager.sceneCount == scenes.Length && SceneManager.GetActiveScene().handle == active,
                    "NPC authoring changed loaded/active scenes; no restore/discard was attempted.");
                for (int i = 0; i < scenes.Length; i++)
                {
                    Scene current = SceneManager.GetSceneAt(i);
                    Require(current.handle == scenes[i].handle && current.isDirty == dirty[i] &&
                        current.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).SequenceEqual(roots[i]),
                        "NPC authoring changed an existing scene/unsaved roots; no restore/discard was attempted.");
                }
            }
        }
        private sealed class ConsoleFailureScope : IDisposable
        {
            private readonly List<string> failures = new List<string>();
            public ConsoleFailureScope() { Application.logMessageReceived += Record; }
            private void Record(string text, string stack, LogType type)
            { if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) failures.Add(text); }
            public void Dispose()
            {
                Application.logMessageReceived -= Record;
                Require(failures.Count == 0, "NPC authoring Console failure: " + string.Join(" | ", failures.Take(3)));
            }
        }
        private static string ExternalOutput()
        {
            string path = Environment.GetEnvironmentVariable("SASHIMIBOY_NPC_PREVIEW_DIR");
            Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path), "SASHIMIBOY_NPC_PREVIEW_DIR must name an external absolute evidence folder.");
            path = Path.GetFullPath(path);
            string project = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath)) + Path.DirectorySeparatorChar;
            Require(!path.StartsWith(project, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), project.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
                "NPC preview evidence must be outside the checkout.");
            AssertExternalAncestors(path);
            Require(!File.Exists(path), "NPC preview output must be a directory.");
            Require(!Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any(),
                "NPC preview output must be a fresh or empty directory; existing evidence/files are preserved.");
            Directory.CreateDirectory(path);
            AssertExternalAncestors(path);
            return path;
        }
        private static void AssertExternalAncestors(string path)
        {
            for (var parent = new DirectoryInfo(path); parent != null; parent = parent.Parent)
            {
                Require(!File.Exists(parent.FullName), "NPC preview output contains a file ancestor.");
                if (Directory.Exists(parent.FullName))
                    Require((parent.Attributes & FileAttributes.ReparsePoint) == 0,
                        "NPC preview output may not traverse reparse points.");
                Require(!File.Exists(Path.Combine(parent.FullName, ".git")) && !Directory.Exists(Path.Combine(parent.FullName, ".git")),
                    "NPC previews cannot be written within any Git checkout.");
            }
        }
        private static void WriteCaptureFile(string path, byte[] bytes)
        {
            AssertExternalAncestors(Path.GetDirectoryName(path));
            // CreateNew refuses to overwrite a file even if one appeared after the empty-directory check.
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(bytes, 0, bytes.Length);
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); Require(slash > 0, "Generated folder must be under Assets.");
            EnsureFolder(path.Substring(0, slash));
            Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1))), "Could not create NPC generated folder.");
        }
        private static void WriteIfDifferent(string path, byte[] bytes)
        {
            Require(path.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal), "Writes are restricted to NPC generated assets.");
            string absolute = Absolute(path);
            if (!File.Exists(absolute) || !File.ReadAllBytes(absolute).SequenceEqual(bytes)) File.WriteAllBytes(absolute, bytes);
        }
        private static string Hash(string path)
        {
            using var input = File.OpenRead(Absolute(path)); using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        private static string Absolute(string path) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static void RequireEditor() => Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before NPC authoring.");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
