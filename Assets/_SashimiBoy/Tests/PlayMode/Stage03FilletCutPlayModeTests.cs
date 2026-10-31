using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SashimiBoy.Tests
{
    public sealed class Stage03FilletCutPlayModeTests
    {
        private readonly List<Object> owned = new List<Object>();
        private float previousTimeScale;

        [SetUp] public void Setup() { previousTimeScale = Time.timeScale; Time.timeScale = 1f; }
        [UnityTearDown] public IEnumerator TearDown()
        {
            Time.timeScale = previousTimeScale;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.Destroy(owned[i]);
            owned.Clear(); yield return null;
        }

        [UnityTest]
        public IEnumerator SixOrdinalCutsAreIndependentAndDoNotCreateMeshesOrSpawnSlices()
        {
            ScriptableObject cache = CreateCache();
            Component a = CreateSurface(cache, "FrontFillet0"), b = CreateSurface(cache, "FrontFillet1");
            var filterA = a.GetComponent<MeshFilter>(); var filterB = b.GetComponent<MeshFilter>();
            Vector3 scale = new Vector3(1.4f, .8f, 1.1f); a.transform.localScale = scale;
            Mesh original = filterA.sharedMesh;
            int meshCount = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int childCount = a.transform.childCount;
            Assert.That(Cut(a, 1), Is.False, "A future ordinal must not skip uncut flesh.");
            for (int cut = 0; cut < 6; cut++)
            {
                Assert.That(Cut(a, cut), Is.True);
                Assert.That(Cut(a, cut), Is.False, "The same resolved input cannot cut twice.");
                Assert.That(Value<int>(a, "CutCount"), Is.EqualTo(cut + 1));
                Assert.That(Value<int>(b, "CutCount"), Is.Zero);
                Assert.That(filterB.sharedMesh, Is.SameAs(original));
                Assert.That(a.transform.localScale, Is.EqualTo(scale), "Actual residual meshes replace scale shrinking.");
            }
            Assert.That(Value<bool>(a, "IsEmpty"), Is.True);
            Assert.That(a.GetComponent<MeshRenderer>().enabled, Is.False);
            Assert.That(filterA.sharedMesh, Is.Null);
            Assert.That(Cut(a, 6), Is.False);
            Assert.That(a.transform.childCount, Is.EqualTo(childCount), "Stage controller owns slice output.");
            Assert.That(Resources.FindObjectsOfTypeAll<Mesh>().Length, Is.EqualTo(meshCount), "Gameplay must only swap the offline cache.");
            RuntimeReflection.Invoke(a, "ResetCuts");
            Assert.That(Value<int>(a, "CutCount"), Is.Zero);
            Assert.That(filterA.sharedMesh, Is.SameAs(original));
            Assert.That(a.GetComponent<MeshRenderer>().enabled, Is.True);
            Assert.That(Cut(a, 0), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PausedDisabledAndInactiveCallsCannotConsumeFillet()
        {
            Component surface = CreateSurface(CreateCache(), "BackFillet");
            Time.timeScale = 0f; Assert.That(Cut(surface, 0), Is.False);
            Time.timeScale = 1f;
            RuntimeReflection.Invoke(surface, "SetCuttingEnabled", false); Assert.That(Cut(surface, 0), Is.False);
            RuntimeReflection.Invoke(surface, "SetCuttingEnabled", true);
            ((Behaviour)surface).enabled = false; Assert.That(Cut(surface, 0), Is.False);
            ((Behaviour)surface).enabled = true;
            surface.gameObject.SetActive(false); Assert.That(Cut(surface, 0), Is.False);
            surface.gameObject.SetActive(true);
            Assert.That(Value<int>(surface, "CutCount"), Is.Zero);
            Assert.That(Cut(surface, 0), Is.True);
            Assert.That(Cut(surface, -1), Is.False);
            yield return null;
        }

        private ScriptableObject CreateCache()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up }, triangles = new[] { 0, 1, 2 }
            };
            mesh.RecalculateBounds(); owned.Add(mesh);
            var material = new Material(Shader.Find("Standard")); owned.Add(material);
            var cache = ScriptableObject.CreateInstance(RuntimeReflection.RuntimeType("SashimiBoy.Stage03FilletCutCache")); owned.Add(cache);
            var meshes = new Mesh[7]; for (int i = 0; i < 6; i++) meshes[i] = mesh;
            RuntimeReflection.SetField(cache, "modelRole", "Fixture");
            RuntimeReflection.SetField(cache, "remainingMeshes", meshes);
            RuntimeReflection.SetField(cache, "cutPositions", new[] { 1f, 2f, 3f, 4f, 5f, 6f });
            RuntimeReflection.SetField(cache, "surfaceMaterial", material);
            RuntimeReflection.SetField(cache, "capMaterial", material);
            return cache;
        }
        private Component CreateSurface(ScriptableObject cache, string name)
        {
            var root = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); owned.Add(root);
            Component surface = RuntimeReflection.AddComponent(root, "SashimiBoy.Stage03FilletSurface");
            RuntimeReflection.Invoke(surface, "Bind", cache);
            return surface;
        }
        private static bool Cut(Component surface, int ordinal) => (bool)RuntimeReflection.Invoke(surface, "TryCut", ordinal);
        private static T Value<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
    }
}
