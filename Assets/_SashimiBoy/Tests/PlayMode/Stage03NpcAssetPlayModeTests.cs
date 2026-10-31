#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SashimiBoy.Tests
{
    // Editor PlayMode asset smoke test; this does not claim a Player/FPS/animation check.
    public sealed class Stage03NpcAssetPlayModeTests
    {
        private const string Prefabs = "Assets/_SashimiBoy/Art/Generated/Stage03NpcAssets/Prefabs/";

        [UnityTest]
        public IEnumerator StaticPreviews_SurviveFramesAndReactivationWithoutRuntimeOwnership()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene temporary = SceneManager.CreateScene("Issue83_StaticPreview_" + Guid.NewGuid().ToString("N"));
            var instances = new GameObject[2];
            AsyncOperation unload = null;
            string[] ids = { "Junseok", "Jaehoon" };
            try
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "PF_" + ids[i] + ".prefab");
                    Assert.That(prefab, Is.Not.Null, ids[i]);
                    instances[i] = Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(instances[i], temporary);
                    instances[i].transform.position = new Vector3(i * 2.5f, 0f, 0f);
                }
                yield return null;
                Bounds[] before = instances.Select(BoundsOf).ToArray();
                int[][] meshes = instances.Select(go => go.GetComponentsInChildren<MeshFilter>(true)
                    .Select(f => f.sharedMesh.GetInstanceID()).ToArray()).ToArray();
                int[][] materials = instances.Select(go => go.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(r => r.sharedMaterials).Select(m => m.GetInstanceID()).ToArray()).ToArray();
                foreach (GameObject instance in instances)
                {
                    AssertStaticComponents(instance);
                    instance.SetActive(false);
                }
                yield return null;
                foreach (GameObject instance in instances) instance.SetActive(true);
                yield return null;
                yield return null;
                for (int i = 0; i < instances.Length; i++)
                {
                    AssertStaticComponents(instances[i]);
                    Assert.That(instances[i].activeInHierarchy, Is.True);
                    Bounds after = BoundsOf(instances[i]);
                    Assert.That(Vector3.Distance(after.center, before[i].center), Is.LessThan(.00001f));
                    Assert.That(Vector3.Distance(after.size, before[i].size), Is.LessThan(.00001f));
                    Assert.That(after.min.y, Is.EqualTo(0f).Within(.002f));
                    Assert.That(after.size.y, Is.EqualTo(1.75f).Within(.002f));
                    Assert.That(instances[i].GetComponentsInChildren<MeshFilter>(true)
                        .Select(f => f.sharedMesh.GetInstanceID()).ToArray(), Is.EqualTo(meshes[i]));
                    Assert.That(instances[i].GetComponentsInChildren<Renderer>(true)
                        .SelectMany(r => r.sharedMaterials).Select(m => m.GetInstanceID()).ToArray(), Is.EqualTo(materials[i]));
                    foreach (Renderer renderer in instances[i].GetComponentsInChildren<Renderer>(true))
                    {
                        Assert.That(renderer.enabled, Is.True);
                        Assert.That(renderer.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported), Is.True);
                    }
                }
                Assert.That(temporary.GetRootGameObjects().Length, Is.EqualTo(2), "Static previews must not spawn runtime owners.");
            }
            finally
            {
                foreach (GameObject instance in instances) if (instance != null) Object.DestroyImmediate(instance);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                // Start unloading in finally even when an assertion fails before the yields below.
                if (temporary.IsValid() && temporary.isLoaded) unload = SceneManager.UnloadSceneAsync(temporary);
            }
            while (unload != null && !unload.isDone) yield return null;
        }

        private static void AssertStaticComponents(GameObject root)
        {
            Assert.That(root.GetComponentsInChildren<MeshFilter>(true), Is.Not.Empty);
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true), Is.Not.Empty);
            Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Animation>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<AudioListener>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<EventSystem>(true), Is.Empty);
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Assert.That(filter.sharedMesh, Is.Not.Null);
                Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0));
            }
        }

        private static Bounds BoundsOf(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds result = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) result.Encapsulate(renderer.bounds);
            foreach (float f in new[] { result.center.x, result.center.y, result.center.z, result.size.x, result.size.y, result.size.z })
                Assert.That(float.IsNaN(f) || float.IsInfinity(f), Is.False);
            Assert.That(Mathf.Min(result.size.x, result.size.y, result.size.z), Is.GreaterThan(0f));
            return result;
        }
    }
}
#endif
