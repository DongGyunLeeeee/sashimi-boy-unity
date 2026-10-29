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
    /// <summary>
    /// Issue #73 authority for generated flounder wrappers, materials and assembly.
    /// Does not open/save a game scene or author charts, rewards or slice counts.
    /// Source assets and their importers are read-only. +Z is head, +Y is up.
    /// </summary>
    public static class Stage03FlounderAuthoring
    {
        public const string SourceRoot = "Assets/_SashimiBoy/Art/Source/DayWorld/Fish/Stage03";
        public const string Root = "Assets/_SashimiBoy/Art/Generated/Stage03Flounder";
        public const string AssemblyPath = Root + "/Prefabs/PF_Stage03_FlounderAssembly.prefab";
        public const string ReportPath = Root + "/Stage03FlounderAuthoring.json";
        public const int MaximumDerivedVertices = 200000;
        private const string Algorithm = "Stage03Flounder-2";

        // These are presentation calibrations, not timing, cutting or balance values.
        // Headless bodies share the tail and neck extents; the final spine is narrower.
        // ProbeBatch and the generated report expose all scales for visual review.
        private const float HeadLengthRatio = .20f;
        private const float FilletLengthRatio = .70f;
        private const float SpineWidthRatio = .45f;
        private const float SliceLengthRatio = .12f;
        public static readonly string[] Roles =
        {
            "WholeFish", "HeadRemovedFish", "FilletRemoved1", "FilletRemoved2",
            "FilletRemoved3", "FilletRemoved4", "FishHead", "FrontFillet", "BackFillet", "SashimiSlice"
        };

        [Serializable]
        public sealed class ModelReport
        {
            public string role, sourcePath, sourceDependencyHash, materialPath, wrapperPath;
            public string baseColorPath, normalSourcePath, metallicSourcePath, roughnessSourcePath, suppliedPackedRmPath;
            public string meshPath, generatedMeshSha256, algorithm;
            public int sourceVertices, derivedVertices, sourceTriangles, derivedTriangles, seamVertices;
            public Vector3 importedSize, importedCenter, orientedSize, orientedCenter, positiveScale;
            public Vector3 rotationEuler, translation, derivedSize, derivedCenter;
            public float clusterCell, maximumVertexDisplacement, p99VertexDisplacement, rmsVertexDisplacement;
            public float relativeP99Error, relativeMaximumError, maximumBoundsDeviation;
            public bool sourceUnchanged, visualConfirmationPending;
            public string alignmentReason;
        }

        [Serializable]
        public sealed class AuthoringReport
        {
            public int issue = 73;
            public string algorithm = Algorithm;
            public string coordinateContract = "Head +Z, up +Y, positive scales only; shared body pivot and tail alignment. Back-work states include an authored working-face correction.";
            public string calibrationScope = "Art wrapper calibration, not source units or approved gameplay values. Visual confirmation remains required.";
            public string packedTextureContract = "Standard metallic R = supplied metallic R, smoothness A = 1 - supplied roughness R; supplied rm recorded but not guessed.";
            public Vector3 wholeFootprint;
            public ModelReport[] models;
        }

        [Serializable]
        private sealed class ProbeReport
        {
            public int issue = 73;
            public string note = "Actual imported source bounds and base-color-only previews. No source/importer/generated game asset edits; not a gameplay test PASS.";
            public string[] galleryOrder = Roles;
            public ModelReport[] models;
        }

        private sealed class SourceGeometry : IDisposable
        {
            public string role, path, dependency;
            public Vector3[] positions, normals;
            public Vector2[] uv;
            public int[] triangles;
            public Bounds importedBounds, orientedBounds;
            public Quaternion orientation;
            public void Dispose() { positions = null; normals = null; uv = null; triangles = null; }
        }

        [MenuItem("Sashimi Boy/Stage 03/Generate Flounder Presentation")]
        public static void BuildBatch()
        {
            RequireEditor();
            EnsureFolder(Root);
            EnsureFolder(Root + "/Meshes");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Textures");
            EnsureFolder(Root + "/Prefabs");
            var reports = new List<ModelReport>();
            Vector3 footprint;
            using (var source = ReadSource("WholeFish")) footprint = source.orientedBounds.size;
            Require(footprint.z > 0 && footprint.x > 0 && footprint.y > 0, "WholeFish must have finite nonzero geometry.");

            foreach (string role in Roles)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using (var source = ReadSource(role))
                {
                    var report = Describe(source);
                    report.algorithm = Algorithm;
                    report.materialPath = Root + "/Materials/MAT_" + role + ".mat";
                    report.wrapperPath = WrapperPath(role);
                    report.meshPath = Root + "/Meshes/MS_" + role + ".asset";
                    var size = TargetSize(role, source.orientedBounds.size, footprint);
                    report.positiveScale = Divide(size, source.orientedBounds.size);
                    report.translation = -Vector3.Scale(source.orientedBounds.center, report.positiveScale);
                    report.alignmentReason = AlignmentReason(role);
                    var calibration = Matrix4x4.TRS(report.translation, Quaternion.identity, report.positiveScale)
                                      * Matrix4x4.Rotate(source.orientation);
                    Mesh mesh = LoadOrBuildDerived(source, calibration, report);
                    Material material = BuildMaterial(role, report);
                    SaveWrapper(role, mesh, material);
                    report.sourceUnchanged = source.dependency == AssetDatabase.GetAssetDependencyHash(source.path).ToString();
                    Require(report.sourceUnchanged, "Source dependency changed during generation: " + source.path);
                    report.visualConfirmationPending = true;
                    reports.Add(report);
                    Debug.Log("[Stage03Flounder] " + role + " vertices " + report.sourceVertices + " -> " + report.derivedVertices + "; generation/cache " + watch.ElapsedMilliseconds + " ms.");
                }
            }
            BuildAssembly(footprint);
            var reportFile = new AuthoringReport { wholeFootprint = footprint, models = reports.ToArray() };
            WriteIfDifferent(ReportPath, JsonUtility.ToJson(reportFile, true) + "\n");
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Stage03Flounder] Generated ten source-backed wrappers and one assembly. Visual confirmation remains pending. " + ReportPath);
        }

        // Safe first-import probe: does not require the generated runtime prefab or save a scene.
        public static void ProbeBatch()
        {
            RequireEditor();
            var reports = new List<ModelReport>();
            var images = new List<Texture2D>();
            using (var preview = new PreviewSpace())
            {
                try
                {
                    foreach (string role in Roles)
                    {
                        using (var source = ReadSource(role))
                        {
                            ModelReport report = Describe(source);
                            reports.Add(report);
                            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(source.path);
                            var instance = preview.Instantiate(prefab);
                            Material material = null;
                            try
                            {
                                // Rotate the whole source, preserving the complete imported node hierarchy.
                                instance.transform.rotation = source.orientation * instance.transform.rotation;
                                Bounds bounds = GeometryBounds(instance);
                                instance.transform.position -= bounds.center;
                                material = new Material(RequireStandard()) { color = Color.white };
                                material.mainTexture = SourceTexture(role, "_basecolor");
                                material.SetFloat("_Glossiness", .2f);
                                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                                Texture2D image = preview.Capture(GeometryBounds(instance), 640, 480);
                                images.Add(image);
                                WriteImage(EvidencePath("probe-" + role + ".png"), image);
                            }
                            finally
                            {
                                Object.DestroyImmediate(instance);
                                if (material != null) Object.DestroyImmediate(material);
                            }
                        }
                    }
                    SaveGallery(images, 5, 2, EvidencePath("source-10-grid.png"));
                    File.WriteAllText(EvidencePath("source-probe.json"), JsonUtility.ToJson(new ProbeReport { models = reports.ToArray() }, true));
                }
                finally { foreach (var image in images) Object.DestroyImmediate(image); }
            }
            Debug.Log("[Stage03Flounder] Actual imported source probe captured. " + EvidencePath("source-probe.json"));
        }

        public static void CapturePreviewsBatch()
        {
            RequireEditor();
            var report = JsonUtility.FromJson<AuthoringReport>(File.ReadAllText(ProjectFile(ReportPath)));
            Require(report != null && report.models != null && report.models.Length == 10, "Generate flounder presentation before capturing.");
            var images = new List<Texture2D>();
            using (var preview = new PreviewSpace())
            {
                try
                {
                    foreach (string role in Roles)
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPath(role));
                        Require(asset != null, "Missing generated wrapper: " + role);
                        var instance = preview.Instantiate(asset);
                        try
                        {
                            var image = preview.Capture(GeometryBounds(instance), 640, 480);
                            images.Add(image);
                            WriteImage(EvidencePath("generated-" + role + ".png"), image);
                        }
                        finally { Object.DestroyImmediate(instance); }
                    }
                    SaveGallery(images, 5, 2, EvidencePath("generated-10-grid.png"));
                    CaptureBodyComparison(preview, report.wholeFootprint);
                    CaptureAssemblyTransitions(preview, report.wholeFootprint);
                }
                finally { foreach (var image in images) Object.DestroyImmediate(image); }
            }
            Debug.Log("[Stage03Flounder] Actual preview captures saved; human composition review remains required. " + EvidencePath("generated-10-grid.png"));
        }

        private static string WrapperPath(string role) => Root + "/Prefabs/PF_" + role + ".prefab";
        private static Vector3 TargetSize(string role, Vector3 source, Vector3 whole)
        {
            int bodyIndex = Array.IndexOf(Roles, role);
            if (bodyIndex == 0) return whole;
            if (bodyIndex > 0 && bodyIndex < 5) return new Vector3(whole.x, whole.y, whole.z * (1f - HeadLengthRatio));
            if (role == "FilletRemoved4")
                return new Vector3(whole.x * SpineWidthRatio, Mathf.Min(whole.y, source.y * whole.z * (1f - HeadLengthRatio) / source.z), whole.z * (1f - HeadLengthRatio));
            float length = role == "FishHead" ? whole.z * HeadLengthRatio
                         : role == "SashimiSlice" ? whole.z * SliceLengthRatio : whole.z * FilletLengthRatio;
            // Independent scan parts keep their source aspect ratio under one positive uniform scale.
            return source * (length / source.z);
        }

        private static string AlignmentReason(string role)
        {
            if (Array.IndexOf(Roles, role) < 5)
                return "Independent normalized scans: shared tail/neck anchors account for the removed head, common width/height and positive calibration. Explicit working-face/yaw corrections follow the actual source probe; inspect transition captures.";
            if (role == "FilletRemoved4")
                return "Final remaining spine: common longitudinal extent and pivot; narrower width, no enlargement beyond WholeFish. Thin-axis detail retains source ratio.";
            return "Independent source part: rigid canonical axes, uniform positive scale; artist calibration length ratio recorded in generator. No cut count/timing inferred.";
        }

        private static SourceGeometry ReadSource(string role)
        {
            Require(Roles.Contains(role, StringComparer.Ordinal), "Unknown exact-case source role: " + role);
            string path = SourceRoot + "/" + role + "/" + role + ".fbx";
            Require(Directory.Exists(ProjectFile(SourceRoot + "/" + role)), "Missing exact-case source folder: " + role);
            Require(Directory.GetDirectories(ProjectFile(SourceRoot)).Any(p => Path.GetFileName(p) == role), "Source folder casing changed: " + role);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(asset != null, "Missing imported source model: " + path);
            using var objects = new IsolatedObjects();
            var instance = objects.Instantiate(asset);
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            try
            {
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    Require(mesh != null && mesh.isReadable, "Source mesh must be readable without an importer edit: " + path);
                    Require(mesh.normals.Length == mesh.vertexCount && mesh.uv.Length == mesh.vertexCount,
                        "Source mesh needs complete normals and UV0: " + path);
                    Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                    Require(matrix.determinant > 0, "Source has a reflected transform; do not compensate with negative scale: " + path);
                    var normalMatrix = matrix.inverse.transpose;
                    int offset = positions.Count;
                    Vector3[] v = mesh.vertices, n = mesh.normals;
                    Vector2[] t = mesh.uv;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector3 p = matrix.MultiplyPoint3x4(v[i]);
                        Require(Finite(p) && Finite(n[i]) && Finite(t[i]), "Nonfinite source vertex: " + path);
                        positions.Add(p);
                        normals.Add(normalMatrix.MultiplyVector(n[i]).normalized);
                        uv.Add(t[i]);
                    }
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        Require(mesh.GetTopology(sub) == MeshTopology.Triangles, "Only source triangles are supported: " + path);
                        foreach (int index in mesh.GetTriangles(sub)) triangles.Add(offset + index);
                    }
                }
            }
            finally { Object.DestroyImmediate(instance); }
            Require(positions.Count > 0 && triangles.Count > 0, "Empty source geometry: " + path);
            Bounds imported = BoundsOf(positions);
            Quaternion orientation = CanonicalRotation(role, imported.size);
            Bounds oriented = BoundsOf(positions.Select(p => orientation * p));
            return new SourceGeometry
            {
                role = role, path = path, dependency = AssetDatabase.GetAssetDependencyHash(path).ToString(),
                positions = positions.ToArray(), normals = normals.ToArray(), uv = uv.ToArray(), triangles = triangles.ToArray(),
                importedBounds = imported, orientedBounds = oriented, orientation = orientation
            };
        }

        // Rigid rotation from the actual imported hierarchy, rather than guessing whether FBX -90X
        // was baked by the importer. Longest axis -> +Z; thinnest axis -> +Y. No reflection.
        private static Quaternion CanonicalRotation(string role, Vector3 size)
        {
            Require(size.x > 0 && size.y > 0 && size.z > 0 && Finite(size), "Source bounds must be finite and three-dimensional.");
            int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            int thinnest = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            Require(longest != thinnest, "Ambiguous source axes require an explicit authoring calibration.");
            Vector3 Axis(int index) => index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;
            Quaternion rotation = Quaternion.Inverse(Quaternion.LookRotation(Axis(longest), Axis(thinnest)));
            // Probe1 exposed reversed tails in these independent scans. They are
            // authored for the already-flipped BodyPivot, so their cut face is
            // local -Y and becomes visible +Y during back-side work.
            if (role == "FilletRemoved3")
                rotation = Quaternion.AngleAxis(180f, Vector3.forward) * Quaternion.AngleAxis(180f, Vector3.up) * rotation;
            else if (role == "FilletRemoved4")
                rotation = Quaternion.AngleAxis(180f, Vector3.forward) * Quaternion.AngleAxis(225f, Vector3.up) * rotation;
            return rotation;
        }

        private static ModelReport Describe(SourceGeometry source)
        {
            return new ModelReport
            {
                role = source.role, sourcePath = source.path, sourceDependencyHash = source.dependency,
                sourceVertices = source.positions.Length, sourceTriangles = source.triangles.Length / 3,
                importedSize = source.importedBounds.size, importedCenter = source.importedBounds.center,
                orientedSize = source.orientedBounds.size, orientedCenter = source.orientedBounds.center,
                rotationEuler = source.orientation.eulerAngles, visualConfirmationPending = true
            };
        }

        private struct PositionKey : IEquatable<PositionKey>
        {
            public int x, y, z;
            public PositionKey(Vector3 p, float cell)
            { x = Mathf.RoundToInt(p.x / cell); y = Mathf.RoundToInt(p.y / cell); z = Mathf.RoundToInt(p.z / cell); }
            public bool Equals(PositionKey b) => x == b.x && y == b.y && z == b.z;
            public override bool Equals(object obj) => obj is PositionKey b && Equals(b);
            public override int GetHashCode() { unchecked { return (x * 397 ^ y) * 397 ^ z; } }
        }

        private struct AttributeKey : IEquatable<AttributeKey>
        {
            public int u, v, nx, ny, nz;
            public AttributeKey(Vector2 uv, Vector3 n, float uvPrecision, float normalPrecision)
            {
                u = Mathf.RoundToInt(uv.x * uvPrecision); v = Mathf.RoundToInt(uv.y * uvPrecision);
                nx = Mathf.RoundToInt(n.x * normalPrecision); ny = Mathf.RoundToInt(n.y * normalPrecision); nz = Mathf.RoundToInt(n.z * normalPrecision);
            }
            public bool Equals(AttributeKey b) => u == b.u && v == b.v && nx == b.nx && ny == b.ny && nz == b.nz;
            public override bool Equals(object obj) => obj is AttributeKey b && Equals(b);
            public override int GetHashCode() { unchecked { return ((((u * 397 ^ v) * 397 ^ nx) * 397 ^ ny) * 397 ^ nz); } }
        }

        private struct ClusterKey : IEquatable<ClusterKey>
        {
            public PositionKey position;
            public AttributeKey attributes;
            public bool seam;
            public bool Equals(ClusterKey b) => seam == b.seam && position.Equals(b.position) && attributes.Equals(b.attributes);
            public override bool Equals(object obj) => obj is ClusterKey b && Equals(b);
            public override int GetHashCode() { unchecked { return (position.GetHashCode() * 397 ^ attributes.GetHashCode()) * 397 ^ (seam ? 1 : 0); } }
        }

        private struct TriangleKey : IEquatable<TriangleKey>
        {
            public int a, b, c;
            public TriangleKey(int x, int y, int z)
            {
                // Cyclic duplicates are equivalent; reversed winding remains distinct.
                if (x <= y && x <= z) { a = x; b = y; c = z; }
                else if (y <= z) { a = y; b = z; c = x; }
                else { a = z; b = x; c = y; }
            }
            public bool Equals(TriangleKey t) => a == t.a && b == t.b && c == t.c;
            public override bool Equals(object obj) => obj is TriangleKey t && Equals(t);
            public override int GetHashCode() { unchecked { return (a * 397 ^ b) * 397 ^ c; } }
        }

        private sealed class ClusterData
        {
            public Vector3 normal;
            public Vector2 uv;
            public int count;
            public PositionKey geometry;
        }

        private static Mesh LoadOrBuildDerived(SourceGeometry source, Matrix4x4 matrix, ModelReport report)
        {
            // Each role owns one cached generated mesh. A prior report is a measured cache receipt.
            AuthoringReport previous = File.Exists(ProjectFile(ReportPath))
                ? JsonUtility.FromJson<AuthoringReport>(File.ReadAllText(ProjectFile(ReportPath))) : null;
            var receipt = previous?.models?.FirstOrDefault(r => r.role == source.role);
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(report.meshPath);
            if (saved != null && receipt != null && receipt.algorithm == Algorithm &&
                receipt.sourceDependencyHash == source.dependency && receipt.positiveScale.Equals(report.positiveScale) &&
                receipt.translation.Equals(report.translation) && receipt.rotationEuler.Equals(report.rotationEuler) &&
                saved.vertexCount == receipt.derivedVertices && saved.vertexCount <= MaximumDerivedVertices &&
                receipt.generatedMeshSha256 == FileSha256(report.meshPath))
            {
                CopyMetrics(receipt, report);
                report.generatedMeshSha256 = receipt.generatedMeshSha256;
                return saved;
            }

            Vector3[] points = source.positions.Select(matrix.MultiplyPoint3x4).ToArray();
            var normalMatrix = matrix.inverse.transpose;
            Vector3[] normals = source.normals.Select(n => normalMatrix.MultiplyVector(n).normalized).ToArray();
            Bounds original = BoundsOf(points);
            float length = Mathf.Max(original.size.x, original.size.y, original.size.z);
            // Duplicate positions with differing UV or normal signatures are attribute boundaries.
            // Such signatures are retained, and every render vertex in a spatial cell receives the
            // same representative position, avoiding a crack along the preserved seam duplicates.
            float positionPrecision = length * 0.000001f;
            var firstAttributes = new Dictionary<PositionKey, AttributeKey>();
            var discontinuities = new HashSet<PositionKey>();
            for (int i = 0; i < points.Length; i++)
            {
                var key = new PositionKey(points[i], positionPrecision);
                var attribute = new AttributeKey(source.uv[i], normals[i], 100000f, 1000f);
                if (firstAttributes.TryGetValue(key, out var first))
                { if (!first.Equals(attribute)) discontinuities.Add(key); }
                else firstAttributes.Add(key, attribute);
            }
            var seam = points.Select(p => discontinuities.Contains(new PositionKey(p, positionPrecision))).ToArray();
            report.seamVertices = seam.Count(s => s);
            Debug.Log("[Stage03Flounder] " + source.role + " attribute-boundary vertices " + report.seamVertices + " / " + points.Length);
            firstAttributes.Clear(); discontinuities.Clear();

            Mesh candidate = null;
            int[] map = null;
            float cell = length / 256f;
            try
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    if (candidate != null) Object.DestroyImmediate(candidate);
                    candidate = Cluster(points, normals, source.uv, source.triangles, seam, cell, out map);
                    Debug.Log("[Stage03Flounder] " + source.role + " cluster attempt " + attempt + ": " + candidate.vertexCount + " vertices.");
                    if (candidate.vertexCount <= MaximumDerivedVertices) break;
                    cell *= 1.25f;
                }
                Require(candidate.vertexCount <= MaximumDerivedVertices,
                    source.role + ": preserving source attribute boundaries exceeded the derived vertex budget; no silent seam removal.");
                Vector3[] derived = candidate.vertices;
                var errors = new float[points.Length];
                double sumSquared = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    float error = Vector3.Distance(points[i], derived[map[i]]);
                    errors[i] = error; sumSquared += (double)error * error;
                }
                Array.Sort(errors);
                report.clusterCell = cell;
                report.derivedVertices = candidate.vertexCount;
                report.derivedTriangles = candidate.triangles.Length / 3;
                report.maximumVertexDisplacement = errors[errors.Length - 1];
                report.p99VertexDisplacement = errors[Mathf.Clamp(Mathf.CeilToInt(errors.Length * .99f) - 1, 0, errors.Length - 1)];
                report.rmsVertexDisplacement = (float)Math.Sqrt(sumSquared / errors.Length);
                report.relativeP99Error = report.p99VertexDisplacement / length;
                report.relativeMaximumError = report.maximumVertexDisplacement / length;
                report.maximumBoundsDeviation = Mathf.Max(MaxAbs(candidate.bounds.min - original.min), MaxAbs(candidate.bounds.max - original.max));
                report.derivedSize = candidate.bounds.size; report.derivedCenter = candidate.bounds.center;
                Require(report.relativeP99Error <= .01f && report.relativeMaximumError <= .02f &&
                        report.maximumBoundsDeviation <= length * .01f,
                    source.role + ": measured derived geometry exceeded the 99%-vertices/maximum/bounds error budget.");
                candidate.name = "Stage03_" + source.role;
                if (saved == null)
                {
                    AssetDatabase.CreateAsset(candidate, report.meshPath);
                    saved = candidate; candidate = null;
                }
                else { EditorUtility.CopySerialized(candidate, saved); EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved); }
                report.generatedMeshSha256 = FileSha256(report.meshPath);
                return saved;
            }
            finally { if (candidate != null) Object.DestroyImmediate(candidate); }
        }

        private static Mesh Cluster(Vector3[] points, Vector3[] normals, Vector2[] uv, int[] triangles,
            bool[] seam, float cell, out int[] map)
        {
            var geometry = new Dictionary<PositionKey, (Vector3 sum, int count)>();
            var keys = new Dictionary<ClusterKey, int>();
            var clusters = new List<ClusterData>();
            map = new int[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var spatial = new PositionKey(points[i], cell);
                geometry.TryGetValue(spatial, out var g);
                geometry[spatial] = (g.sum + points[i], g.count + 1);
                var key = new ClusterKey
                {
                    position = spatial, seam = seam[i],
                    attributes = new AttributeKey(uv[i], normals[i], seam[i] ? 100000f : 32f, seam[i] ? 1000f : 4f)
                };
                if (!keys.TryGetValue(key, out int index))
                {
                    index = clusters.Count; keys.Add(key, index); clusters.Add(new ClusterData { geometry = spatial });
                }
                map[i] = index;
                var cluster = clusters[index];
                cluster.normal += normals[i]; cluster.uv += uv[i]; cluster.count++;
            }
            var positions = new List<Vector3>(clusters.Count);
            var averagedNormals = new List<Vector3>(clusters.Count);
            var averagedUv = new List<Vector2>(clusters.Count);
            foreach (var cluster in clusters)
            {
                var g = geometry[cluster.geometry];
                positions.Add(g.sum / g.count); averagedNormals.Add(cluster.normal.normalized); averagedUv.Add(cluster.uv / cluster.count);
            }
            var indices = new List<int>(triangles.Length);
            var seen = new HashSet<TriangleKey>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = map[triangles[i]], b = map[triangles[i + 1]], c = map[triangles[i + 2]];
                if (a == b || b == c || c == a || !seen.Add(new TriangleKey(a, b, c))) continue;
                if (Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]).sqrMagnitude <= 1e-20f) continue;
                indices.Add(a); indices.Add(b); indices.Add(c);
            }
            Require(indices.Count > 0, "Clustering removed every triangle.");
            var result = new Mesh { name = "Stage03FlounderDerived", indexFormat = IndexFormat.UInt32 };
            result.SetVertices(positions); result.SetNormals(averagedNormals); result.SetUVs(0, averagedUv);
            result.SetTriangles(indices, 0); result.RecalculateBounds(); result.RecalculateTangents();
            return result;
        }

        private static void CopyMetrics(ModelReport source, ModelReport target)
        {
            target.derivedVertices = source.derivedVertices; target.derivedTriangles = source.derivedTriangles;
            target.clusterCell = source.clusterCell; target.seamVertices = source.seamVertices;
            target.maximumVertexDisplacement = source.maximumVertexDisplacement; target.p99VertexDisplacement = source.p99VertexDisplacement;
            target.rmsVertexDisplacement = source.rmsVertexDisplacement; target.relativeP99Error = source.relativeP99Error;
            target.relativeMaximumError = source.relativeMaximumError; target.maximumBoundsDeviation = source.maximumBoundsDeviation;
            target.derivedSize = source.derivedSize; target.derivedCenter = source.derivedCenter;
        }

        private static Material BuildMaterial(string role, ModelReport report)
        {
            string baseColor = TexturePath(role, "_basecolor"), normal = TexturePath(role, "_normal"),
                   metallic = TexturePath(role, "_metallic"), roughness = TexturePath(role, "_roughness"), packed = TexturePath(role, "_rm");
            report.baseColorPath = baseColor; report.normalSourcePath = normal; report.metallicSourcePath = metallic;
            report.roughnessSourcePath = roughness; report.suppliedPackedRmPath = packed;
            string normalCopy = Root + "/Textures/N_" + role + Path.GetExtension(normal);
            WriteIfDifferent(normalCopy, File.ReadAllBytes(ProjectFile(normal)));
            AssetDatabase.ImportAsset(normalCopy, ImportAssetOptions.ForceSynchronousImport);
            var normalImporter = (TextureImporter)AssetImporter.GetAtPath(normalCopy);
            if (normalImporter.textureType != TextureImporterType.NormalMap || normalImporter.convertToNormalmap || normalImporter.sRGBTexture)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.convertToNormalmap = false; normalImporter.sRGBTexture = false;
                normalImporter.SaveAndReimport();
            }
            string metalSmooth = Root + "/Textures/MS_" + role + ".png";
            BuildMetallicSmoothness(metallic, roughness, metalSmooth);
            var material = AssetDatabase.LoadAssetAtPath<Material>(report.materialPath);
            if (material == null)
            {
                material = new Material(RequireStandard());
                AssetDatabase.CreateAsset(material, report.materialPath);
            }
            material.shader = RequireStandard(); material.color = Color.white;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(baseColor);
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalCopy));
            material.SetFloat("_BumpScale", 1f); material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(metalSmooth));
            material.SetFloat("_Metallic", 1f); material.SetFloat("_Glossiness", 1f); material.SetFloat("_GlossMapScale", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f); material.EnableKeyword("_METALLICGLOSSMAP");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            Require(material.mainTexture != null && material.GetTexture("_BumpMap") != null && material.GetTexture("_MetallicGlossMap") != null,
                "Generated material has a missing supplied texture: " + role);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static void BuildMetallicSmoothness(string metallicPath, string roughnessPath, string output)
        {
            // Decode copies of the supplied file bytes as linear scalar data. Reading an imported
            // sRGB texture would gamma-decode these values; changing source importer flags is forbidden.
            Texture2D metallic = null, roughness = null, combined = null;
            try
            {
                metallic = ReadScalarImage(metallicPath); roughness = ReadScalarImage(roughnessPath);
                Color32[] m = metallic.GetPixels32(), r = roughness.GetPixels32();
                var pixels = new Color32[m.Length];
                for (int y = 0; y < metallic.height; y++) for (int x = 0; x < metallic.width; x++)
                {
                    int rx = Mathf.Min(roughness.width - 1, x * roughness.width / metallic.width);
                    int ry = Mathf.Min(roughness.height - 1, y * roughness.height / metallic.height);
                    int index = y * metallic.width + x;
                    pixels[index] = new Color32(m[index].r, 0, 0, (byte)(255 - r[ry * roughness.width + rx].r));
                }
                combined = new Texture2D(metallic.width, metallic.height, TextureFormat.RGBA32, false, true);
                combined.SetPixels32(pixels); combined.Apply(false, false);
                WriteIfDifferent(output, combined.EncodeToPNG());
                AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(output);
                if (importer.sRGBTexture || importer.alphaSource != TextureImporterAlphaSource.FromInput ||
                    importer.alphaIsTransparency || importer.textureType != TextureImporterType.Default)
                {
                    importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                if (combined != null) Object.DestroyImmediate(combined);
                if (roughness != null) Object.DestroyImmediate(roughness);
                if (metallic != null) Object.DestroyImmediate(metallic);
            }
        }

        private static Texture2D ReadScalarImage(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (ImageConversion.LoadImage(texture, File.ReadAllBytes(ProjectFile(path)), false)) return texture;
            Object.DestroyImmediate(texture);
            throw new InvalidOperationException("Could not decode the supplied scalar image: " + path);
        }

        private static string TexturePath(string role, string suffix)
        {
            string folder = SourceRoot + "/" + role;
            string[] matches = Directory.GetFiles(ProjectFile(folder)).Where(p =>
                Path.GetFileNameWithoutExtension(p).EndsWith(suffix, StringComparison.Ordinal) &&
                (Path.GetExtension(p).Equals(".JPEG", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p).Equals(".PNG", StringComparison.OrdinalIgnoreCase)))
                .Select(p => (folder + "/" + Path.GetFileName(p)).Replace('\\', '/')).ToArray();
            Require(matches.Length == 1, "Require exactly one supplied texture for " + role + suffix + ", actual " + matches.Length);
            Require(AssetDatabase.LoadAssetAtPath<Texture2D>(matches[0]) != null, "Texture did not import: " + matches[0]);
            return matches[0];
        }
        private static Texture2D SourceTexture(string role, string suffix) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(role, suffix));

        private static void SaveWrapper(string role, Mesh mesh, Material material)
        {
            using var objects = new IsolatedObjects();
            var root = objects.CreateRoot("PF_" + role);
            try
            {
                var volume = Child(root.transform, role, typeof(MeshFilter), typeof(MeshRenderer));
                volume.GetComponent<MeshFilter>().sharedMesh = mesh;
                volume.GetComponent<MeshRenderer>().sharedMaterial = material;
                Require(PrefabUtility.SaveAsPrefabAsset(root, WrapperPath(role)) != null, "Could not save wrapper " + role);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildAssembly(Vector3 whole)
        {
            using var objects = new IsolatedObjects();
            var root = objects.CreateRoot("PF_Stage03_FlounderAssembly");
            try
            {
                var view = root.AddComponent<FlounderAssemblyView>();
                view.bodyPivot = Anchor(root.transform, "BodyPivot", Vector3.up * (whole.y * .5f));
                view.detachedPartsRoot = Anchor(root.transform, "DetachedParts", Vector3.zero);
                view.bodyStates = new GameObject[6];
                for (int i = 0; i < 6; i++)
                {
                    view.bodyStates[i] = WrapperInstance(Roles[i], view.bodyPivot);
                    Bounds bodyBounds = GeometryBounds(view.bodyStates[i]);
                    float heightOffset = (whole.y - bodyBounds.size.y) * .5f;
                    view.bodyStates[i].transform.localPosition = new Vector3(0f,
                        i >= 4 ? heightOffset : -heightOffset,
                        i == 0 ? 0f : -whole.z * HeadLengthRatio * .5f);
                    view.bodyStates[i].SetActive(i == 0);
                }
                view.headStorageAnchor = Anchor(root.transform, "Storage_Head", new Vector3(-whole.x * 1.1f, whole.y * .6f, whole.z * .55f));
                view.filletStorageAnchors = new Transform[4];
                view.fillets = new FlounderAssemblyPart[4];
                view.head = Part("FishHead", "Head", view.bodyPivot, new Vector3(0, 0, whole.z * .4f), view.headStorageAnchor);
                for (int i = 0; i < 4; i++)
                {
                    bool front = i < 2;
                    string id = (front ? "FrontFillet" : "BackFillet") + (i % 2);
                    float side = i % 2 == 0 ? -.25f : .25f;
                    view.filletStorageAnchors[i] = Anchor(root.transform, "Storage_" + id,
                        new Vector3(whole.x * (1.15f + (i % 2) * .65f), whole.y * .5f, whole.z * (front ? .25f : -.25f)));
                    // Back fillets sit below the initial plane and rotate up with BodyPivot.
                    view.fillets[i] = Part(front ? "FrontFillet" : "BackFillet", id, view.bodyPivot,
                        new Vector3(whole.x * side, whole.y * (front ? .25f : -.25f), -whole.z * .06f), view.filletStorageAnchors[i]);
                }
                view.filletWorkAnchor = Anchor(root.transform, "FilletWork", new Vector3(0, whole.y * .65f, 0));
                view.knifeAttachmentAnchor = Anchor(root.transform, "KnifeAttachment", new Vector3(whole.x * .2f, whole.y, whole.z * .3f));
                view.handAttachmentAnchor = Anchor(root.transform, "HandAttachment", new Vector3(-whole.x * .2f, whole.y, -whole.z * .1f));
                view.sashimiOutputAnchor = Anchor(root.transform, "SashimiOutput", new Vector3(whole.x * .65f, whole.y * .5f, -whole.z * .1f));
                view.plateOutputAnchor = Anchor(root.transform, "PlateOutput", new Vector3(-whole.x * 1.4f, 0, -whole.z * .2f));
                view.sashimiSlicePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPath("SashimiSlice"));
                Require(PrefabUtility.SaveAsPrefabAsset(root, AssemblyPath) != null, "Could not save flounder assembly.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static FlounderAssemblyPart Part(string role, string id, Transform parent, Vector3 position, Transform storage)
        {
            var instance = WrapperInstance(role, parent);
            instance.name = id; instance.transform.localPosition = position;
            var part = instance.AddComponent<FlounderAssemblyPart>();
            part.stableId = id; part.storageAnchor = storage;
            instance.SetActive(false);
            return part;
        }

        private static GameObject WrapperInstance(string role, Transform parent)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(WrapperPath(role));
            Require(source != null, "Missing generated wrapper " + role);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.transform.localPosition = Vector3.zero; instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }
        private static Transform Anchor(Transform parent, string name, Vector3 position)
        {
            var value = Child(parent, name).transform;
            value.localPosition = position;
            return value;
        }

        private static GameObject Child(Transform parent, string name, params Type[] components)
        {
            var child = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
            child.transform.SetParent(parent, false);
            child.hideFlags = HideFlags.None;
            return child;
        }

        private static void CaptureBodyComparison(PreviewSpace preview, Vector3 whole)
        {
            var root = preview.CreateRoot("BodyStateComparison");
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    var instance = WrapperInstance(Roles[i], root.transform);
                    instance.transform.localPosition = new Vector3((i % 3 - 1) * whole.x * 1.4f, 0, (i / 3) * whole.z * 1.35f);
                }
                Texture2D image = preview.Capture(GeometryBounds(root), 1500, 1100, true);
                try { WriteImage(EvidencePath("body-state-shared-footprint.png"), image); }
                finally { Object.DestroyImmediate(image); }
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void CaptureAssemblyTransitions(PreviewSpace preview, Vector3 whole)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(AssemblyPath);
            Require(asset != null, "Missing generated assembly.");
            var root = preview.Instantiate(asset);
            try
            {
                var view = root.GetComponent<FlounderAssemblyView>();
                var frame = new Bounds(new Vector3(0, whole.y * .5f, 0), whole * 1.25f);
                void Capture(string id)
                {
                    Texture2D image = preview.Capture(frame, 1000, 800);
                    try { WriteImage(EvidencePath("transition-" + id + ".png"), image); }
                    finally { Object.DestroyImmediate(image); }
                }
                view.ResetAssembly(); Capture("0-WholeFish");
                Require(!view.TryTurnOver(), "Presentation exposed turn-over before front fillets.");
                Require(view.TryRemoveHead(), "Head presentation rejected valid sequence."); view.AdvancePresentation(5f); Capture("1-HeadRemovedFish");
                Require(view.TrySeparateFillet(0), "First fillet presentation rejected."); view.AdvancePresentation(5f); Capture("2-FilletRemoved1");
                Require(view.TrySeparateFillet(1), "Second fillet presentation rejected."); view.AdvancePresentation(5f); Capture("3-FilletRemoved2-front");
                Require(view.TryTurnOver(), "Turn-over presentation rejected after both front fillets."); view.AdvancePresentation(5f); Capture("4-FilletRemoved2-back");
                Require(view.TrySeparateFillet(2), "Third fillet presentation rejected."); view.AdvancePresentation(5f); Capture("5-FilletRemoved3");
                Require(view.TrySeparateFillet(3), "Fourth fillet presentation rejected.");
                Capture("6-FilletRemoved4-separating");
                view.AdvancePresentation(5f);
                // Keep the completed stored-parts state in frame as a separate capture.
                Texture2D stored = preview.Capture(GeometryBounds(root), 1200, 900);
                try { WriteImage(EvidencePath("transition-7-all-parts-stored.png"), stored); }
                finally { Object.DestroyImmediate(stored); }
                view.ResetAssembly(); Capture("8-reset-WholeFish");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // Camera.scene isolates only this preview scene (Unity's documented preview-scene API).
        // Never switch the active scene, mutate RenderSettings, or save/reopen user scenes.
        private sealed class IsolatedObjects : IDisposable
        {
            public readonly Scene scene;
            public IsolatedObjects() { scene = EditorSceneManager.NewPreviewScene(); }
            public GameObject CreateRoot(string name, params Type[] components)
            {
                var root = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
                try { SceneManager.MoveGameObjectToScene(root, scene); root.hideFlags = HideFlags.None; return root; }
                catch { Object.DestroyImmediate(root); throw; }
            }
            public GameObject Instantiate(GameObject asset)
            {
                Require(asset != null, "Cannot instantiate a missing preview source.");
                return (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            }
            public void Dispose() { if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); }
        }

        private sealed class PreviewSpace : IDisposable
        {
            private readonly IsolatedObjects objects;
            private readonly Camera camera;
            private readonly SceneHandle[] originalHandles;
            private readonly bool[] originalDirty;
            private readonly SceneHandle originalActive;
            public PreviewSpace()
            {
                originalHandles = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).handle).ToArray();
                originalDirty = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).isDirty).ToArray();
                originalActive = SceneManager.GetActiveScene().handle;
                objects = new IsolatedObjects();
                try
                {
                    var cameraObject = objects.CreateRoot("Stage03PreviewCamera", typeof(Camera));
                    camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.scene = objects.scene;
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.105f, .13f, .15f);
                    camera.orthographic = true; camera.nearClipPlane = .001f; camera.farClipPlane = 10000f;
                    AddLight("Key", Quaternion.Euler(45, -35, 0), .7f);
                    AddLight("Fill", Quaternion.Euler(65, 150, 0), .2f);
                }
                catch { objects.Dispose(); throw; }
            }
            public GameObject Instantiate(GameObject asset) => objects.Instantiate(asset);
            public GameObject CreateRoot(string name) => objects.CreateRoot(name);
            private void AddLight(string name, Quaternion rotation, float intensity)
            {
                var root = objects.CreateRoot(name, typeof(Light)); root.transform.rotation = rotation;
                var light = root.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = intensity; light.shadows = LightShadows.None;
            }
            public Texture2D Capture(Bounds bounds, int width, int height, bool overhead = false)
            {
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture previousTarget = camera.targetTexture;
                bool previousSrgbWrite = GL.sRGBWrite;
                RenderTexture target = null;
                Texture2D result = null;
                try
                {
                    float radius = Mathf.Max(bounds.size.magnitude * .5f, .01f);
                    Vector3 direction = overhead ? new Vector3(0, 1, -.001f) : new Vector3(.85f, 1.35f, -1.1f);
                    camera.transform.position = bounds.center + direction.normalized * radius * 4f;
                    camera.transform.LookAt(bounds.center);
                    camera.aspect = (float)width / height;
                    Vector3[] corners = BoundsCorners(bounds);
                    Vector3 localMin = Vector3.one * float.MaxValue, localMax = Vector3.one * float.MinValue;
                    foreach (var corner in corners)
                    {
                        Vector3 p = camera.transform.InverseTransformPoint(corner);
                        localMin = Vector3.Min(localMin, p); localMax = Vector3.Max(localMax, p);
                    }
                    camera.orthographicSize = Mathf.Max((localMax.y - localMin.y) * .5f, (localMax.x - localMin.x) * .5f / camera.aspect) * 1.15f;
                    target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                    target.Create(); camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                    result = new Texture2D(width, height, TextureFormat.RGB24, false);
                    result.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); result.Apply(false, false);
                    var value = result; result = null; return value;
                }
                finally
                {
                    camera.targetTexture = previousTarget; RenderTexture.active = previousActive; GL.sRGBWrite = previousSrgbWrite;
                    if (result != null) Object.DestroyImmediate(result);
                    if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                }
            }
            public void Dispose()
            {
                try { objects.Dispose(); }
                finally
                {
                    Require(SceneManager.GetActiveScene().handle == originalActive && SceneManager.sceneCount == originalHandles.Length,
                        "Preview changed the user's loaded/active scenes; no restore/discard was attempted.");
                    for (int i = 0; i < originalHandles.Length; i++)
                    {
                        Scene current = SceneManager.GetSceneAt(i);
                        Require(current.handle == originalHandles[i] && current.isDirty == originalDirty[i],
                            "Preview changed an existing scene or its unsaved state; no restore/discard was attempted.");
                    }
                }
            }
        }

        private static Vector3[] BoundsCorners(Bounds bounds)
        {
            var result = new Vector3[8]; int index = 0;
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
                result[index++] = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            return result;
        }
        private static Bounds GeometryBounds(GameObject root)
        {
            bool any = false; var bounds = new Bounds();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(false))
            {
                if (filter.sharedMesh == null) continue;
                foreach (var corner in BoundsCorners(filter.sharedMesh.bounds))
                {
                    Vector3 p = filter.transform.TransformPoint(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
                }
            }
            Require(any, "Preview object has no active mesh geometry: " + root.name);
            return bounds;
        }
        private static Bounds BoundsOf(IEnumerable<Vector3> points)
        {
            bool any = false; var result = new Bounds();
            foreach (Vector3 p in points) { if (!any) { result = new Bounds(p, Vector3.zero); any = true; } else result.Encapsulate(p); }
            Require(any, "Cannot measure empty geometry."); return result;
        }
        private static void SaveGallery(IReadOnlyList<Texture2D> tiles, int columns, int rows, string path)
        {
            Require(tiles.Count == columns * rows && tiles.Count > 0, "Gallery dimensions do not match source roles.");
            int width = tiles[0].width, height = tiles[0].height;
            var gallery = new Texture2D(width * columns, height * rows, TextureFormat.RGB24, false);
            try
            {
                for (int i = 0; i < tiles.Count; i++)
                    gallery.SetPixels((i % columns) * width, (rows - 1 - i / columns) * height, width, height, tiles[i].GetPixels());
                gallery.Apply(false, false); WriteImage(path, gallery);
            }
            finally { Object.DestroyImmediate(gallery); }
        }
        private static void WriteImage(string path, Texture2D image) => File.WriteAllBytes(path, image.EncodeToPNG());
        private static string EvidencePath(string file)
        {
            string root = Environment.GetEnvironmentVariable("SASHIMI_BOY_STAGE03_EVIDENCE_ROOT");
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Path.GetTempPath(), "SashimiBoyIssue73", "UnityEvidence");
            Require(Path.IsPathRooted(root), "Evidence root must be an absolute external path.");
            root = Path.GetFullPath(root);
            string project = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath)) + Path.DirectorySeparatorChar;
            Require(!root.StartsWith(project, StringComparison.OrdinalIgnoreCase), "Preview evidence must remain outside the checkout.");
            Directory.CreateDirectory(root); return Path.Combine(root, file);
        }
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent); Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1))), "Could not create generated folder " + folder);
        }
        private static Shader RequireStandard()
        {
            Shader shader = Shader.Find("Standard"); Require(shader != null, "Built-in Standard shader is required."); return shader;
        }
        private static void WriteIfDifferent(string path, string text)
        { path = ProjectFile(path); if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text, new System.Text.UTF8Encoding(false)); }
        private static void WriteIfDifferent(string path, byte[] bytes)
        { path = ProjectFile(path); if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes)) File.WriteAllBytes(path, bytes); }
        private static string ProjectFile(string path) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
        private static string FileSha256(string path)
        {
            using var input = File.OpenRead(ProjectFile(path));
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
        private static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
        private static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y);
        private static void RequireEditor() => Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before flounder authoring.");
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
