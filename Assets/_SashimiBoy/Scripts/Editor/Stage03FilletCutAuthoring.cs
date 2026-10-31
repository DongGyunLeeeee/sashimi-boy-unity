using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    /// <summary>
    /// Clips the already-normalized supplied fillets once in the Editor. No
    /// source asset/importer is changed and no mesh Boolean runs during play.
    /// </summary>
    public static class Stage03FilletCutAuthoring
    {
        public const string Root = "Assets/_SashimiBoy/Art/Generated/Stage03FilletCuts";
        private const string PresentationRoot = Stage03FlounderAuthoring.Root;
        private const string Algorithm = "Stage03FilletClip-2";
        private static readonly string[] Roles = { "FrontFillet", "BackFillet" };

        [Serializable] private sealed class Receipt
        {
            public int issue = 87;
            public string algorithm = Algorithm;
            public string contract = "Six equal longitudinal cuts; immutable original coordinate frame; cut normal -Z; original skin UV/normals retained; continuous 0..1 planar cap UV; opaque cap albedo sampled from the supplied SashimiSlice flesh; source bytes/importers untouched.";
            public FleshReference capFleshReference;
            public Entry[] fillets;
        }
        [Serializable] private sealed class FleshReference
        {
            public string materialPath, texturePath, textureSha256, materialDependencyHash, colorSpace;
            public string sampling = "256x256 sRGB readback of the supplied SashimiSlice albedo; component medians of bright warm-flesh pixels; reject dark/neutral atlas regions; no source importer setting changes or invented fallback.";
            public int inspectedPixels, acceptedFleshPixels;
            public Color capColor;
        }
        [Serializable] private sealed class Entry
        {
            public string role, sourceMeshPath, sourceDependencyHash, cachePath;
            public int cutsPerFillet, sourceVertices;
            public int[] remainingVertices, surfaceTriangles, capTriangles;
            public float[] cutPositions;
            public Vector3[] remainingSizes;
            public long generationMilliseconds;
        }

        public static string CachePath(string role) => Root + "/" + role + "Cache.asset";

        [MenuItem("Sashimi Boy/Stage 03/Generate Fillet Cut Caches")]
        public static void BuildBatch()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Meshes");
            EnsureFolder(Root + "/Materials");
            try
            {
            var flesh = ReadFleshReference();
            var entries = new List<Entry>();
            foreach (string role in Roles)
            {
                string wrapperPath = PresentationRoot + "/Prefabs/PF_" + role + ".prefab";
                var wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
                Require(wrapper != null, "Generate the supplied fillet presentation first: " + wrapperPath);
                var sourceFilter = wrapper.GetComponentInChildren<MeshFilter>(true);
                var sourceRenderer = wrapper.GetComponentInChildren<MeshRenderer>(true);
                Require(sourceFilter != null && sourceFilter.sharedMesh != null && sourceRenderer != null && sourceRenderer.sharedMaterial != null,
                    "Fillet wrapper has incomplete geometry/material bindings: " + role);
                Mesh source = sourceFilter.sharedMesh;
                string sourcePath = AssetDatabase.GetAssetPath(source);
                string dependencyBefore = AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var cache = AssetDatabase.LoadAssetAtPath<Stage03FilletCutCache>(CachePath(role));
                if (cache == null)
                {
                    cache = ScriptableObject.CreateInstance<Stage03FilletCutCache>();
                    AssetDatabase.CreateAsset(cache, CachePath(role));
                }
                cache.modelRole = role;
                cache.surfaceMaterial = sourceRenderer.sharedMaterial;
                cache.capMaterial = SaveCapMaterial(flesh.capColor, role);
                cache.remainingMeshes = new Mesh[Stage03FilletCutCache.ApprovedCutsPerFillet + 1];
                cache.remainingMeshes[0] = source;
                cache.cutPositions = new float[Stage03FilletCutCache.ApprovedCutsPerFillet];
                var entry = new Entry
                {
                    role = role, sourceMeshPath = sourcePath, sourceDependencyHash = dependencyBefore,
                    cachePath = CachePath(role), cutsPerFillet = Stage03FilletCutCache.ApprovedCutsPerFillet,
                    sourceVertices = source.vertexCount,
                    remainingVertices = new int[6], surfaceTriangles = new int[6], capTriangles = new int[6], remainingSizes = new Vector3[6]
                };
                for (int cuts = 1; cuts <= Stage03FilletCutCache.ApprovedCutsPerFillet; cuts++)
                {
                    float plane = Mathf.Lerp(source.bounds.min.z, source.bounds.max.z,
                        cuts / (float)Stage03FilletCutCache.ApprovedCutsPerFillet);
                    cache.cutPositions[cuts - 1] = plane;
                    if (cuts == Stage03FilletCutCache.ApprovedCutsPerFillet) continue;
                    Mesh generated = BuildClippedMesh(source, plane);
                    generated.name = "MS_" + role + "_Remaining_" + cuts;
                    cache.remainingMeshes[cuts] = SaveMesh(generated,
                        Root + "/Meshes/" + generated.name + ".asset");
                }
                for (int cuts = 0; cuts < Stage03FilletCutCache.ApprovedCutsPerFillet; cuts++)
                {
                    Mesh mesh = cache.remainingMeshes[cuts];
                    entry.remainingVertices[cuts] = mesh.vertexCount;
                    entry.surfaceTriangles[cuts] = (int)mesh.GetIndexCount(0) / 3;
                    entry.capTriangles[cuts] = mesh.subMeshCount > 1 ? (int)mesh.GetIndexCount(1) / 3 : 0;
                    entry.remainingSizes[cuts] = mesh.bounds.size;
                }
                Require(cache.IsValid, "Incomplete generated fillet cache: " + role);
                Require(dependencyBefore == AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
                    "Generating cut states changed the presentation mesh: " + role);
                EditorUtility.SetDirty(cache);
                AssetDatabase.SaveAssetIfDirty(cache);
                entry.cutPositions = (float[])cache.cutPositions.Clone();
                // Timing is logged, not serialized into deterministic generated data.
                entry.generationMilliseconds = 0;
                entries.Add(entry);
                Debug.Log("[Stage03FilletCuts] " + role + " cached five closed residual meshes in " + watch.ElapsedMilliseconds + " ms; gameplay changes only immutable mesh references.");
            }
            string reportPath = Root + "/Stage03FilletCutReport.json";
            string report = JsonUtility.ToJson(new Receipt { capFleshReference = flesh, fillets = entries.ToArray() }, true) + "\n";
            if (!File.Exists(reportPath) || File.ReadAllText(reportPath) != report) File.WriteAllText(reportPath, report);
            AssetDatabase.ImportAsset(reportPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Stage03FilletCuts] Six cuts per fillet, four independent fillets; cap and edge appearance still require final Player visual review.");
            }
            finally { NormalizeGeneratedText(); }
        }

        /// <summary>Retains z >= cutZ and seals the intersection. Public for deterministic geometry regression tests.</summary>
        public static Mesh BuildClippedMesh(Mesh source, float cutZ)
        {
            if (source == null || !source.isReadable || !float.IsFinite(cutZ) ||
                cutZ <= source.bounds.min.z || cutZ >= source.bounds.max.z)
                throw new ArgumentException("A readable fillet and an interior cut plane are required.");
            return new ClipBuilder(source, cutZ).Build();
        }

        private static FleshReference ReadFleshReference()
        {
            string materialPath = PresentationRoot + "/Materials/MAT_SashimiSlice.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Require(material != null && material.mainTexture != null, "The supplied SashimiSlice flesh reference is missing.");
            string texturePath = AssetDatabase.GetAssetPath(material.mainTexture);
            Require(!string.IsNullOrWhiteSpace(texturePath) && File.Exists(ProjectFile(texturePath)), "The supplied flesh albedo has no readable source file identity.");
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Flesh-reference sampling requires the approved graphics-enabled Editor mode.");
            var reference = new FleshReference
            {
                materialPath = materialPath, texturePath = texturePath,
                materialDependencyHash = AssetDatabase.GetAssetDependencyHash(materialPath).ToString(),
                colorSpace = QualitySettings.activeColorSpace.ToString()
            };
            using (var sha = SHA256.Create())
                reference.textureSha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(ProjectFile(texturePath)))).Replace("-", "").ToLowerInvariant();
            var previousTarget = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null;
            Texture2D readback = null;
            try
            {
                target = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(material.mainTexture, target);
                RenderTexture.active = target;
                readback = new Texture2D(256, 256, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
                readback.ReadPixels(new Rect(0, 0, 256, 256), 0, 0, false);
                readback.Apply(false, false);
                var reds = new List<byte>(); var greens = new List<byte>(); var blues = new List<byte>();
                Color32[] pixels = readback.GetPixels32();
                reference.inspectedPixels = pixels.Length;
                foreach (Color32 pixel in pixels)
                {
                    float r = pixel.r / 255f, g = pixel.g / 255f, b = pixel.b / 255f;
                    float luminance = .2126f * r + .7152f * g + .0722f * b;
                    // Accept the light pink/peach flesh in the provided slice,
                    // excluding black seams, white padding and saturated blood/skin.
                    if (pixel.a < 250 || r < .60f || g < .45f || b < .35f ||
                        r - g < .025f || r - g > .20f || g - b < -.08f || g - b > .15f ||
                        r - b > .35f || luminance < .62f || luminance > .93f) continue;
                    reds.Add(pixel.r); greens.Add(pixel.g); blues.Add(pixel.b);
                }
                Require(reds.Count >= 64, "The supplied SashimiSlice albedo has insufficient actual flesh pixels; no fallback cap color was invented.");
                reds.Sort(); greens.Sort(); blues.Sort();
                int middle = reds.Count / 2;
                reference.acceptedFleshPixels = reds.Count;
                reference.capColor = new Color(reds[middle] / 255f, greens[middle] / 255f, blues[middle] / 255f, 1f);
                return reference;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgbWrite;
                if (readback != null) Object.DestroyImmediate(readback);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Material SaveCapMaterial(Color flesh, string role)
        {
            string path = Root + "/Materials/MAT_" + role + "_CutFace.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Standard");
            Require(shader != null, "The opaque Standard cut-face shader is unavailable.");
            var generated = new Material(shader) { name = "MAT_" + role + "_CutFace", color = flesh };
            // A source exterior atlas is not a cross-section texture: sampling
            // arbitrary islands produces black/white patchwork. Use the actual
            // supplied slice's representative flesh color without that atlas.
            generated.SetTexture("_MainTex", null);
            generated.DisableKeyword("_NORMALMAP");
            generated.DisableKeyword("_METALLICGLOSSMAP");
            generated.DisableKeyword("_ALPHATEST_ON");
            generated.DisableKeyword("_ALPHABLEND_ON");
            generated.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            generated.SetTexture("_BumpMap", null);
            generated.SetTexture("_MetallicGlossMap", null);
            generated.SetFloat("_Mode", 0f);
            generated.SetInt("_SrcBlend", (int)BlendMode.One);
            generated.SetInt("_DstBlend", (int)BlendMode.Zero);
            generated.SetInt("_ZWrite", 1);
            generated.SetFloat("_Metallic", 0f);
            generated.SetFloat("_Glossiness", .26f);
            generated.renderQueue = -1;
            if (material == null) { material = generated; AssetDatabase.CreateAsset(material, path); }
            else { EditorUtility.CopySerialized(generated, material); Object.DestroyImmediate(generated); }
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static Mesh SaveMesh(Mesh generated, string path)
        {
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(generated, path); return generated; }
            EditorUtility.CopySerialized(generated, saved);
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved);
            return saved;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
        private static void NormalizeGeneratedText()
        {
            // Match the presentation generator's text policy without rewriting
            // its assets or any Source file. Unity adds spaces to empty scalars.
            string absoluteRoot = ProjectFile(Root);
            foreach (string path in Directory.GetFiles(absoluteRoot, "*", SearchOption.AllDirectories))
            {
                if (!new[] { ".asset", ".mat", ".meta", ".json" }.Contains(
                    Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
                NormalizeOwnedText(Root + "/" + Path.GetRelativePath(absoluteRoot, path).Replace('\\', '/'));
            }
            NormalizeOwnedText(Root + ".meta");
        }
        private static void NormalizeOwnedText(string path)
        {
            Require(path == Root + ".meta" || path.StartsWith(Root + "/", StringComparison.Ordinal),
                "Fillet normalization is limited to its owned generated assets.");
            string absolute = ProjectFile(path);
            if (!File.Exists(absolute)) return;
            if (Path.GetExtension(path).Equals(".asset", StringComparison.OrdinalIgnoreCase))
            {
                using var input = File.OpenRead(absolute);
                if (input.ReadByte() != '%') return; // Preserve binary mesh payloads byte-for-byte.
            }
            string original = File.ReadAllText(absolute);
            string normalized = string.Join("\n", File.ReadAllLines(absolute).Select(line => line.TrimEnd(' ', '\t'))) + "\n";
            if (original != normalized) File.WriteAllText(absolute, normalized, new System.Text.UTF8Encoding(false));
        }
        private static string ProjectFile(string path) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int A, B;
            public EdgeKey(int a, int b) { A = Mathf.Min(a, b); B = Mathf.Max(a, b); }
            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object other) => other is EdgeKey key && Equals(key);
            public override int GetHashCode() => unchecked(A * 397 ^ B);
        }
        private sealed class BoundaryNode
        {
            public Vector3 Position;
            public readonly List<int> Neighbours = new List<int>(2);
        }

        private sealed class ClipBuilder
        {
            private readonly Mesh source;
            private readonly float plane, weld;
            private readonly Vector3[] positions, normals;
            private readonly Vector2[] uv;
            private readonly int[] triangles;
            private readonly List<Vector3> output = new List<Vector3>();
            private readonly List<Vector3> outputNormals = new List<Vector3>();
            private readonly List<Vector2> outputUv = new List<Vector2>();
            private readonly List<int> skin = new List<int>();
            private readonly List<int> cap = new List<int>();
            private readonly Dictionary<int, int> originalVertices = new Dictionary<int, int>();
            private readonly Dictionary<EdgeKey, int> intersectedEdges = new Dictionary<EdgeKey, int>();
            private readonly Dictionary<int, int> intersectionNodes = new Dictionary<int, int>();
            private readonly Dictionary<Vector2Int, List<int>> boundaryBins = new Dictionary<Vector2Int, List<int>>();
            private readonly HashSet<EdgeKey> boundaryEdges = new HashSet<EdgeKey>();
            private readonly List<BoundaryNode> boundary = new List<BoundaryNode>();

            public ClipBuilder(Mesh source, float plane)
            {
                this.source = source; this.plane = plane;
                positions = source.vertices; normals = source.normals; uv = source.uv; triangles = source.triangles;
                weld = Mathf.Max(source.bounds.size.magnitude * .00001f, .0000001f);
                Require(normals.Length == positions.Length && uv.Length == positions.Length,
                    "Fillet source must retain per-vertex normals and UVs.");
            }

            public Mesh Build()
            {
                var polygon = new int[4]; var crossings = new int[2];
                for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                {
                    int count = 0, crossingCount = 0;
                    for (int edge = 0; edge < 3; edge++)
                    {
                        int a = triangles[triangle + edge], b = triangles[triangle + (edge + 1) % 3];
                        bool aInside = positions[a].z >= plane, bInside = positions[b].z >= plane;
                        if (aInside) polygon[count++] = AddOriginal(a);
                        if (aInside != bInside)
                        {
                            int index = AddIntersection(a, b);
                            polygon[count++] = index;
                            if (crossingCount < 2) crossings[crossingCount++] = intersectionNodes[index];
                        }
                    }
                    for (int i = 1; i + 1 < count; i++) AddSkinTriangle(polygon[0], polygon[i], polygon[i + 1]);
                    if (crossingCount == 2 && crossings[0] != crossings[1])
                    {
                        var edge = new EdgeKey(crossings[0], crossings[1]);
                        if (boundaryEdges.Add(edge))
                        { boundary[edge.A].Neighbours.Add(edge.B); boundary[edge.B].Neighbours.Add(edge.A); }
                    }
                }
                Require(skin.Count > 0 && boundaryEdges.Count > 2, "The fillet plane did not intersect a residual solid.");
                var contours = TraceContours();
                // A hollow source scan may contain nested rings. Sealing the
                // outer ring fills the flesh rather than reproducing a hole.
                contours.Sort((a, b) => Mathf.Abs(Area(b)).CompareTo(Mathf.Abs(Area(a))));
                var outerContours = new List<List<int>>();
                foreach (var contour in contours)
                {
                    Vector3 point = boundary[contour[0]].Position;
                    if (!outerContours.Any(outer => ContainsPoint(outer, new Vector2(point.x, point.y))))
                    { AddCap(contour); outerContours.Add(contour); }
                }
                Require(cap.Count > 0, "No closed fillet cap could be triangulated.");
                var mesh = new Mesh { name = source.name + "_Cut", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(output); mesh.SetNormals(outputNormals); mesh.SetUVs(0, outputUv);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(skin, 0, false); mesh.SetTriangles(cap, 1, false);
                mesh.RecalculateBounds(); mesh.RecalculateTangents();
                return mesh;
            }

            private int AddOriginal(int sourceIndex)
            {
                if (originalVertices.TryGetValue(sourceIndex, out int existing)) return existing;
                int index = output.Count; originalVertices.Add(sourceIndex, index);
                output.Add(positions[sourceIndex]); outputNormals.Add(normals[sourceIndex]); outputUv.Add(uv[sourceIndex]);
                return index;
            }
            private int AddIntersection(int a, int b)
            {
                var edge = new EdgeKey(a, b);
                if (intersectedEdges.TryGetValue(edge, out int existing)) return existing;
                float t = (plane - positions[a].z) / (positions[b].z - positions[a].z);
                Vector3 position = Vector3.LerpUnclamped(positions[a], positions[b], t); position.z = plane;
                int node = FindBoundaryNode(position);
                int index = output.Count;
                output.Add(boundary[node].Position);
                Vector3 normal = Vector3.LerpUnclamped(normals[a], normals[b], t);
                outputNormals.Add(normal.sqrMagnitude > 0f ? normal.normalized : Vector3.up);
                outputUv.Add(Vector2.LerpUnclamped(uv[a], uv[b], t));
                intersectedEdges.Add(edge, index); intersectionNodes.Add(index, node);
                return index;
            }
            private int FindBoundaryNode(Vector3 point)
            {
                var bin = new Vector2Int(Mathf.FloorToInt(point.x / weld), Mathf.FloorToInt(point.y / weld));
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
                {
                    if (!boundaryBins.TryGetValue(bin + new Vector2Int(x, y), out var candidates)) continue;
                    foreach (int candidate in candidates)
                        if ((boundary[candidate].Position - point).sqrMagnitude <= weld * weld) return candidate;
                }
                int index = boundary.Count; boundary.Add(new BoundaryNode { Position = point });
                if (!boundaryBins.TryGetValue(bin, out var bucket)) { bucket = new List<int>(); boundaryBins.Add(bin, bucket); }
                bucket.Add(index); return index;
            }
            private void AddSkinTriangle(int a, int b, int c)
            {
                if (a == b || b == c || c == a || Vector3.Cross(output[b] - output[a], output[c] - output[a]).sqrMagnitude < 1e-20f) return;
                skin.Add(a); skin.Add(b); skin.Add(c);
            }

            private List<List<int>> TraceContours()
            {
                // A supplied scan can have coincident seams and short dangling
                // triangles. Traverse planar faces, rather than requiring every
                // welded node to have degree two or bridging arbitrary gaps.
                foreach (var node in boundary)
                    node.Neighbours.Sort((a, b) =>
                    {
                        Vector3 da = boundary[a].Position - node.Position, db = boundary[b].Position - node.Position;
                        return Mathf.Atan2(da.y, da.x).CompareTo(Mathf.Atan2(db.y, db.x));
                    });
                var used = new HashSet<long>(); var contours = new List<List<int>>();
                foreach (EdgeKey edge in boundaryEdges.OrderBy(e => e.A).ThenBy(e => e.B))
                    for (int direction = 0; direction < 2; direction++)
                {
                    int startA = direction == 0 ? edge.A : edge.B, startB = direction == 0 ? edge.B : edge.A;
                    if (used.Contains(DirectedKey(startA, startB))) continue;
                    int a = startA, b = startB;
                    var loop = new List<int>();
                    for (int guard = 0; guard <= boundaryEdges.Count * 2; guard++)
                    {
                        Require(used.Add(DirectedKey(a, b)), "Fillet cap face reused a directed edge before closing.");
                        loop.Add(a);
                        List<int> neighbours = boundary[b].Neighbours;
                        int reverse = neighbours.IndexOf(a);
                        Require(reverse >= 0, "Fillet section lost an intersection edge.");
                        int next = neighbours[(reverse + neighbours.Count - 1) % neighbours.Count];
                        a = b; b = next;
                        if (a == startA && b == startB) break;
                    }
                    Require(a == startA && b == startB, "Fillet cross-section did not close.");
                    RemoveSpurs(loop);
                    // Clockwise faces are the exposed outer contours. Tree-only
                    // and zero-area scan fragments cannot manufacture a solid.
                    foreach (var simple in SplitRepeatedNodes(loop))
                        if (simple.Count >= 3 && Area(simple) < -weld * weld) contours.Add(simple);
                }
                Require(contours.Count > 0, "Open fillet cross-section at z=" + plane + "; no closed outer flesh contour exists.");
                return contours;
            }
            private static long DirectedKey(int a, int b) => ((long)a << 32) | (uint)b;
            private static IEnumerable<List<int>> SplitRepeatedNodes(List<int> loop)
            {
                var first = new Dictionary<int, int>();
                for (int i = 0; i < loop.Count; i++)
                {
                    if (!first.TryGetValue(loop[i], out int previous)) { first.Add(loop[i], i); continue; }
                    var inner = loop.GetRange(previous, i - previous);
                    var rest = loop.Take(previous).Concat(loop.Skip(i)).ToList();
                    foreach (var part in SplitRepeatedNodes(inner)) yield return part;
                    foreach (var part in SplitRepeatedNodes(rest)) yield return part;
                    yield break;
                }
                yield return loop;
            }
            private static void RemoveSpurs(List<int> loop)
            {
                bool removed;
                do
                {
                    removed = false;
                    for (int i = 0; i < loop.Count && loop.Count > 2; i++)
                    {
                        int previous = loop[(i + loop.Count - 1) % loop.Count], next = loop[(i + 1) % loop.Count];
                        if (loop[i] != next && previous != next) continue;
                        loop.RemoveAt(i); removed = true; break;
                    }
                } while (removed);
            }
            private float Area(List<int> contour)
            {
                double area = 0;
                for (int i = 0; i < contour.Count; i++)
                { var a = boundary[contour[i]].Position; var b = boundary[contour[(i + 1) % contour.Count]].Position; area += (double)a.x * b.y - (double)b.x * a.y; }
                return (float)(area * .5);
            }
            private bool ContainsPoint(List<int> contour, Vector2 p)
            {
                bool inside = false;
                for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
                {
                    var a = boundary[contour[i]].Position; var b = boundary[contour[j]].Position;
                    if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                }
                return inside;
            }
            private void AddCap(List<int> sourceContour)
            {
                var contour = new List<int>(sourceContour);
                if (Area(contour) > 0f) contour.Reverse();
                var capIndex = new Dictionary<int, int>();
                float minX = contour.Min(i => boundary[i].Position.x), maxX = contour.Max(i => boundary[i].Position.x);
                float minY = contour.Min(i => boundary[i].Position.y), maxY = contour.Max(i => boundary[i].Position.y);
                foreach (int node in contour)
                {
                    Vector3 point = boundary[node].Position;
                    capIndex[node] = output.Count; output.Add(point); outputNormals.Add(Vector3.back);
                    outputUv.Add(new Vector2(Mathf.InverseLerp(minX, maxX, point.x),
                        Mathf.InverseLerp(minY, maxY, point.y)));
                }
                int remaining = contour.Count, attempts = 0;
                while (remaining > 2)
                {
                    bool clipped = false;
                    for (int i = 0; i < remaining; i++)
                    {
                        int a = contour[(i + remaining - 1) % remaining], b = contour[i], c = contour[(i + 1) % remaining];
                        Vector3 pa = boundary[a].Position, pb = boundary[b].Position, pc = boundary[c].Position;
                        float cross = Cross(pa, pb, pc);
                        if (Mathf.Abs(cross) < weld * weld * .01f)
                        { contour.RemoveAt(i); remaining--; clipped = true; break; }
                        if (cross >= 0f) continue;
                        bool contains = false;
                        for (int j = 0; j < remaining; j++)
                        {
                            int p = contour[j]; if (p == a || p == b || p == c) continue;
                            Vector3 point = boundary[p].Position;
                            if (Cross(pa, pb, point) < -1e-12f && Cross(pb, pc, point) < -1e-12f && Cross(pc, pa, point) < -1e-12f)
                            { contains = true; break; }
                        }
                        if (contains) continue;
                        cap.Add(capIndex[a]); cap.Add(capIndex[b]); cap.Add(capIndex[c]);
                        contour.RemoveAt(i); remaining--; clipped = true; break;
                    }
                    Require(clipped && ++attempts <= sourceContour.Count, "Non-simple fillet cross-section could not be sealed without overlapping cap triangles.");
                }
            }
            private static float Cross(Vector3 a, Vector3 b, Vector3 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }
    }
}
