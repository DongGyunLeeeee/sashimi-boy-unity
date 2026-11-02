using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        private const string Algorithm = "Stage03TwoFillet-2";
        private const int FleshResolution = 1024;
        private const int AtlasGutter = 4;
        public static string WholeFilletPrefabPath(int side) => WrapperPath(side == 0 ? "FrontWholeFillet" : "BackWholeFillet");
        public static string MeshPath(string role) => Root + "/Meshes/MS_" + role + ".asset";
        private static string WrapperPath(string role) => Root + "/Prefabs/PF_" + role + ".prefab";

        [Serializable] public sealed class PhysicalSideGeometry
        {
            public string role, sourceRole, sourcePath, sourceDependencyHash, sourceMeshPath, generatedMeshPath, wrapperPath;
            public string operation, fleshUvSourceRole;
            public string sourceAlbedoPath, sourceAlbedoSha256, bakedAlbedoPath, bakeMethod;
            public string sourcePackedMapPath;
            public float sampledSmoothness;
            public int sourceVertices, generatedVertices, generatedTriangles, selectedSurfaceTriangles;
            public int preservedOriginalFleshTriangles;
            public Vector3 sourceSize, generatedSize, rotationEuler;
            public bool sourceUnchanged;
        }
        [Serializable] public sealed class GeometryReport
        {
            public string algorithm = Algorithm;
            public string[] sourceRoles = { "HeadRemovedFish", "FilletRemoved2", "FrontFillet", "BackFillet" };
            public string sourceUVObservation = "Actual source UV/triangle-area audit: Front +Y is pigmented skin, -Y is flesh; Back both faces are flesh. Original flesh triangles retain their original UV interpolation. Continuous planar flesh albedo is rasterized from actual flesh-facing triangles with barycentric source-UV sampling, never nearest-vertex UV transfer across atlas islands. No source texture is recolored.";
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
            var frontFlesh = BakeFleshSurface(front, "FrontFillet", true);
            var backFlesh = BakeFleshSurface(back, "BackFillet", false);
            var frontAtlas = SaveFrontAtlas(frontFlesh);
            var frontWholeMaterial = SaveFleshMaterial("FrontWholeFillet", SourceMaterial("FrontFillet"), frontAtlas, frontFlesh.smoothness);
            Mesh frontWhole = BuildWholeFillet(front, true, out int preservedFleshTriangles);
            Mesh backWhole = BuildWholeFillet(back, false, out _);
            frontWhole = SaveDerived("FrontWholeFillet", "FrontFillet", frontWhole, new[] { frontWholeMaterial },
                "Rigid longitudinal 180-degree rotation; source shape/aspect retained. Original flesh triangle UVs retain their interpolation on the source-albedo atlas page; former skin underside uses a separately baked continuous flesh page. Page-boundary vertices are split, so no triangle interpolates across the two atlas pages.", "FrontFillet", 0, reports);
            reports[reports.Count - 1].preservedOriginalFleshTriangles = preservedFleshTriangles;
            RecordBake(reports[reports.Count - 1], frontFlesh, AssetDatabase.GetAssetPath(frontAtlas));
            backWhole = SaveDerived("BackWholeFillet", "BackFillet", backWhole, new[] { SourceMaterial("BackFillet") },
                "Existing single flesh fillet, source aspect ratio and surface retained; no duplicate/composite or negative scale.", "BackFillet", 0, reports);

            Mesh frontBody = SourceMesh("HeadRemovedFish"), backBody = SourceMesh("FilletRemoved2");
            foreach (int side in new[] { 0, 1 })
            {
                string prefix = side == 0 ? "Front" : "Back";
                string sourceRole = side == 0 ? "HeadRemovedFish" : "FilletRemoved2";
                Mesh source = side == 0 ? frontBody : backBody;
                var flesh = side == 0 ? frontFlesh : backFlesh;
                var fleshMaterial = SaveFleshMaterial(prefix + "SkinnedBody", SourceMaterial(side == 0 ? "FrontFillet" : "BackFillet"), flesh.texture, flesh.smoothness);
                Mesh body = BuildSkinnedBody(source, side == 0 ? 1f : -1f, out int selected);
                SaveDerived(prefix + "SkinnedBody", sourceRole, body,
                    new[] { SourceMaterial(sourceRole), fleshMaterial },
                    "Original complete body geometry retained. The working hemisphere uses continuous XZ UVs and an albedo rasterized from the supplied flesh-facing triangles; the opposite physical side retains its original material/UVs. No UV coordinates are transferred between unrelated atlas islands.",
                    side == 0 ? "FrontFillet" : "BackFillet", selected, reports);
                RecordBake(reports[reports.Count - 1], flesh, AssetDatabase.GetAssetPath(flesh.texture));
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

        private static Mesh BuildWholeFillet(Mesh source, bool front, out int preservedFleshTriangles)
        {
            preservedFleshTriangles = 0;
            if (!front) return MakeMesh(source.vertices, source.normals, source.uv, new[] { source.triangles });
            var sourceV = source.vertices; var sourceN = source.normals; var sourceUv = source.uv;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var triangles = new List<int>(); var copies = new Dictionary<long, int>();
            var sourceTriangles = source.triangles;
            Bounds rotatedBounds = source.bounds; rotatedBounds.center = new Vector3(-source.bounds.center.x, -source.bounds.center.y, source.bounds.center.z);
            for (int i = 0; i < sourceV.Length; i++)
            {
                vertices.Add(new Vector3(-sourceV[i].x, -sourceV[i].y, sourceV[i].z));
                normals.Add(new Vector3(-sourceN[i].x, -sourceN[i].y, sourceN[i].z));
                uv.Add(AtlasUv(sourceUv[i], false)); copies.Add((long)i * 2, i);
            }
            for (int i = 0; i < sourceTriangles.Length; i += 3)
            {
                int a = sourceTriangles[i], b = sourceTriangles[i + 1], c = sourceTriangles[i + 2];
                bool originalFlesh = (sourceN[a].y + sourceN[b].y + sourceN[c].y) < -.05f;
                if (originalFlesh) preservedFleshTriangles++;
                for (int k = 0; k < 3; k++)
                {
                    int index = sourceTriangles[i + k]; long key = (long)index * 2 + (originalFlesh ? 0 : 1);
                    if (!copies.TryGetValue(key, out int copy))
                    {
                        copy = vertices.Count; copies.Add(key, copy);
                        Vector3 p = sourceV[index]; var rotated = new Vector3(-p.x, -p.y, p.z);
                        vertices.Add(rotated); normals.Add(new Vector3(-sourceN[index].x, -sourceN[index].y, sourceN[index].z));
                        Vector2 t = originalFlesh ? sourceUv[index] : PlanarUv(rotated, rotatedBounds);
                        uv.Add(AtlasUv(t, !originalFlesh));
                    }
                    triangles.Add(copy);
                }
            }
            return MakeMesh(vertices.ToArray(), normals.ToArray(), uv.ToArray(), new[] { triangles.ToArray() });
        }

        private static Mesh BuildSkinnedBody(Mesh source, float side, out int selected)
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
                        uv.Add(PlanarUv(originalV[index], source.bounds));
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

        private static Vector2 PlanarUv(Vector3 p, Bounds bounds) => new Vector2(
            Mathf.InverseLerp(bounds.min.x, bounds.max.x, p.x), Mathf.InverseLerp(bounds.min.z, bounds.max.z, p.z));
        private static Vector2 AtlasUv(Vector2 original, bool projected) => new Vector2(
            ((projected ? FleshResolution : 0) + AtlasGutter + .5f + original.x * (FleshResolution - AtlasGutter * 2 - 1)) / (FleshResolution * 2f),
            (AtlasGutter + .5f + original.y * (FleshResolution - AtlasGutter * 2 - 1)) / FleshResolution);

        private sealed class FleshBake
        {
            public string sourceAlbedoPath, sourceAlbedoSha256;
            public string sourcePackedMapPath;
            public float smoothness;
            public Color32[] sourcePixels, pixels;
            public Texture2D texture;
        }

        private static FleshBake BakeFleshSurface(Mesh source, string role, bool rotateFront)
        {
            var material = SourceMaterial(role);
            var bake = new FleshBake { sourceAlbedoPath = AssetDatabase.GetAssetPath(material.mainTexture) };
            using (var sha = SHA256.Create())
                bake.sourceAlbedoSha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(ProjectFile(bake.sourceAlbedoPath)))).Replace("-", "").ToLowerInvariant();
            bake.sourcePixels = ReadAlbedo(material.mainTexture);
            Texture packedMap = material.GetTexture("_MetallicGlossMap");
            if (packedMap == null) throw new InvalidOperationException("Provided flesh roughness reference is missing: " + role);
            bake.sourcePackedMapPath = AssetDatabase.GetAssetPath(packedMap);
            Color32[] packedPixels = ReadAlbedo(packedMap);
            bake.pixels = new Color32[FleshResolution * FleshResolution];
            var depth = Enumerable.Repeat(float.NegativeInfinity, bake.pixels.Length).ToArray();
            var valid = new bool[bake.pixels.Length];
            var positions = source.vertices; var normals = source.normals; var uv = source.uv; var triangles = source.triangles;
            var smoothness = new List<byte>();
            for (int offset = 0; offset < triangles.Length; offset += 3 * 17)
            {
                int a = triangles[offset], b = triangles[offset + 1], c = triangles[offset + 2];
                float facing = normals[a].y + normals[b].y + normals[c].y;
                if (rotateFront ? facing >= -.3f : facing <= .3f) continue;
                smoothness.Add(SampleAlbedo(packedPixels, (uv[a] + uv[b] + uv[c]) / 3f).a);
            }
            if (smoothness.Count == 0) throw new InvalidOperationException("No provided flesh roughness samples: " + role);
            smoothness.Sort(); bake.smoothness = smoothness[smoothness.Count / 2] / 255f * material.GetFloat("_GlossMapScale");
            packedPixels = null;
            Bounds bounds = source.bounds;
            if (rotateFront) bounds.center = new Vector3(-bounds.center.x, -bounds.center.y, bounds.center.z);
            Vector3 Orient(Vector3 p) => rotateFront ? new Vector3(-p.x, -p.y, p.z) : p;
            for (int offset = 0; offset < triangles.Length; offset += 3)
            {
                int ia = triangles[offset], ib = triangles[offset + 1], ic = triangles[offset + 2];
                float facing = normals[ia].y + normals[ib].y + normals[ic].y;
                if (rotateFront ? facing >= -.3f : facing <= .3f) continue;
                Vector3 a = Orient(positions[ia]), b = Orient(positions[ib]), c = Orient(positions[ic]);
                Vector2 pa = PlanarUv(a, bounds) * (FleshResolution - 1), pb = PlanarUv(b, bounds) * (FleshResolution - 1), pc = PlanarUv(c, bounds) * (FleshResolution - 1);
                float determinant = (pb.y - pc.y) * (pa.x - pc.x) + (pc.x - pb.x) * (pa.y - pc.y);
                if (Mathf.Abs(determinant) < .000001f) continue;
                int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))), 0, FleshResolution - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))), 0, FleshResolution - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y))), 0, FleshResolution - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y))), 0, FleshResolution - 1);
                for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                {
                    float px = x + .5f, py = y + .5f;
                    float wa = ((pb.y - pc.y) * (px - pc.x) + (pc.x - pb.x) * (py - pc.y)) / determinant;
                    float wb = ((pc.y - pa.y) * (px - pc.x) + (pa.x - pc.x) * (py - pc.y)) / determinant;
                    float wc = 1f - wa - wb;
                    if (wa < -.0001f || wb < -.0001f || wc < -.0001f) continue;
                    int index = y * FleshResolution + x; float height = a.y * wa + b.y * wb + c.y * wc;
                    if (height <= depth[index]) continue;
                    Vector2 sourceUv = uv[ia] * wa + uv[ib] * wb + uv[ic] * wc;
                    bake.pixels[index] = SampleAlbedo(bake.sourcePixels, sourceUv); depth[index] = height; valid[index] = true;
                }
            }
            // Fill only uncovered projection pixels from their nearest actual
            // surface texel. No color is painted or averaged across UV islands.
            var queue = new int[valid.Length]; int read = 0, write = 0;
            for (int i = 0; i < valid.Length; i++) if (valid[i]) queue[write++] = i;
            if (write < 64) throw new InvalidOperationException("Insufficient actual flesh surface pixels for " + role);
            while (read < write)
            {
                int index = queue[read++], x = index % FleshResolution, y = index / FleshResolution;
                void Fill(int adjacent)
                {
                    if (valid[adjacent]) return;
                    valid[adjacent] = true; bake.pixels[adjacent] = bake.pixels[index]; queue[write++] = adjacent;
                }
                if (x > 0) Fill(index - 1); if (x + 1 < FleshResolution) Fill(index + 1);
                if (y > 0) Fill(index - FleshResolution); if (y + 1 < FleshResolution) Fill(index + FleshResolution);
            }
            bake.texture = SaveAlbedo(role.Replace("Fillet", "") + "ContinuousFlesh", bake.pixels, FleshResolution, FleshResolution);
            return bake;
        }

        private static Color32 SampleAlbedo(Color32[] pixels, Vector2 uv)
        {
            float x = Mathf.Clamp01(uv.x) * (FleshResolution - 1), y = Mathf.Clamp01(uv.y) * (FleshResolution - 1);
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), x1 = Mathf.Min(x0 + 1, FleshResolution - 1), y1 = Mathf.Min(y0 + 1, FleshResolution - 1);
            Color low = Color.LerpUnclamped(pixels[y0 * FleshResolution + x0], pixels[y0 * FleshResolution + x1], x - x0);
            Color high = Color.LerpUnclamped(pixels[y1 * FleshResolution + x0], pixels[y1 * FleshResolution + x1], x - x0);
            return Color.LerpUnclamped(low, high, y - y0);
        }

        private static Color32[] ReadAlbedo(Texture source)
        {
            if (source == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Continuous source-flesh baking requires a graphics-enabled Editor and provided albedo.");
            RenderTexture previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            RenderTexture target = null; Texture2D readback = null;
            try
            {
                target = RenderTexture.GetTemporary(FleshResolution, FleshResolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear; Graphics.Blit(source, target); RenderTexture.active = target;
                readback = new Texture2D(FleshResolution, FleshResolution, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
                readback.ReadPixels(new Rect(0, 0, FleshResolution, FleshResolution), 0, 0, false); readback.Apply(false, false);
                return readback.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (readback != null) Object.DestroyImmediate(readback); if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Texture2D SaveFrontAtlas(FleshBake bake)
        {
            var pixels = new Color32[FleshResolution * FleshResolution * 2];
            for (int y = 0; y < FleshResolution; y++) for (int x = 0; x < FleshResolution; x++)
            {
                var uv = new Vector2((x - AtlasGutter) / (float)(FleshResolution - AtlasGutter * 2 - 1),
                    (y - AtlasGutter) / (float)(FleshResolution - AtlasGutter * 2 - 1));
                pixels[y * FleshResolution * 2 + x] = SampleAlbedo(bake.sourcePixels, uv);
                pixels[y * FleshResolution * 2 + x + FleshResolution] = SampleAlbedo(bake.pixels, uv);
            }
            return SaveAlbedo("FrontWholeFilletAtlas", pixels, FleshResolution * 2, FleshResolution);
        }

        private static Texture2D SaveAlbedo(string name, Color32[] pixels, int width, int height)
        {
            string path = Root + "/Textures/T_" + name + ".png";
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                texture.SetPixels32(pixels); texture.Apply(false, false); byte[] bytes = texture.EncodeToPNG();
                if (!File.Exists(ProjectFile(path)) || !File.ReadAllBytes(ProjectFile(path)).SequenceEqual(bytes)) File.WriteAllBytes(ProjectFile(path), bytes);
            }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool mipmaps = name != "FrontWholeFilletAtlas";
            if (!importer.sRGBTexture || importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != 2048 ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled != mipmaps)
            {
                importer.sRGBTexture = true; importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = mipmaps; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material SaveFleshMaterial(string role, Material provided, Texture2D texture, float smoothness)
        {
            string path = Root + "/Materials/MAT_" + role + ".mat";
            var generated = new Material(provided) { name = "MAT_" + role, mainTexture = texture };
            generated.mainTextureScale = Vector2.one; generated.mainTextureOffset = Vector2.zero;
            // Source tangent-space maps cannot be reused after planar projection.
            // Keep the actual source vertex normals and a soft flesh highlight.
            generated.SetTexture("_BumpMap", null); generated.DisableKeyword("_NORMALMAP");
            generated.SetTexture("_MetallicGlossMap", null); generated.DisableKeyword("_METALLICGLOSSMAP");
            generated.SetFloat("_Metallic", 0); generated.SetFloat("_Glossiness", Mathf.Clamp01(smoothness));
            var stored = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (stored == null) { AssetDatabase.CreateAsset(generated, path); stored = generated; }
            else { EditorUtility.CopySerialized(generated, stored); Object.DestroyImmediate(generated); EditorUtility.SetDirty(stored); }
            return stored;
        }

        private static void RecordBake(PhysicalSideGeometry report, FleshBake bake, string bakedPath)
        {
            report.sourceAlbedoPath = bake.sourceAlbedoPath; report.sourceAlbedoSha256 = bake.sourceAlbedoSha256;
            report.bakedAlbedoPath = bakedPath;
            report.sourcePackedMapPath = bake.sourcePackedMapPath; report.sampledSmoothness = bake.smoothness;
            report.bakeMethod = "1024px actual flesh-facing triangle XZ rasterization; per-pixel barycentric interpolation of each triangle's original source UV, frontmost surface depth test, nearest-valid surface-texel extension outside the donor silhouette. Continuous planar UVs; derived material excludes incompatible tangent-space source maps and uses the median supplied flesh-facing packed smoothness alpha. Front atlas pages have 4px clamp gutters and texel-centered UV affine transforms; atlas mipmaps are disabled to prevent page bleeding.";
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
        private static Mesh SaveDerived(string role, string sourceRole, Mesh mesh, Material[] materials,
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
            return stored;
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
        private static string ProjectFile(string path) => Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
    }
}
