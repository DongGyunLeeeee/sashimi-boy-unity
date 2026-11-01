using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    // Source-preserving presentation adaptation for the Owner's two edible sides.
    // No source FBX/texture/importer or original ten generated wrappers is changed.
    public static class Stage03TwoFilletAuthoring
    {
        public const string Root = Stage03FlounderAuthoring.Root;
        public const string ReportPath = Root + "/Stage03TwoFilletGeometry.json";
        private const string Algorithm = "Stage03TwoFillet-1";
        public static string WholeFilletPrefabPath(int side) => WrapperPath(side == 0 ? "FrontWholeFillet" : "BackWholeFillet");
        public static string MeshPath(string role) => Root + "/Meshes/MS_" + role + ".asset";
        private static string WrapperPath(string role) => Root + "/Prefabs/PF_" + role + ".prefab";

        [Serializable] public sealed class PhysicalSideGeometry
        {
            public string role, sourceRole, sourcePath, sourceDependencyHash, sourceMeshPath, generatedMeshPath, wrapperPath;
            public string operation, fleshUvSourceRole;
            public int sourceVertices, generatedVertices, generatedTriangles, selectedSurfaceTriangles;
            public Vector3 sourceSize, generatedSize, rotationEuler;
            public bool sourceUnchanged;
        }
        [Serializable] public sealed class GeometryReport
        {
            public string algorithm = Algorithm;
            public string[] sourceRoles = { "HeadRemovedFish", "FilletRemoved2", "FrontFillet", "BackFillet" };
            public string sourceUVObservation = "Actual source UV/triangle-area audit: Front +Y is pigmented skin, -Y is flesh; Back both faces are flesh. Front is rigidly rotated 180 degrees around longitudinal +Z. Skin-facing vertices then receive UVs from the supplied flesh-facing surface; no source texture is recolored.";
            public string coordinateContract = "Head +Z; front +Y; back -Y before the longitudinal 180-degree roll. Two discarded skins, exactly two edible source-backed single meshes. Positive scales only.";
            public string sourceScope = "Original ten FBX/texture/importer/meta and original generated mesh/material/wrapper assets remain unchanged. Adaptation is in separately named generated meshes and prefabs.";
            public PhysicalSideGeometry[] models;
        }

        public static void BuildAndBind()
        {
            BuildGeometry();
            BindAssembly();
        }

        public static void BuildGeometry()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Generate two-side presentation outside Play Mode.");
            var dependencies = Stage03FlounderAuthoring.Roles.ToDictionary(role => role, role =>
                AssetDatabase.GetAssetDependencyHash(Stage03FlounderAuthoring.SourceRoot + "/" + role + "/" + role + ".fbx").ToString());
            var reports = new List<PhysicalSideGeometry>();
            Mesh front = SourceMesh("FrontFillet"), back = SourceMesh("BackFillet");
            var frontFlesh = new FleshUvField(front, -1f);
            var backFlesh = new FleshUvField(back, 1f);
            Mesh frontWhole = BuildWholeFillet(front, true, frontFlesh);
            Mesh backWhole = BuildWholeFillet(back, false, backFlesh);
            SaveDerived("FrontWholeFillet", "FrontFillet", frontWhole, new[] { SourceMaterial("FrontFillet") },
                "Rigid longitudinal 180-degree rotation; existing source geometry and aspect ratio retained. All UVs sampled from the supplied flesh side, including the former skin underside.", "FrontFillet", 0, reports);
            SaveDerived("BackWholeFillet", "BackFillet", backWhole, new[] { SourceMaterial("BackFillet") },
                "Existing single flesh fillet, source aspect ratio and surface retained; no duplicate/composite or negative scale.", "BackFillet", 0, reports);

            Mesh frontBody = SourceMesh("HeadRemovedFish"), backBody = SourceMesh("FilletRemoved2");
            foreach (int side in new[] { 0, 1 })
            {
                string prefix = side == 0 ? "Front" : "Back";
                string sourceRole = side == 0 ? "HeadRemovedFish" : "FilletRemoved2";
                Mesh source = side == 0 ? frontBody : backBody;
                Mesh donor = side == 0 ? frontWhole : backWhole;
                var flesh = new FleshUvField(donor, 1f);
                Mesh body = BuildSkinnedBody(source, side == 0 ? 1f : -1f, flesh, out int selected);
                SaveDerived(prefix + "SkinnedBody", sourceRole, body,
                    new[] { SourceMaterial(sourceRole), SourceMaterial(side == 0 ? "FrontFillet" : "BackFillet") },
                    "Original complete body geometry retained. Only the working hemisphere receives the supplied fillet flesh material/UVs; the opposite physical side is preserved. This is a whole skinned side, not the original half-removed state.",
                    side == 0 ? "FrontFillet" : "BackFillet", selected, reports);
                Mesh skin = BuildSkin(source, side == 0 ? 1f : -1f, out int skinTriangles);
                SaveDerived(prefix + "Skin", sourceRole, skin, new[] { SourceMaterial(sourceRole) },
                    "Actual working-hemisphere surface extracted from the supplied body, with a 2mm inward shell and boundary walls. Separate discarded part, never an edible fillet.",
                    "", skinTriangles, reports);
            }
            foreach (var pair in dependencies)
                if (pair.Value != AssetDatabase.GetAssetDependencyHash(Stage03FlounderAuthoring.SourceRoot + "/" + pair.Key + "/" + pair.Key + ".fbx").ToString())
                    throw new InvalidOperationException("Source dependency changed during two-side generation: " + pair.Key);
            foreach (var model in reports) model.sourceUnchanged = model.sourceDependencyHash == dependencies[model.sourceRole];
            var report = new GeometryReport { models = reports.ToArray() };
            WriteText(ReportPath, JsonUtility.ToJson(report, true) + "\n");
            AssetDatabase.SaveAssets();
        }

        private static Mesh SourceMesh(string role)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Root + "/Meshes/MS_" + role + ".asset");
            if (mesh == null || !mesh.isReadable) throw new InvalidOperationException("Missing readable source-backed mesh " + role);
            return mesh;
        }
        private static Material SourceMaterial(string role)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_" + role + ".mat");
            if (material == null) throw new InvalidOperationException("Missing provided material " + role);
            return material;
        }

        private static Mesh BuildWholeFillet(Mesh source, bool front, FleshUvField flesh)
        {
            var vertices = source.vertices;
            var normals = source.normals;
            var uv = source.uv;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 original = vertices[i];
                if (front)
                {
                    // Proper rigid rotation, determinant +1; head remains +Z.
                    vertices[i] = new Vector3(-original.x, -original.y, original.z);
                    normals[i] = new Vector3(-normals[i].x, -normals[i].y, normals[i].z);
                    // Sample in the original donor coordinate system before rotation.
                    uv[i] = flesh.Sample(original, source.bounds);
                }
            }
            return MakeMesh(vertices, normals, uv, new[] { source.triangles });
        }

        private static Mesh BuildSkinnedBody(Mesh source, float side, FleshUvField donor, out int selected)
        {
            var originalV = source.vertices; var originalN = source.normals; var originalUv = source.uv;
            var vertices = new List<Vector3>(originalV); var normals = new List<Vector3>(originalN);
            var uv = new List<Vector2>(originalUv); var retained = new List<int>(); var exposed = new List<int>();
            var remapped = new Dictionary<int, int>();
            var triangles = source.triangles;
            selected = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                bool workingSide = IsWorkingTriangle(originalV, triangles, i, source.bounds.center.y, side);
                if (workingSide) selected++;
                for (int k = 0; k < 3; k++)
                {
                    int index = triangles[i + k];
                    if (!workingSide) { retained.Add(index); continue; }
                    if (!remapped.TryGetValue(index, out int copy))
                    {
                        copy = vertices.Count; remapped.Add(index, copy);
                        vertices.Add(originalV[index]); normals.Add(originalN[index]);
                        uv.Add(donor.Sample(originalV[index], source.bounds));
                    }
                    exposed.Add(copy);
                }
            }
            if (selected == 0 || retained.Count == 0) throw new InvalidOperationException("Missing separate physical side geometry.");
            return MakeMesh(vertices.ToArray(), normals.ToArray(), uv.ToArray(), new[] { retained.ToArray(), exposed.ToArray() });
        }

        private static bool IsWorkingTriangle(Vector3[] vertices, int[] triangles, int offset, float middleY, float side)
        {
            float y = (vertices[triangles[offset]].y + vertices[triangles[offset + 1]].y + vertices[triangles[offset + 2]].y) / 3f;
            return (y - middleY) * side >= 0f;
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly Vector3Int a, b;
            public EdgeKey(Vector3 p, Vector3 q)
            {
                var x = Vector3Int.RoundToInt(p * 1000000f); var y = Vector3Int.RoundToInt(q * 1000000f);
                bool order = x.x < y.x || x.x == y.x && (x.y < y.y || x.y == y.y && x.z <= y.z);
                a = order ? x : y; b = order ? y : x;
            }
            public bool Equals(EdgeKey other) => a == other.a && b == other.b;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => unchecked(a.GetHashCode() * 397 ^ b.GetHashCode());
        }
        private sealed class Edge { public int a, b, count; }

        private static Mesh BuildSkin(Mesh source, float side, out int selected)
        {
            var sourceV = source.vertices; var sourceN = source.normals; var sourceUv = source.uv;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var triangles = new List<int>(); var indices = new Dictionary<int, int>();
            var boundary = new Dictionary<EdgeKey, Edge>();
            int[] sourceTriangles = source.triangles;
            selected = 0;
            for (int i = 0; i < sourceTriangles.Length; i += 3)
            {
                if (!IsWorkingTriangle(sourceV, sourceTriangles, i, source.bounds.center.y, side)) continue;
                selected++;
                var face = new int[3];
                for (int k = 0; k < 3; k++)
                {
                    int index = sourceTriangles[i + k];
                    if (!indices.TryGetValue(index, out int copy))
                    {
                        copy = vertices.Count; indices.Add(index, copy);
                        vertices.Add(sourceV[index]); normals.Add(sourceN[index]); uv.Add(sourceUv[index]);
                    }
                    face[k] = copy; triangles.Add(copy);
                }
                for (int k = 0; k < 3; k++)
                {
                    int a = face[k], b = face[(k + 1) % 3];
                    var key = new EdgeKey(vertices[a], vertices[b]);
                    if (boundary.TryGetValue(key, out var edge)) edge.count++;
                    else boundary.Add(key, new Edge { a = a, b = b, count = 1 });
                }
            }
            int count = vertices.Count, outerIndexCount = triangles.Count;
            for (int i = 0; i < count; i++)
            {
                vertices.Add(vertices[i] - normals[i] * .002f); normals.Add(-normals[i]); uv.Add(uv[i]);
            }
            for (int i = 0; i < outerIndexCount; i += 3)
            {
                triangles.Add(triangles[i] + count); triangles.Add(triangles[i + 2] + count); triangles.Add(triangles[i + 1] + count);
            }
            foreach (var edge in boundary.Values.Where(e => e.count == 1))
            {
                triangles.AddRange(new[] { edge.a, edge.a + count, edge.b, edge.b, edge.a + count, edge.b + count });
            }
            return MakeMesh(vertices.ToArray(), normals.ToArray(), uv.ToArray(), new[] { triangles.ToArray() });
        }

        // A spatial lookup into actual supplied flesh-facing UVs, not a guessed
        // solid color or a new painted source texture. Nearest candidates are
        // searched in a bounded 2D grid; no quadratic vertex-to-vertex search.
        private sealed class FleshUvField
        {
            private const int Resolution = 64;
            private readonly List<int>[] cells = new List<int>[Resolution * Resolution];
            private readonly Vector3[] vertices;
            private readonly Vector2[] uv;
            private readonly Bounds bounds;
            public FleshUvField(Mesh source, float face)
            {
                vertices = source.vertices; uv = source.uv; bounds = source.bounds;
                var normals = source.normals;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (normals[i].y * face < .65f) continue;
                    int cell = Cell(vertices[i], bounds);
                    (cells[cell] ??= new List<int>()).Add(i);
                }
                if (cells.All(c => c == null)) throw new InvalidOperationException("No actual source flesh-facing surface.");
            }
            public Vector2 Sample(Vector3 position, Bounds targetBounds)
            {
                float x = Mathf.InverseLerp(targetBounds.min.x, targetBounds.max.x, position.x);
                float z = Mathf.InverseLerp(targetBounds.min.z, targetBounds.max.z, position.z);
                Vector3 target = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, x), 0, Mathf.Lerp(bounds.min.z, bounds.max.z, z));
                int center = Cell(target, bounds), cx = center % Resolution, cz = center / Resolution;
                int best = -1; float distance = float.PositiveInfinity;
                for (int radius = 0; radius < Resolution; radius++)
                {
                    for (int dz = -radius; dz <= radius; dz++) for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (radius > 0 && Mathf.Abs(dx) != radius && Mathf.Abs(dz) != radius) continue;
                        int ix = cx + dx, iz = cz + dz;
                        if (ix < 0 || iz < 0 || ix >= Resolution || iz >= Resolution) continue;
                        var candidates = cells[iz * Resolution + ix]; if (candidates == null) continue;
                        foreach (int index in candidates)
                        {
                            float dd = (vertices[index].x - target.x) * (vertices[index].x - target.x) +
                                       (vertices[index].z - target.z) * (vertices[index].z - target.z);
                            if (dd < distance) { distance = dd; best = index; }
                        }
                    }
                    if (best >= 0 && radius >= 1) return uv[best];
                }
                if (best < 0) throw new InvalidOperationException("Unmapped source flesh UV.");
                return uv[best];
            }
            private static int Cell(Vector3 position, Bounds b)
            {
                int x = Mathf.Clamp((int)(Mathf.InverseLerp(b.min.x, b.max.x, position.x) * Resolution), 0, Resolution - 1);
                int z = Mathf.Clamp((int)(Mathf.InverseLerp(b.min.z, b.max.z, position.z) * Resolution), 0, Resolution - 1);
                return z * Resolution + x;
            }
        }

        private static Mesh MakeMesh(Vector3[] vertices, Vector3[] normals, Vector2[] uv, int[][] submeshes)
        {
            if (vertices.Length > Stage03FlounderAuthoring.MaximumDerivedVertices)
                throw new InvalidOperationException("Two-side derived geometry exceeds the existing vertex ceiling.");
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.subMeshCount = submeshes.Length;
            for (int i = 0; i < submeshes.Length; i++) mesh.SetTriangles(submeshes[i], i, false);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
        }
        private static void SaveDerived(string role, string sourceRole, Mesh mesh, Material[] materials,
            string operation, string fleshRole, int selected, List<PhysicalSideGeometry> reports)
        {
            mesh.name = "MS_" + role;
            string path = MeshPath(role);
            Mesh stored = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (stored == null) { AssetDatabase.CreateAsset(mesh, path); stored = mesh; }
            else
            {
                // Native setters update existing GPU buffers as well as serialized
                // data; CopySerialized alone can leave a stale native mesh after
                // a generator changes topology in the same Editor session.
                stored.Clear(false); stored.indexFormat = mesh.indexFormat;
                stored.vertices = mesh.vertices; stored.normals = mesh.normals;
                stored.uv = mesh.uv; stored.tangents = mesh.tangents;
                stored.subMeshCount = mesh.subMeshCount;
                for (int i = 0; i < mesh.subMeshCount; i++) stored.SetTriangles(mesh.GetTriangles(i), i, false);
                stored.bounds = mesh.bounds; stored.UploadMeshData(false);
                EditorUtility.SetDirty(stored); Object.DestroyImmediate(mesh);
            }
            var preview = EditorSceneManager.NewPreviewScene();
            var root = EditorUtility.CreateGameObjectWithHideFlags("PF_" + role, HideFlags.HideAndDontSave);
            try
            {
                SceneManager.MoveGameObjectToScene(root, preview); root.hideFlags = HideFlags.None;
                root.AddComponent<MeshFilter>().sharedMesh = stored;
                root.AddComponent<MeshRenderer>().sharedMaterials = materials;
                if (PrefabUtility.SaveAsPrefabAsset(root, WrapperPath(role)) == null) throw new InvalidOperationException("Could not save " + role);
            }
            finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
            string sourcePath = Stage03FlounderAuthoring.SourceRoot + "/" + sourceRole + "/" + sourceRole + ".fbx";
            Mesh source = SourceMesh(sourceRole);
            reports.Add(new PhysicalSideGeometry
            {
                role = role, sourceRole = sourceRole, sourcePath = sourcePath,
                sourceDependencyHash = AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
                sourceMeshPath = AssetDatabase.GetAssetPath(source), generatedMeshPath = path, wrapperPath = WrapperPath(role),
                operation = operation, fleshUvSourceRole = fleshRole, sourceVertices = source.vertexCount,
                generatedVertices = stored.vertexCount, generatedTriangles = stored.triangles.Length / 3,
                selectedSurfaceTriangles = selected, sourceSize = source.bounds.size, generatedSize = stored.bounds.size,
                rotationEuler = role == "FrontWholeFillet" ? new Vector3(0, 0, 180) : Vector3.zero,
                sourceUnchanged = false
            });
        }

        private static void BindAssembly()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(Stage03FlounderAuthoring.AssemblyPath);
            try
            {
                BindAssemblyRoot(root);
                if (PrefabUtility.SaveAsPrefabAsset(root, Stage03FlounderAuthoring.AssemblyPath) == null)
                    throw new InvalidOperationException("Could not bind two-side assembly.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Called before the authority's single assembly Save so regeneration can
        // match every named object and preserve serialized file IDs atomically.
        public static void BindAssemblyRoot(GameObject root)
        {
                var view = root.GetComponent<FlounderAssemblyView>();
                // Parts themselves are authored under BodyPivot so the back surface rolls with the body.
                Transform storage = Child(root.transform, "TwoFilletStorage");
                view.skinnedBodyStates = new GameObject[2]; view.skins = new FlounderAssemblyPart[2];
                view.wholeFillets = new FlounderAssemblyPart[2]; view.skinStorageAnchors = new Transform[2];
                view.wholeFilletStorageAnchors = new Transform[2];
                for (int side = 0; side < 2; side++)
                {
                    string prefix = side == 0 ? "Front" : "Back";
                    GameObject body = InstantiateWrapper(prefix + "SkinnedBody", view.bodyPivot);
                    body.transform.localPosition = view.bodyStates[side == 0 ? 1 : 3].transform.localPosition;
                    body.transform.localRotation = Quaternion.identity; body.transform.localScale = Vector3.one;
                    body.SetActive(false); view.skinnedBodyStates[side] = body;
                    Mesh wholeMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(prefix + "WholeFillet"));
                    Mesh bodyMesh = SourceMesh(side == 0 ? "HeadRemovedFish" : "FilletRemoved2");
                    Transform wholeStorage = Child(storage, prefix + "WholeFilletStorage");
                    wholeStorage.localPosition = new Vector3((side == 0 ? 1 : 2) * .65f, -wholeMesh.bounds.min.y, -.35f);
                    wholeStorage.localRotation = Quaternion.identity;
                    Transform skinStorage = Child(storage, prefix + "SkinDiscard");
                    skinStorage.localPosition = new Vector3(-.8f, .02f, side == 0 ? .45f : -.45f);
                    skinStorage.localRotation = Quaternion.identity;
                    view.wholeFilletStorageAnchors[side] = wholeStorage; view.skinStorageAnchors[side] = skinStorage;
                    var whole = NewPart(prefix + "WholeFillet", view.bodyPivot, wholeStorage);
                    float y = side == 0 ? bodyMesh.bounds.max.y - wholeMesh.bounds.max.y : bodyMesh.bounds.min.y + wholeMesh.bounds.max.y;
                    whole.transform.localPosition = view.bodyStates[side == 0 ? 1 : 3].transform.localPosition + Vector3.up * y;
                    whole.transform.localRotation = side == 0 ? Quaternion.identity : Quaternion.AngleAxis(180, Vector3.forward);
                    whole.transform.localScale = Vector3.one;
                    var skin = NewPart(prefix + "Skin", view.bodyPivot, skinStorage);
                    skin.transform.localPosition = view.bodyStates[side == 0 ? 1 : 3].transform.localPosition;
                    skin.transform.localRotation = Quaternion.identity; skin.transform.localScale = Vector3.one;
                    skin.separationDuration = .9f; skin.separationLift = .08f;
                    view.wholeFillets[side] = whole; view.skins[side] = skin;
                }
                EditorUtility.SetDirty(view);
        }
        private static Transform Child(Transform parent, string name)
        {
            var existing = parent.Find(name); if (existing != null) return existing;
            var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene); go.hideFlags = HideFlags.None;
            go.transform.SetParent(parent, false); return go.transform;
        }
        private static GameObject InstantiateWrapper(string role, Transform parent)
        {
            var existing = parent.Find(role); if (existing != null) return existing.gameObject;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPath(role));
            if (source == null) throw new InvalidOperationException("Missing adaptation wrapper " + role);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent); go.name = role;
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            return go;
        }
        private static FlounderAssemblyPart NewPart(string role, Transform parent, Transform storage)
        {
            var go = InstantiateWrapper(role, parent);
            var part = go.GetComponent<FlounderAssemblyPart>(); if (part == null) part = go.AddComponent<FlounderAssemblyPart>();
            part.stableId = role; part.storageAnchor = storage; go.SetActive(false); return part;
        }
        private static void WriteText(string assetPath, string value)
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
            if (!File.Exists(path) || File.ReadAllText(path) != value) File.WriteAllText(path, value, new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
