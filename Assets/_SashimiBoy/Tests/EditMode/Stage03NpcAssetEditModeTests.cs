using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.Tests
{
    public sealed class Stage03NpcAssetEditModeTests
    {
        private const string ManifestPath = "Docs/Assets/STAGE03_NPC_ASSET_MANIFEST.json";
        private const string LegacyManifest = "Docs/Assets/STAGE03_ASSET_MANIFEST.json";
        private const string LegacyManifestHash = "8a586c697634f95bc42dedb8ee33f9c6f1370ebcffb43104a068db2fc0f8de26";
        private const string Generated = "Assets/_SashimiBoy/Art/Generated/Stage03NpcAssets";
        private const string Source = "Assets/_SashimiBoy/Art/Source";
        private const string CharacterSource = Source + "/DayWorld/Characters/";
        private static string Root => Directory.GetParent(Application.dataPath).FullName;

        [Serializable] private sealed class Intake
        {
            public int issue;
            public Package[] sources;
            public Mapping[] mappings;
            public Entry[] assets;
        }
        [Serializable] private sealed class Package { public string name, sha256; public long bytes; }
        [Serializable] private sealed class Mapping { public string id, displayName, archive, fbxPath, texturePrefix; }
        [Serializable] private sealed class Entry { public string archive, entry, path, character, role, sha256; public long bytes; }
        [Serializable] private sealed class LegacyIntake { public Entry[] assets; }

        private static Intake ReadManifest()
        {
            var value = JsonUtility.FromJson<Intake>(File.ReadAllText(Absolute(ManifestPath)));
            Assert.That(value, Is.Not.Null);
            Assert.That(value.issue, Is.EqualTo(83));
            Assert.That(value.sources, Has.Length.EqualTo(2));
            Assert.That(value.mappings, Has.Length.EqualTo(2));
            Assert.That(value.assets, Has.Length.EqualTo(12));
            return value;
        }

        [Test]
        public void Manifest_RegistersApprovedTwelveOriginalsAndPreservesLegacyIntake()
        {
            Intake manifest = ReadManifest();
            Assert.That(Hash(Absolute(LegacyManifest)), Is.EqualTo(LegacyManifestHash),
                "The existing 68-file manifest is a separate unchanged contract.");
            var legacy = JsonUtility.FromJson<LegacyIntake>(File.ReadAllText(Absolute(LegacyManifest)));
            Assert.That(legacy.assets, Has.Length.EqualTo(68));
            Assert.That(manifest.assets.Select(e => e.path).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(12));
            Assert.That(manifest.assets.Select(e => e.path).Intersect(legacy.assets.Select(e => e.path), StringComparer.OrdinalIgnoreCase), Is.Empty);
            Assert.That(manifest.mappings.Select(m => m.id), Is.EquivalentTo(new[] { "Junseok", "Jaehoon" }));
            Assert.That(manifest.sources.Select(s => s.name), Is.EquivalentTo(new[] { "재훈.zip", "준석.zip" }));
            AssertPackage(manifest, "재훈.zip", 63943902L, "5f17ec437f9861a4c5a3e6d999a5be50593b25cd7356ab6f95560f5deddad8e3");
            AssertPackage(manifest, "준석.zip", 63152114L, "a9fb3e05c326f01e16b5f0e3670efaaf1de34bc180eba4503bc3701eb5dc6913");
            foreach (string id in new[] { "Junseok", "Jaehoon" })
            {
                Mapping mapping = manifest.mappings.Single(m => m.id == id);
                bool junseok = id == "Junseok";
                string prefix = junseok ? "junseok_" : "jaehoon_";
                string archive = junseok ? "재훈.zip" : "준석.zip";
                string modelId = junseok ? "4b828a99-2bf3-439d-b7b8-19988b8469b0" : "994f3d88-2ebb-4bd5-8313-744a6cd4fd47";
                Assert.That(mapping.archive, Is.EqualTo(archive));
                Assert.That(mapping.texturePrefix.TrimEnd('_'), Is.EqualTo(prefix.TrimEnd('_')));
                Assert.That(mapping.fbxPath, Does.Contain(modelId));
                Entry[] entries = manifest.assets.Where(e => e.character == id).ToArray();
                Assert.That(entries, Has.Length.EqualTo(6), id);
                Assert.That(entries.Count(e => e.path.EndsWith(".fbx", StringComparison.Ordinal)), Is.EqualTo(1));
                Assert.That(entries.Count(e => e.path.EndsWith(".JPEG", StringComparison.Ordinal)), Is.EqualTo(5));
                Assert.That(entries.Single(e => e.path.EndsWith(".fbx", StringComparison.Ordinal)).path, Is.EqualTo(mapping.fbxPath));
                Assert.That(entries.Where(e => e.path.EndsWith(".JPEG", StringComparison.Ordinal)).Select(e => Path.GetFileName(e.path)),
                    Is.EquivalentTo(new[] { prefix + "basecolor.JPEG", prefix + "normal.JPEG", prefix + "metallic.JPEG", prefix + "roughness.JPEG", prefix + "rm.JPEG" }));
                foreach (Entry entry in entries)
                {
                    Assert.That(entry.archive, Is.EqualTo(archive), entry.path);
                    Assert.That(entry.entry.Contains('\\'), Is.False);
                    Assert.That(entry.entry.Split('/').Any(p => p == ".." || p == "." || p == ""), Is.False);
                    Assert.That(entry.path, Is.EqualTo(CharacterSource + id + "/" + entry.entry));
                    Assert.That(entry.role, Is.Not.Null.And.Not.Empty);
                    Assert.That(entry.sha256, Does.Match("^[a-f0-9]{64}$"), entry.path);
                    Assert.That(entry.bytes, Is.GreaterThan(0), entry.path);
                    Assert.That(new FileInfo(Absolute(entry.path)).Length, Is.EqualTo(entry.bytes), entry.path);
                    Assert.That(Hash(Absolute(entry.path)), Is.EqualTo(entry.sha256), entry.path);
                    string guid = AssetDatabase.AssetPathToGUID(entry.path);
                    Assert.That(guid, Does.Match("^[a-f0-9]{32}$"), entry.path);
                    Assert.That(AssetDatabase.GUIDToAssetPath(guid), Is.EqualTo(entry.path));
                    Assert.That(File.ReadAllText(Absolute(entry.path) + ".meta"), Does.Contain("guid: " + guid));
                }
            }
            Assert.That(manifest.assets.Select(e => AssetDatabase.AssetPathToGUID(e.path)).Distinct().Count(), Is.EqualTo(12));
        }

        [TestCase("Junseok")]
        [TestCase("Jaehoon")]
        public void ImportedOriginal_HasNonemptyStaticGeometryAndExpectedTextureTypes(string id)
        {
            Intake manifest = ReadManifest();
            Mapping mapping = manifest.mappings.Single(m => m.id == id);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(mapping.fbxPath);
            Assert.That(source, Is.Not.Null, mapping.fbxPath);
            Assert.That(source.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty, "No skeletal motion was supplied.");
            Assert.That(source.GetComponentsInChildren<Animator>(true), Is.Empty);
            MeshFilter[] meshes = source.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(meshes, Is.Not.Empty);
            foreach (MeshFilter filter in meshes)
            {
                Assert.That(filter.sharedMesh, Is.Not.Null);
                Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0));
                Assert.That(filter.sharedMesh.subMeshCount, Is.GreaterThan(0));
                Assert.That(Enumerable.Range(0, filter.sharedMesh.subMeshCount).Sum(i => (long)filter.sharedMesh.GetIndexCount(i)), Is.GreaterThan(0));
                Assert.That(filter.GetComponent<MeshRenderer>(), Is.Not.Null);
            }
            foreach (Entry texture in manifest.assets.Where(e => e.character == id && e.path.EndsWith(".JPEG", StringComparison.Ordinal)))
                Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(texture.path), Is.Not.Null, texture.path);
        }

        [TestCase("Junseok")]
        [TestCase("Jaehoon")]
        public void PreviewWrapper_IsGroundedUniformAndUsesOnlyProvidedGeometry(string id)
        {
            Intake manifest = ReadManifest();
            Mapping mapping = manifest.mappings.Single(m => m.id == id);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Generated + "/Prefabs/PF_" + id + ".prefab");
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(mapping.fbxPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(source, Is.Not.Null);
            Assert.That(prefab.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(Quaternion.Angle(prefab.transform.localRotation, Quaternion.identity), Is.LessThan(.001f));
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(prefab.transform.up, Is.EqualTo(Vector3.up));
            Assert.That(prefab.transform.forward, Is.EqualTo(Vector3.forward));
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                Vector3 s = t.localScale;
                Assert.That(Mathf.Min(s.x, s.y, s.z), Is.GreaterThan(0f), t.name);
                Assert.That(s.x, Is.EqualTo(s.y).Within(.00001f), t.name);
                Assert.That(s.x, Is.EqualTo(s.z).Within(.00001f), t.name);
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject), Is.Zero);
            }
            foreach (Component c in prefab.GetComponentsInChildren<Component>(true))
            {
                Assert.That(c, Is.Not.Null);
                Assert.That(c is Transform || c is MeshFilter || c is MeshRenderer, Is.True,
                    "Preview assets have no gameplay, collider, camera, audio, rig or procedural pose ownership: " + c.GetType().Name);
                using (var serialized = new SerializedObject(c))
                {
                    SerializedProperty p = serialized.GetIterator();
                    while (p.NextVisible(true))
                        if (p.propertyType == SerializedPropertyType.ObjectReference)
                            Assert.That(p.objectReferenceValue == null && p.objectReferenceInstanceIDValue != 0, Is.False, c.name + "/" + p.propertyPath);
                }
            }
            MeshFilter[] sourceMeshes = source.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter[] wrappedMeshes = prefab.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(wrappedMeshes.Length, Is.EqualTo(sourceMeshes.Length));
            Assert.That(wrappedMeshes.Select(f => f.sharedMesh), Is.EquivalentTo(sourceMeshes.Select(f => f.sharedMesh)),
                "No head splitting, posing, replacement character or geometry rewrite belongs to this intake.");
            foreach (MeshFilter filter in wrappedMeshes)
                Assert.That(AssetDatabase.GetAssetPath(filter.sharedMesh), Is.EqualTo(mapping.fbxPath));
            var instance = Object.Instantiate(prefab);
            var original = Object.Instantiate(source);
            try
            {
                Bounds normalized = BoundsOf(instance);
                AssertFinite(normalized);
                Assert.That(normalized.size.y, Is.EqualTo(1.75f).Within(.002f), "Preview scale is not a final world-height decision.");
                Assert.That(normalized.min.y, Is.EqualTo(0f).Within(.002f));
                Assert.That(normalized.center.x, Is.EqualTo(0f).Within(.002f));
                Assert.That(normalized.center.z, Is.EqualTo(0f).Within(.002f));
                Bounds raw = BoundsOf(original);
                Assert.That(normalized.size.x / normalized.size.y, Is.EqualTo(raw.size.x / raw.size.y).Within(.002f));
                Assert.That(normalized.size.z / normalized.size.y, Is.EqualTo(raw.size.z / raw.size.y).Within(.002f));
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.That(renderer.sharedMaterials, Is.Not.Empty);
                    Assert.That(renderer.sharedMaterials.All(m => m != null && AssetDatabase.GetAssetPath(m) == Generated + "/Materials/M_" + id + ".mat"), Is.True);
                }
            }
            finally { Object.DestroyImmediate(instance); Object.DestroyImmediate(original); }
            // +Z anatomy needs front/side/back capture verification; an AABB cannot identify a face.
        }

        [TestCase("Junseok")]
        [TestCase("Jaehoon")]
        public void Materials_BindExplicitMapsAndPackOriginalLinearPixelValues(string id)
        {
            Intake manifest = ReadManifest();
            Entry[] entries = manifest.assets.Where(e => e.character == id).ToArray();
            string PathFor(string suffix) => entries.Single(e => e.path.EndsWith(suffix, StringComparison.Ordinal)).path;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(Generated + "/Materials/M_" + id + ".mat");
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Standard"));
            Assert.That(material.shader.isSupported, Is.True);
            Assert.That(material.color, Is.EqualTo(Color.white));
            Assert.That(AssetDatabase.GetAssetPath(material.mainTexture), Is.EqualTo(PathFor("_basecolor.JPEG")));
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(PathFor("_basecolor.JPEG"))).sRGBTexture, Is.True);
            string normalPath = Generated + "/Textures/" + id + "_Normal.JPEG";
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap")), Is.EqualTo(normalPath));
            Assert.That(Hash(Absolute(normalPath)), Is.EqualTo(Hash(Absolute(PathFor("_normal.JPEG")))));
            var normalImporter = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            Assert.That(normalImporter, Is.Not.Null);
            Assert.That(normalImporter.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(normalImporter.sRGBTexture, Is.False);
            Assert.That(normalImporter.convertToNormalmap, Is.False);
            Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
            Assert.That(material.GetFloat("_BumpScale"), Is.EqualTo(1f));
            string packedPath = Generated + "/Textures/" + id + "_MetallicSmoothness.png";
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_MetallicGlossMap")), Is.EqualTo(packedPath));
            Assert.That(material.IsKeywordEnabled("_METALLICGLOSSMAP"), Is.True);
            Assert.That(material.GetFloat("_GlossMapScale"), Is.EqualTo(1f));
            Assert.That(material.GetFloat("_SmoothnessTextureChannel"), Is.EqualTo(0f));
            var packedImporter = (TextureImporter)AssetImporter.GetAtPath(packedPath);
            Assert.That(packedImporter, Is.Not.Null);
            Assert.That(packedImporter.sRGBTexture, Is.False);
            Assert.That(packedImporter.textureType, Is.EqualTo(TextureImporterType.Default));
            Assert.That(packedImporter.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput));
            Assert.That(packedImporter.alphaIsTransparency, Is.False);
            foreach (string property in material.GetTexturePropertyNames())
                Assert.That(AssetDatabase.GetAssetPath(material.GetTexture(property)).EndsWith("_rm.JPEG", StringComparison.OrdinalIgnoreCase), Is.False, property);
            Decoded metallic = Decode(PathFor("_metallic.JPEG"));
            Decoded roughness = Decode(PathFor("_roughness.JPEG"));
            Decoded packed = Decode(packedPath);
            Assert.That(packed.width, Is.EqualTo(metallic.width));
            Assert.That(packed.height, Is.EqualTo(metallic.height));
            foreach (int y in SampleAxis(packed.height))
            foreach (int x in SampleAxis(packed.width))
            {
                Color32 pixel = packed.pixels[y * packed.width + x];
                Color32 metal = metallic.pixels[y * metallic.width + x];
                int rx = Mathf.Min(roughness.width - 1, x * roughness.width / metallic.width);
                int ry = Mathf.Min(roughness.height - 1, y * roughness.height / metallic.height);
                byte rough = roughness.pixels[ry * roughness.width + rx].r;
                Assert.That(pixel.r, Is.EqualTo(metal.r), id + " metallic pixel " + x + "," + y);
                Assert.That(pixel.a, Is.EqualTo((byte)(255 - rough)), id + " smoothness pixel " + x + "," + y);
            }
        }

        [Test]
        public void GenerateTwice_PreservesBytesGuidsSourcesAndUnsavedEditorState()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "Run the strict Unity wrapper with EnableGraphics for the render-target preservation check.");
            Intake manifest = ReadManifest();
            var legacy = JsonUtility.FromJson<LegacyIntake>(File.ReadAllText(Absolute(LegacyManifest)));
            string[] ProtectedPaths() => manifest.assets.Select(e => Absolute(e.path))
                .Concat(legacy.assets.Select(e => Absolute(e.path)))
                .Concat(new[] { Absolute(LegacyManifest), Absolute(ManifestPath) })
                .Concat(Directory.GetFiles(Application.dataPath, "*", SearchOption.AllDirectories)
                    .Where(p => (p.EndsWith(".meta", StringComparison.Ordinal) || p.EndsWith(".unity", StringComparison.Ordinal) || p.EndsWith(".prefab", StringComparison.Ordinal))
                        && !p.Replace('\\', '/').StartsWith(Absolute(Generated).Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Dictionary<string, string> protectedBefore = Snapshot(ProtectedPaths());
            Dictionary<string, string> generatedBefore = GeneratedSnapshot();
            string[] rawGuids = manifest.assets.Select(e => AssetDatabase.AssetPathToGUID(e.path)).ToArray();
            Scene previousActive = SceneManager.GetActiveScene();
            RenderTexture previousTarget = RenderTexture.active;
            Scene sentinelScene = default;
            GameObject sentinel = null, cameraObject = null;
            Camera camera = null;
            RenderTexture target = null;
            try
            {
                sentinelScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                sentinel = new GameObject("Issue83_UnsavedSentinel");
                SceneManager.MoveGameObjectToScene(sentinel, sentinelScene);
                cameraObject = new GameObject("Issue83_DisabledSentinelCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, sentinelScene);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                target = new RenderTexture(8, 8, 0);
                Assert.That(target.Create(), Is.True);
                camera.targetTexture = target;
                RenderTexture.active = target;
                SceneManager.SetActiveScene(sentinelScene);
                EditorSceneManager.MarkSceneDirty(sentinelScene);
                SceneSnapshot[] scenesBefore = Scenes();
                int sentinelId = sentinel.GetInstanceID();
                var cameraTargets = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .ToDictionary(c => c.GetInstanceID(), c => c.targetTexture);
                for (int run = 1; run <= 2; run++)
                {
                    RuntimeReflection.InvokeStatic("SashimiBoy.EditorTools.Stage03NpcAssetAuthoring", "Generate");
                    AssertSnapshotsEqual(generatedBefore, GeneratedSnapshot(), "Generated byte/GUID drift at run " + run);
                    AssertSnapshotsEqual(protectedBefore, Snapshot(ProtectedPaths()), "Protected source/scene/prefab drift at run " + run);
                    Assert.That(manifest.assets.Select(e => AssetDatabase.AssetPathToGUID(e.path)).ToArray(), Is.EqualTo(rawGuids));
                    Assert.That(sentinel != null && sentinel.GetInstanceID() == sentinelId, Is.True);
                    Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(sentinelScene.handle));
                    Assert.That(RenderTexture.active, Is.SameAs(target));
                    AssertScenesEqual(scenesBefore, Scenes());
                    foreach (var pair in cameraTargets)
                    {
                        var originalCamera = EditorUtility.InstanceIDToObject(pair.Key) as Camera;
                        Assert.That(originalCamera, Is.Not.Null);
                        Assert.That(originalCamera.targetTexture, Is.SameAs(pair.Value));
                    }
                }
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (camera != null) camera.targetTexture = null;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                if (sentinel != null) Object.DestroyImmediate(sentinel);
                if (sentinelScene.IsValid() && sentinelScene.isLoaded) EditorSceneManager.CloseScene(sentinelScene, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                // Never restore unexpected source or serialized asset drift to hide a failure.
            }
        }

        private static void AssertPackage(Intake m, string name, long bytes, string hash)
        {
            Package source = m.sources.Single(s => s.name == name);
            Assert.That(source.bytes, Is.EqualTo(bytes));
            Assert.That(source.sha256, Is.EqualTo(hash));
        }
        private static string Absolute(string path)
        {
            Assert.That(Path.IsPathRooted(path), Is.False, path);
            string absolute = Path.GetFullPath(Path.Combine(Root, path));
            Assert.That(absolute.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True, path);
            return absolute;
        }
        private static string Hash(string absolute)
        {
            using (var file = File.OpenRead(absolute))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        private static Dictionary<string, string> Snapshot(IEnumerable<string> paths) =>
            paths.OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p, Hash, StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GeneratedSnapshot()
        {
            string folder = Absolute(Generated);
            Assert.That(Directory.Exists(folder), Is.True);
            string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            Assert.That(files.Length, Is.GreaterThan(0));
            return Snapshot(files.Concat(new[] { folder + ".meta" }));
        }
        private static void AssertSnapshotsEqual(Dictionary<string, string> expected, Dictionary<string, string> actual, string message)
        {
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys), message);
            foreach (var pair in expected) Assert.That(actual[pair.Key], Is.EqualTo(pair.Value), message + ": " + pair.Key);
        }
        private sealed class Decoded { public int width, height; public Color32[] pixels; }
        private static Decoded Decode(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(Absolute(path)), false), Is.True, path);
                return new Decoded { width = texture.width, height = texture.height, pixels = texture.GetPixels32() };
            }
            finally { Object.DestroyImmediate(texture); }
        }
        private static int[] SampleAxis(int length) =>
            new[] { 0, 1, length / 4, length / 2, length * 3 / 4, length - 2, length - 1 }.Distinct().ToArray();
        private static Bounds BoundsOf(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds result = renderers[0].bounds;
            foreach (Renderer r in renderers.Skip(1)) result.Encapsulate(r.bounds);
            return result;
        }
        private static void AssertFinite(Bounds b)
        {
            foreach (float f in new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z })
                Assert.That(float.IsNaN(f) || float.IsInfinity(f), Is.False);
            Assert.That(Mathf.Min(b.size.x, b.size.y, b.size.z), Is.GreaterThan(0f));
        }
        private sealed class SceneSnapshot { public int handle; public bool dirty; public int[] roots; }
        private static SceneSnapshot[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        {
            Scene s = SceneManager.GetSceneAt(i);
            return new SceneSnapshot { handle = s.handle, dirty = s.isDirty, roots = s.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(x => x).ToArray() };
        }).ToArray();
        private static void AssertScenesEqual(SceneSnapshot[] before, SceneSnapshot[] after)
        {
            Assert.That(after.Select(s => s.handle), Is.EqualTo(before.Select(s => s.handle)));
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(after[i].dirty, Is.EqualTo(before[i].dirty), "Scene dirty state");
                Assert.That(after[i].roots, Is.EqualTo(before[i].roots), "Loaded scene objects were replaced or leaked.");
            }
        }
    }
}
