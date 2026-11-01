using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SashimiBoy.Tests
{
    public sealed class Stage03FilletCutEditModeTests
    {
        private const string Generator = "SashimiBoy.EditorTools.Stage03FilletCutAuthoring";
        private const string Root = "Assets/_SashimiBoy/Art/Generated/Stage03FilletCuts";

        [Test]
        public void ClippingKeepsTheSuppliedShapeAndSealsAConcaveCrossSection()
        {
            Mesh original = ConcavePrism(), clipped = null;
            try
            {
                Vector3[] beforeVertices = original.vertices;
                int[] beforeTriangles = original.triangles;
                Vector2[] beforeUv = original.uv;
                clipped = (Mesh)RuntimeReflection.InvokeStatic(Generator, "BuildClippedMesh", original, 1f);
                Assert.That(original.vertices, Is.EqualTo(beforeVertices));
                Assert.That(original.triangles, Is.EqualTo(beforeTriangles));
                Assert.That(original.uv, Is.EqualTo(beforeUv));
                Assert.That(clipped.subMeshCount, Is.EqualTo(2));
                Assert.That(clipped.GetTriangles(0).Length, Is.GreaterThan(0));
                int[] cap = clipped.GetTriangles(1);
                Assert.That(cap.Length, Is.GreaterThan(0));
                Vector3[] vertices = clipped.vertices, normals = clipped.normals;
                Assert.That(vertices.All(p => p.z >= 1f - .00001f), Is.True);
                Assert.That(clipped.bounds.min.z, Is.EqualTo(1f).Within(.00001f));
                Assert.That(clipped.bounds.max.z, Is.EqualTo(2f).Within(.00001f));
                float area = 0f;
                for (int i = 0; i < cap.Length; i += 3)
                {
                    Vector3 a = vertices[cap[i]], b = vertices[cap[i + 1]], c = vertices[cap[i + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    Assert.That(cross.z, Is.LessThan(0f), "The retained +Z solid needs an outward -Z cap.");
                    area += cross.magnitude * .5f;
                    foreach (int index in new[] { cap[i], cap[i + 1], cap[i + 2] })
                    {
                        Assert.That(vertices[index].z, Is.EqualTo(1f).Within(.00001f));
                        Assert.That(normals[index], Is.EqualTo(Vector3.back));
                    }
                }
                Assert.That(area, Is.EqualTo(3f).Within(.00001f), "A convex hull would cover the L-shaped source's missing corner.");
                Vector2[] clippedUv = clipped.uv;
                foreach (int index in clipped.GetTriangles(0).Distinct())
                {
                    if (Mathf.Abs(vertices[index].z - 2f) > .00001f) continue;
                    int sourceIndex = Array.FindIndex(beforeVertices, p => Vector3.Distance(p, vertices[index]) < .00001f);
                    Assert.That(sourceIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(Vector2.Distance(clippedUv[index], beforeUv[sourceIndex]), Is.LessThan(.00001f), "Retained exterior vertices keep their original atlas coordinates.");
                }
            }
            finally { if (clipped != null) Object.DestroyImmediate(clipped); Object.DestroyImmediate(original); }
        }

        [Test]
        public void TrueCrossingScanEdges_AreSplitIntoTheirActualLobesWithoutAHullOrOverlappingCaps()
        {
            // Two triangular lobes meet at (1,1). The measured scan outline
            // crosses itself: a convex hull would incorrectly cover area four,
            // while skipping the troublesome crossing would leave an open cap.
            var contour=new[]{new Vector2(0,0),new Vector2(2,2),new Vector2(0,2),new Vector2(2,0)};
            Mesh original=CrossedScanPrism(contour),clipped=null;
            Vector3[] before=original.vertices;
            try
            {
                clipped=(Mesh)RuntimeReflection.InvokeStatic(Generator,"BuildClippedMesh",original,1f);
                Assert.That(original.vertices,Is.EqualTo(before));
                var vertices=clipped.vertices;int[] cap=clipped.GetTriangles(1);double area=0d;
                Assert.That(cap.Length,Is.GreaterThan(0));
                Assert.That(cap.Any(index=>Vector3.Distance(vertices[index],new Vector3(1,1,1))<.00001f),Is.True,
                    "A true crossing must become a shared measured intersection vertex.");
                for(int i=0;i<cap.Length;i+=3)
                {
                    Vector3 a=vertices[cap[i]],b=vertices[cap[i+1]],c=vertices[cap[i+2]];
                    Vector3 normal=Vector3.Cross(b-a,c-a);
                    Assert.That(normal.z,Is.LessThan(0f));area-=normal.z*.5d;
                    Vector3 midpoint=(a+b+c)/3f;
                    bool lower=midpoint.y<=1f&&midpoint.x>=midpoint.y-.00001f&&midpoint.x<=2f-midpoint.y+.00001f;
                    bool upper=midpoint.y>=1f&&midpoint.x>=2f-midpoint.y-.00001f&&midpoint.x<=midpoint.y+.00001f;
                    Assert.That(lower||upper,Is.True,"Cap triangles must stay inside the two actual source lobes.");
                }
                Assert.That(area,Is.EqualTo(2d).Within(.00001d),"The cap must cover each measured lobe once, preserving the missing side regions.");
            }
            finally{if(clipped!=null)Object.DestroyImmediate(clipped);Object.DestroyImmediate(original);}
        }

        [TestCase(0f)]
        [TestCase(37f)]
        [TestCase(90f)]
        [TestCase(173f)]
        public void UnequalLobesSharingACrossingVertex_BothRemainSealedUnderRotation(float degrees)
        {
            // Upper lobe area three, lower lobe area two. Both start at the
            // measured crossing O=(2,0): boundary membership in the first lobe
            // must not cause the second lobe to disappear as a false inner ring.
            var contour=new[]{new Vector2(0,0),new Vector2(4,0),new Vector2(2,3),new Vector2(2,-2)};
            Quaternion rotation=Quaternion.AngleAxis(degrees,Vector3.forward),inverse=Quaternion.Inverse(rotation);
            var rotated=contour.Select(point=>(Vector2)(rotation*new Vector3(point.x,point.y,0))).ToArray();
            Mesh original=CrossedScanPrism(rotated),clipped=null;
            try
            {
                clipped=(Mesh)RuntimeReflection.InvokeStatic(Generator,"BuildClippedMesh",original,1f);
                Vector3[] vertices=clipped.vertices;int[] triangles=clipped.GetTriangles(1);
                double upperArea=0d,lowerArea=0d;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    Vector3 a=vertices[triangles[i]],b=vertices[triangles[i+1]],c=vertices[triangles[i+2]];
                    float cross=Vector3.Cross(b-a,c-a).z;Assert.That(cross,Is.LessThan(0f));
                    Vector3 center=inverse*((a+b+c)/3f);double area=-cross*.5d;
                    bool upper=center.y>=-.0001f&&center.x>=2f-.0001f&&center.y<=1.5f*(4f-center.x)+.0001f;
                    bool lower=center.y<=.0001f&&center.x<=2f+.0001f&&center.y>=-center.x-.0001f;
                    Assert.That(upper||lower,Is.True,"Each cap triangle must stay within one measured lobe.");
                    if(center.y>0)upperArea+=area;else lowerArea+=area;
                }
                Assert.That(upperArea,Is.EqualTo(3d).Within(.0001d));
                Assert.That(lowerArea,Is.EqualTo(2d).Within(.0001d));
                Assert.That(upperArea+lowerArea,Is.EqualTo(5d).Within(.0001d),"A shared boundary point cannot discard the smaller lobe.");
            }
            finally{if(clipped!=null)Object.DestroyImmediate(clipped);Object.DestroyImmediate(original);}
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActualNestedScanRing_IsFilledOnlyOnce_EvenWhenItTouchesTheOuterBoundary(bool touching)
        {
            var outerPoints=new[]{new Vector2(-2,-2),new Vector2(2,-2),new Vector2(2,2),new Vector2(-2,2)};
            var innerPoints=touching?new[]{new Vector2(-2,-2),new Vector2(0,-1),new Vector2(-1,0)}:
                new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)};
            Mesh outer=CrossedScanPrism(outerPoints),inner=CrossedScanPrism(innerPoints),source=new Mesh(),clipped=null;
            try
            {
                source.CombineMeshes(new[]{new CombineInstance{mesh=outer},new CombineInstance{mesh=inner}},true,false);
                source.RecalculateBounds();
                clipped=(Mesh)RuntimeReflection.InvokeStatic(Generator,"BuildClippedMesh",source,1f);
                Vector3[] vertices=clipped.vertices;int[] triangles=clipped.GetTriangles(1);double area=0d;
                for(int i=0;i<triangles.Length;i+=3)
                    area-=Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).z*.5d;
                Assert.That(area,Is.EqualTo(16d).Within(.0001d),"An actual nested ring is flesh, not an additional overlapping cap.");
            }
            finally
            {if(clipped!=null)Object.DestroyImmediate(clipped);Object.DestroyImmediate(source);Object.DestroyImmediate(inner);Object.DestroyImmediate(outer);}
        }

        [Test]
        public void BothWholeSideCachesHaveElevenClosedResidualStatesAndAnEmptyTwelfthState()
        {
            foreach (string role in new[] { "FrontFillet", "BackFillet" })
            {
                var cache = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Root + "/" + role + "Cache.asset");
                Assert.That(cache, Is.Not.Null, "Run Stage03FilletCutAuthoring.BuildBatch before validation.");
                Assert.That(Property<bool>(cache, "IsValid"), Is.True, role);
                var meshes = (Mesh[])RuntimeReflection.GetField(cache, "remainingMeshes");
                var planes = (float[])RuntimeReflection.GetField(cache, "cutPositions");
                Assert.That(meshes.Length, Is.EqualTo(13)); Assert.That(meshes[12], Is.Null);
                Assert.That(AssetDatabase.GetAssetPath(meshes[0]), Is.EqualTo(
                    "Assets/_SashimiBoy/Art/Generated/Stage03Flounder/Meshes/MS_" + role.Replace("Fillet","WholeFillet") + ".asset"));
                for (int cut = 1; cut < 12; cut++)
                {
                    Mesh mesh = meshes[cut];
                    Assert.That(mesh, Is.Not.Null, role + " cut " + cut);
                    Assert.That(mesh.subMeshCount, Is.EqualTo(2));
                    Assert.That(mesh.GetIndexCount(0), Is.GreaterThan(0));
                    Assert.That(mesh.GetIndexCount(1), Is.GreaterThan(0));
                    Assert.That(mesh.bounds.min.z, Is.EqualTo(planes[cut - 1]).Within(.00002f));
                    Assert.That(mesh.bounds.max.z, Is.EqualTo(meshes[0].bounds.max.z).Within(.00002f));
                    Assert.That(mesh.bounds.size.z, Is.LessThan(meshes[cut - 1].bounds.size.z));
                    Assert.That(mesh.vertexCount, Is.LessThan(meshes[0].vertexCount * 2));
                    Vector3[] positions = mesh.vertices, normals = mesh.normals;
                    int[] capVertices = mesh.GetTriangles(1).Distinct().ToArray();
                    foreach (int index in capVertices)
                    {
                        Assert.That(positions[index].z, Is.EqualTo(planes[cut - 1]).Within(.00002f));
                        Assert.That(normals[index].z, Is.LessThan(-.99f));
                    }
                    Vector2[] uv = mesh.uv;
                    Assert.That(capVertices.Min(index => uv[index].x), Is.EqualTo(0f).Within(.0001f));
                    Assert.That(capVertices.Max(index => uv[index].x), Is.EqualTo(1f).Within(.0001f));
                    Assert.That(capVertices.Min(index => uv[index].y), Is.EqualTo(0f).Within(.0001f));
                    Assert.That(capVertices.Max(index => uv[index].y), Is.EqualTo(1f).Within(.0001f));
                    Assert.That(capVertices.All(index => uv[index].x >= 0f && uv[index].x <= 1f && uv[index].y >= 0f && uv[index].y <= 1f), Is.True,
                        "Each cut face has a continuous normalized planar UV frame, not arbitrary exterior atlas islands.");
                }
            }
        }

        [Test]
        public void CacheRegenerationPreservesSourceAndAllGeneratedBytesAndGuids()
        {
            var paths = Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/_SashimiBoy/Art/Source/DayWorld/Fish/Stage03", "*", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("Assets/_SashimiBoy/Art/Generated/Stage03Flounder", "*", SearchOption.AllDirectories))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Dictionary<string, string> before = paths.ToDictionary(p => p, Hash);
            RenderTexture beforeTarget = RenderTexture.active;
            bool beforeSrgbWrite = GL.sRGBWrite;
            RuntimeReflection.InvokeStatic(Generator, "BuildBatch");
            Assert.That(RenderTexture.active, Is.SameAs(beforeTarget));
            Assert.That(GL.sRGBWrite, Is.EqualTo(beforeSrgbWrite));
            foreach (var pair in before) Assert.That(Hash(pair.Key), Is.EqualTo(pair.Value), pair.Key);
            Assert.That(Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Length,
                Is.EqualTo(paths.Count(p => p.Replace('\\', '/').StartsWith(Root + "/", StringComparison.Ordinal))));
        }

        [Serializable] private sealed class CapReceiptProbe
        {
            public string algorithm;
            public FleshProbe capFleshReference;
        }
        [Serializable] private sealed class FleshProbe
        {
            public string materialPath, texturePath, textureSha256, sampling;
            public int inspectedPixels, acceptedFleshPixels;
            public Color capColor;
        }

        [Test]
        public void CutFacesAreOpaqueFleshFromTheProvidedSlice_WithoutExteriorAtlasOrNormalSampling()
        {
            const string referencePath = "Assets/_SashimiBoy/Art/Generated/Stage03Flounder/Materials/MAT_SashimiSlice.mat";
            var reference = AssetDatabase.LoadAssetAtPath<Material>(referencePath);
            Assert.That(reference, Is.Not.Null); Assert.That(reference.mainTexture, Is.Not.Null);
            string sourcePath = AssetDatabase.GetAssetPath(reference.mainTexture);
            var receipt = JsonUtility.FromJson<CapReceiptProbe>(File.ReadAllText(Root + "/Stage03FilletCutReport.json"));
            Assert.That(receipt.algorithm, Is.EqualTo("Stage03FilletClip-3"));
            Assert.That(receipt.capFleshReference, Is.Not.Null);
            Assert.That(receipt.capFleshReference.materialPath, Is.EqualTo(referencePath));
            Assert.That(receipt.capFleshReference.texturePath, Is.EqualTo(sourcePath));
            using (var sha = SHA256.Create())
                Assert.That(receipt.capFleshReference.textureSha256,
                    Is.EqualTo(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(sourcePath))).Replace("-", "").ToLowerInvariant()));
            Assert.That(receipt.capFleshReference.acceptedFleshPixels, Is.GreaterThan(0));
            Assert.That(receipt.capFleshReference.acceptedFleshPixels, Is.LessThanOrEqualTo(receipt.capFleshReference.inspectedPixels));
            Color32[] sourceSamples = ReadReferenceSamples(reference.mainTexture);
            foreach (string role in new[] { "FrontFillet", "BackFillet" })
            {
                var cache = AssetDatabase.LoadAssetAtPath<ScriptableObject>(Root + "/" + role + "Cache.asset");
                var surface = (Material)RuntimeReflection.GetField(cache, "surfaceMaterial");
                var cap = (Material)RuntimeReflection.GetField(cache, "capMaterial");
                Assert.That(cap, Is.Not.SameAs(surface));
                Assert.That(surface.mainTexture, Is.Not.Null, "Only the new cross-section material changes; the supplied exterior atlas remains bound.");
                Assert.That(cap.mainTexture, Is.Null, "A new cross section cannot sample arbitrary skin-atlas islands.");
                Assert.That(cap.GetTexture("_BumpMap"), Is.Null);
                Assert.That(cap.GetTexture("_MetallicGlossMap"), Is.Null);
                Assert.That(cap.IsKeywordEnabled("_NORMALMAP"), Is.False);
                Assert.That(cap.IsKeywordEnabled("_ALPHATEST_ON"), Is.False);
                Assert.That(cap.IsKeywordEnabled("_ALPHABLEND_ON"), Is.False);
                Assert.That(cap.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"), Is.False);
                Assert.That(cap.GetFloat("_Mode"), Is.Zero);
                Assert.That(cap.GetInt("_SrcBlend"), Is.EqualTo((int)UnityEngine.Rendering.BlendMode.One));
                Assert.That(cap.GetInt("_DstBlend"), Is.EqualTo((int)UnityEngine.Rendering.BlendMode.Zero));
                Assert.That(cap.GetInt("_ZWrite"), Is.EqualTo(1));
                Assert.That(cap.GetFloat("_Metallic"), Is.Zero);
                Assert.That(cap.color.a, Is.EqualTo(1f));
                Assert.That(cap.color.grayscale, Is.InRange(.55f, .95f), "The cut face stays in the supplied light flesh range, not black/white padding.");
                Assert.That(Vector3.Distance(new Vector3(cap.color.r, cap.color.g, cap.color.b),
                    new Vector3(receipt.capFleshReference.capColor.r, receipt.capFleshReference.capColor.g, receipt.capFleshReference.capColor.b)), Is.LessThan(.0001f));
                float closest = sourceSamples.Where(pixel => pixel.a > 250).Min(pixel => Vector3.Distance(
                    new Vector3(pixel.r / 255f, pixel.g / 255f, pixel.b / 255f), new Vector3(cap.color.r, cap.color.g, cap.color.b)));
                Assert.That(closest, Is.LessThan(.075f), "The independently sampled supplied slice must contain the generated flesh tone.");
            }
        }

        private static Color32[] ReadReferenceSamples(Texture texture)
        {
            // This independent coarse readback compares the resulting material to
            // the actual provided flesh; it does not rerun the generator's selector.
            RenderTexture previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture target = null; Texture2D readback = null;
            try
            {
                target = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(texture, target); RenderTexture.active = target;
                readback = new Texture2D(64, 64, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
                readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0, false);
                return readback.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = previousSrgbWrite;
                if (readback != null) Object.DestroyImmediate(readback);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }

        private static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
        private static string Hash(string path)
        { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
        private static Mesh ConcavePrism()
        {
            var contour = new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(2, 1),
                new Vector2(1, 1), new Vector2(1, 2), new Vector2(0, 2) };
            var vertices = new Vector3[12]; var uv = new Vector2[12]; var triangles = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                vertices[i] = new Vector3(contour[i].x, contour[i].y, 0);
                vertices[i + 6] = new Vector3(contour[i].x, contour[i].y, 2);
                uv[i] = uv[i + 6] = contour[i] * .5f;
                int next = (i + 1) % 6;
                triangles.AddRange(new[] { i, next, next + 6, i, next + 6, i + 6 });
            }
            int[] top = { 0, 1, 3, 1, 2, 3, 0, 3, 5, 3, 4, 5 };
            for (int i = 0; i < top.Length; i += 3)
            {
                triangles.AddRange(new[] { top[i + 2], top[i + 1], top[i] });
                triangles.AddRange(new[] { top[i] + 6, top[i + 1] + 6, top[i + 2] + 6 });
            }
            var mesh = new Mesh { name = "ConcaveFilletFixture", vertices = vertices, uv = uv, triangles = triangles.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Mesh CrossedScanPrism(Vector2[] contour)
        {
            var vertices=new Vector3[contour.Length*2];var uv=new Vector2[vertices.Length];var triangles=new List<int>();
            for(int i=0;i<contour.Length;i++)
            {
                vertices[i]=new Vector3(contour[i].x,contour[i].y,0);
                vertices[i+contour.Length]=new Vector3(contour[i].x,contour[i].y,2);
                uv[i]=uv[i+contour.Length]=contour[i]*.5f;
                int next=(i+1)%contour.Length;
                triangles.AddRange(new[]{i,next,next+contour.Length,i,next+contour.Length,i+contour.Length});
            }
            for(int i=1;i+1<contour.Length;i++)
            {
                triangles.AddRange(new[]{0,i+1,i});
                triangles.AddRange(new[]{contour.Length,i+contour.Length,i+1+contour.Length});
            }
            var mesh=new Mesh{name="CrossedScanFilletFixture",vertices=vertices,uv=uv,triangles=triangles.ToArray()};
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
