using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SashimiBoy.EditorTools
{
    // The supplied knife's sharpened bevel is +Y in the existing wrapper.
    // PCA establishes its long axis but cannot choose between that bevel and
    // the rounded -Y spine. Keep this correction local to the Stage3 scene.
    public static class Stage03KnifeAuthoring
    {
        public const string SourcePath = "Assets/_SashimiBoy/Art/Source/Environment/FishShop/Props/KitchenKnife/Models/KitchenKnife.fbx";
        public const string SourceGuid = "0d849fb4b722b4e7a8a320249dd577c3";
        public const string SourceSha256 = "8d8c70daec71027d8d72e451d7b2c8c9ecce7473155831a93afe315d06142e55";

        [Serializable] public sealed class TransformRecord
        {
            public string name;
            public Vector3 position, scale;
            public Quaternion rotation;
        }
        [Serializable] public sealed class Report
        {
            public string algorithm = "Stage03SuppliedKnifeEdge-1";
            public string sourcePath = SourcePath, sourceGuid = SourceGuid, sourceSha256 = SourceSha256;
            public int sourceControlPoints = 975089, importedVertices;
            public Bounds localBounds;
            public float bladeBandMinZ, bladeBandMaxZ, sharpEdgeThickness, spineThickness;
            public Vector3 contactBefore, contactAfter, gripLocal, forwardBefore, forwardAfter;
            public Vector3 sharpDirectionBefore, sharpDirectionAfter;
            public Quaternion rotationBefore, rotationAfter;
            public TransformRecord[] hierarchy;
            public string contract = "The pinned source's +Y blade edge tapers to a thinner bevel than its -Y rounded spine. Put +Y down with a proper quaternion while preserving +Z hilt-to-tip direction. Locate BladeContact on the actual +Y edge near the existing blade midpoint; preserve GripAnchor, source FBX, shared prefab and all scales.";
        }

        public static Report Bind(Stage03CookingView cooking)
        {
            if (cooking == null || cooking.knifeRoot == null || cooking.grip == null || cooking.contact == null)
                throw new InvalidOperationException("Stage3 supplied knife and its two anchors are required.");
            if (AssetDatabase.AssetPathToGUID(SourcePath) != SourceGuid || Hash(SourcePath) != SourceSha256)
                throw new InvalidOperationException("The supplied knife identity changed; inspect its real bevel before rebinding the cutting edge.");
            Transform knife = cooking.knifeRoot;
            var points = new List<Vector3>();
            foreach (var filter in knife.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || AssetDatabase.GetAssetPath(filter.sharedMesh) != SourcePath)
                    throw new InvalidOperationException("Stage3 must retain only the supplied knife mesh under its wrapper.");
                Matrix4x4 toKnife = knife.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                using var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh);
                using var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
                data[0].GetVertices(vertices);
                foreach (Vector3 vertex in vertices) points.Add(toKnife.MultiplyPoint3x4(vertex));
            }
            // Unity can split control points at UV/normal seams. Inspect every
            // imported vertex without assuming its count equals FBX control points.
            if (points.Count == 0) throw new InvalidOperationException("The supplied knife has no imported mesh vertices.");
            foreach (Transform node in knife.GetComponentsInChildren<Transform>(true))
                if (node.localScale.x <= 0 || node.localScale.y <= 0 || node.localScale.z <= 0)
                    throw new InvalidOperationException("Orient the knife with a proper rotation, never a negative scale.");
            Bounds bounds = BoundsOf(points);
            float z = bounds.min.z + bounds.size.z * .61f;
            var band = points.Where(p => Mathf.Abs(p.z - z) <= .05f).ToArray();
            Bounds blade = BoundsOf(band);
            Bounds bevel = BoundsOf(band.Where(p => p.y >= blade.max.y - blade.size.y * .1f));
            Bounds spine = BoundsOf(band.Where(p => p.y <= blade.min.y + blade.size.y * .1f));
            if (bevel.size.x <= .001f || bevel.size.x >= spine.size.x * .7f)
                throw new InvalidOperationException("Actual supplied blade cross-section no longer proves the +Y sharpened bevel.");
            var contactBand = points.Where(p => Mathf.Abs(p.z - z) <= .005f).ToArray();
            float edgeY = contactBand.Max(p => p.y);
            Bounds edge = BoundsOf(contactBand.Where(p => p.y >= edgeY - .002f));
            var report = new Report
            {
                importedVertices = points.Count, localBounds = bounds,
                bladeBandMinZ = z - .05f, bladeBandMaxZ = z + .05f,
                sharpEdgeThickness = bevel.size.x, spineThickness = spine.size.x,
                contactBefore = cooking.contact.localPosition, gripLocal = cooking.grip.localPosition,
                rotationBefore = knife.rotation, forwardBefore = knife.forward, sharpDirectionBefore = knife.up,
                hierarchy = knife.GetComponentsInChildren<Transform>(true).Select(t => new TransformRecord
                    { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray()
            };
            Vector3 forward = Vector3.ProjectOnPlane(knife.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .9f) throw new InvalidOperationException("Stage3 knife must have a horizontal hilt-to-tip axis.");
            // Idempotent: do not add another 180-degree roll on repeated builds.
            knife.rotation = Quaternion.LookRotation(forward, Vector3.down);
            cooking.contact.localPosition = new Vector3(edge.center.x, edgeY, z);
            report.contactAfter = cooking.contact.localPosition;
            report.rotationAfter = knife.rotation; report.forwardAfter = knife.forward; report.sharpDirectionAfter = knife.up;
            EditorUtility.SetDirty(knife); EditorUtility.SetDirty(cooking.contact);
            if (PrefabUtility.IsPartOfPrefabInstance(knife)) PrefabUtility.RecordPrefabInstancePropertyModifications(knife);
            if (PrefabUtility.IsPartOfPrefabInstance(cooking.contact)) PrefabUtility.RecordPrefabInstancePropertyModifications(cooking.contact);
            return report;
        }

        // A focused scene update: no fish/body/chart/shared-asset regeneration.
        public static void ApplyKnifePoseBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save unsaved scenes first.");
            string output = Environment.GetEnvironmentVariable("SASHIMI_STAGE3_KNIFE_REPORT");
            if (string.IsNullOrWhiteSpace(output) || File.Exists(output))
                throw new InvalidOperationException("Set a fresh SASHIMI_STAGE3_KNIFE_REPORT JSON output path.");
            Stage03BuildInputGuard.ValidateMaterializedAssets();
            var scene = EditorSceneManager.OpenScene(Stage03PlayableAuthoring.ScenePath, OpenSceneMode.Single);
            var cooking = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Stage03CookingView>(true)).Single();
            Report report = Bind(cooking);
            Stage03PlayableAuthoring.Scan(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Stage3 knife scene save failed.");
            // Match the owning Stage3 generator's canonical YAML. Unity adds
            // trailing spaces to empty serialized scalars whenever it saves.
            AssetDatabase.ReleaseCachedFileHandles();
            string canonical = string.Join("\n", File.ReadAllLines(Stage03PlayableAuthoring.ScenePath)
                .Select(line => line.TrimEnd(' ', '\t'))) + "\n";
            File.WriteAllText(Stage03PlayableAuthoring.ScenePath, canonical, new System.Text.UTF8Encoding(false));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonUtility.ToJson(report, true) + "\n");
            Debug.Log("[Stage03Knife] Actual supplied +Y cutting edge faces the fish; geometry report: " + output);
        }

        private static Bounds BoundsOf(IEnumerable<Vector3> values)
        {
            using var iterator = values.GetEnumerator();
            if (!iterator.MoveNext()) throw new InvalidOperationException("Missing actual knife section geometry.");
            var bounds = new Bounds(iterator.Current, Vector3.zero);
            while (iterator.MoveNext()) bounds.Encapsulate(iterator.Current);
            return bounds;
        }
        private static string Hash(string path)
        {
            using var hash = SHA256.Create(); using var input = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
    }
}
