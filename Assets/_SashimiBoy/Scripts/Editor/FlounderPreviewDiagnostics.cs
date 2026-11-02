using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SashimiBoy.EditorTools
{
    // Optional editor-only diagnostic for the next Issue #73 capture. Does not
    // save/unpack the asset: only a disposable instance in an isolated preview scene.
    public static class FlounderPreviewDiagnostics
    {
        public static void PrepareRuntimeClone(GameObject root)
        {
            Require(root != null && !EditorUtility.IsPersistent(root) && EditorSceneManager.IsPreviewScene(root.scene),
                "Only a disposable preview-scene instance may be prepared.");
            if (PrefabUtility.IsPartOfAnyPrefab(root))
            {
                Require(PrefabUtility.GetOutermostPrefabInstanceRoot(root) == root,
                    "Do not unpack a nested child or any user-scene prefab.");
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            Require(!root.GetComponentsInChildren<Transform>(true).Any(t => PrefabUtility.IsPartOfAnyPrefab(t)),
                "Preview runtime clone must have no remaining connected nested prefab roots.");
        }

        public static void WriteSnapshot(FlounderAssemblyView view, string stage, string externalPath)
        {
            Require(view != null && EditorSceneManager.IsPreviewScene(view.gameObject.scene), "Audit requires the disposable preview view.");
            Require(Path.IsPathRooted(externalPath), "Audit output must be an absolute external evidence path.");
            string path = Path.GetFullPath(externalPath);
            string project = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath)) + Path.DirectorySeparatorChar;
            Require(!path.StartsWith(project, StringComparison.OrdinalIgnoreCase), "Do not write audit evidence into a checkout.");
            var report = new Snapshot
            {
                stage = stage, bodyState = view.BodyState.ToString(), hasBody = view.HasBody,
                headRemoved = view.HeadRemoved, detachedFilletCount = view.DetachedFilletCount,
                backFacing = view.IsBackFacing, turning = view.IsTurningOver,
                bodyPivotPosition = view.bodyPivot.position, bodyPivotRotation = view.bodyPivot.rotation,
                bodies = view.bodyStates.Select(b => new Body
                {
                    path = Hierarchy(b.transform), activeSelf = b.activeSelf, activeHierarchy = b.activeInHierarchy
                }).ToArray(),
                parts = new[] { view.head }.Concat(view.fillets).Select(p => new Part
                {
                    id = p.stableId, path = Hierarchy(p.transform), parent = Hierarchy(p.transform.parent),
                    storage = Hierarchy(p.storageAnchor), sameParentAsStorage = p.transform.parent == p.storageAnchor,
                    storagePosition = p.storageAnchor.position, worldPosition = p.transform.position,
                    storageError = Vector3.Distance(p.transform.position, p.storageAnchor.position),
                    localPosition = p.transform.localPosition, worldRotation = p.transform.rotation,
                    localScale = p.transform.localScale, activeHierarchy = p.gameObject.activeInHierarchy,
                    detached = p.IsDetached, moving = p.IsMoving, stored = p.IsStored,
                    connectedPrefab = PrefabUtility.IsPartOfAnyPrefab(p),
                    renderers = p.GetComponentsInChildren<MeshRenderer>(true).Select(RendererState).ToArray()
                }).ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
        }

        public static void RequireCompletedStorage(FlounderAssemblyView view)
        {
            Require(view.BodyState == FlounderBodyState.Hidden && !view.HasBody, "Final body state did not hide after all five parts arrived.");
            Require(view.bodyStates.All(b => !b.activeSelf && !b.activeInHierarchy), "A body renderer's object remains active in Hidden state.");
            foreach (var part in new[] { view.head }.Concat(view.fillets))
            {
                Require(part.IsDetached && part.IsStored && !part.IsMoving, "Part has not actually completed: " + part.stableId);
                Require(part.transform.parent == part.storageAnchor, "SetParent did not reach the storage anchor: " + part.stableId);
                Require(Vector3.Distance(part.transform.position, part.storageAnchor.position) < .00001f,
                    "Stored world position differs from its anchor: " + part.stableId);
                Require(part.gameObject.activeInHierarchy, "Stored part is unexpectedly hidden: " + part.stableId);
                foreach (var renderer in part.GetComponentsInChildren<MeshRenderer>(true))
                    Require(renderer.enabled && renderer.gameObject.activeInHierarchy, "Stored part renderer is disabled: " + part.stableId);
            }
        }

        private static RendererSnapshot RendererState(MeshRenderer renderer)
        {
            Matrix4x4 actual = renderer.transform.localToWorldMatrix;
            Matrix4x4 proxy = renderer.localToWorldMatrix;
            float difference = 0;
            for (int i = 0; i < 16; i++) difference = Mathf.Max(difference, Mathf.Abs(actual[i] - proxy[i]));
            return new RendererSnapshot
            {
                path = Hierarchy(renderer.transform), enabled = renderer.enabled,
                activeHierarchy = renderer.gameObject.activeInHierarchy,
                transformMatrix = Enumerable.Range(0, 16).Select(i => actual[i]).ToArray(),
                rendererMatrix = Enumerable.Range(0, 16).Select(i => proxy[i]).ToArray(),
                matrixMaximumDifference = difference,
                rendererBoundsCenter = renderer.bounds.center, rendererBoundsSize = renderer.bounds.size
            };
        }

        private static string Hierarchy(Transform transform)
        {
            if (transform == null) return "<none>";
            string value = transform.name;
            while (transform.parent != null) { transform = transform.parent; value = transform.name + "/" + value; }
            return value;
        }

        [Serializable] private sealed class Snapshot
        {
            public string stage, bodyState;
            public bool hasBody, headRemoved, backFacing, turning;
            public int detachedFilletCount;
            public Vector3 bodyPivotPosition;
            public Quaternion bodyPivotRotation;
            public Body[] bodies;
            public Part[] parts;
        }
        [Serializable] private sealed class Body { public string path; public bool activeSelf, activeHierarchy; }
        [Serializable] private sealed class Part
        {
            public string id, path, parent, storage;
            public bool sameParentAsStorage, activeHierarchy, detached, moving, stored, connectedPrefab;
            public float storageError;
            public Vector3 storagePosition, worldPosition, localPosition, localScale;
            public Quaternion worldRotation;
            public RendererSnapshot[] renderers;
        }
        [Serializable] private sealed class RendererSnapshot
        {
            public string path;
            public bool enabled, activeHierarchy;
            public float[] transformMatrix, rendererMatrix;
            public float matrixMaximumDifference;
            public Vector3 rendererBoundsCenter, rendererBoundsSize;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
