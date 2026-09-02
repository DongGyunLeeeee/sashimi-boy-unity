using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    public sealed class ClubArtPassPlayModeTests
    {
        private const string ClubSceneName = "Club";

        [UnityTest]
        public IEnumerator ClubArtPass_LoadsWithGameplayAndSceneOwnership()
        {
            yield return SceneManager.LoadSceneAsync(
                ClubSceneName,
                LoadSceneMode.Single);
            yield return null;

            Scene scene = SceneManager.GetActiveScene();
            Assert.That(scene.name, Is.EqualTo(ClubSceneName));
            GameObject artRoot = FindNamed(scene, "ClubArtRoot");
            Assert.That(artRoot, Is.Not.Null);
            Assert.That(
                FindNamed(scene, "DJStand_PF_Club_DJStand"),
                Is.Not.Null);
            Assert.That(
                FindNamed(scene, "BarTable_01_PF_Club_BarTable"),
                Is.Not.Null);
            Assert.That(
                FindNamed(scene, "DoorFrame_PF_Club_ClubDoorFrame"),
                Is.Not.Null);

            Assert.That(
                Components(scene, "SashimiBoy.ClubController").Length,
                Is.EqualTo(1));
            Assert.That(
                Components(scene, "SashimiBoy.ClubPerformanceGate").Length,
                Is.EqualTo(1));
            Assert.That(
                Components(scene, "SashimiBoy.ReturnToStreetDoor").Length,
                Is.EqualTo(1));
            Assert.That(
                Components(scene, "SashimiBoy.KevinFirstPersonCameraRig")
                    .Cast<Behaviour>()
                    .Count(item => item.enabled &&
                        item.gameObject.activeInHierarchy),
                Is.EqualTo(1));
            Assert.That(
                Components<AudioListener>(scene)
                    .Count(item => item.enabled &&
                        item.gameObject.activeInHierarchy),
                Is.EqualTo(1));
            Assert.That(
                Components<EventSystem>(scene)
                    .Count(item => item.enabled &&
                        item.gameObject.activeInHierarchy),
                Is.EqualTo(1));

            string[] hiddenLegacyVisuals =
            {
                "Label_PerformanceGate",
                "Label_ReturnDoor",
                "PerformanceGate",
                "DJ_Booth",
                "Bar",
            };
            for (int i = 0; i < hiddenLegacyVisuals.Length; i++)
            {
                GameObject target = FindNamed(scene, hiddenLegacyVisuals[i]);
                Assert.That(target, Is.Not.Null, hiddenLegacyVisuals[i]);
                Assert.That(
                    target.GetComponentsInChildren<Renderer>(true)
                        .Any(renderer => renderer.enabled),
                    Is.False,
                    hiddenLegacyVisuals[i] + " is still visibly rendered.");
            }

            Assert.That(
                FindNamed(scene, "DJ_Booth")
                    .GetComponentsInChildren<Collider>(true)
                    .Any(collider => collider.enabled),
                Is.False);
            Assert.That(
                FindNamed(scene, "Bar")
                    .GetComponentsInChildren<Collider>(true)
                    .Any(collider => collider.enabled),
                Is.False);

            Physics.SyncTransforms();
            Bounds[] protectedRoutes =
            {
                new Bounds(
                    new Vector3(0f, 1.5f, -1.25f),
                    new Vector3(2.4f, 3f, 5.5f)),
                new Bounds(
                    new Vector3(0f, 1.5f, 1.9f),
                    new Vector3(3f, 3f, 1.4f)),
                new Bounds(
                    new Vector3(6.4f, 1.5f, -4.1f),
                    new Vector3(2.4f, 3f, 3f)),
            };
            foreach (Collider collider in
                     artRoot.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled ||
                    !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Assert.That(
                    protectedRoutes.Any(route =>
                        collider.bounds.Intersects(route)),
                    Is.False,
                    collider.name + " blocks a protected Club route.");
            }
        }

        private static GameObject FindNamed(Scene scene, string name)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(item => item.name == name)
                ?.gameObject;
        }

        private static Component[] Components(
            Scene scene,
            string fullTypeName)
        {
            Type type = RuntimeReflection.RuntimeType(fullTypeName);
            return scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren(type, true)
                        .Cast<Component>())
                .ToArray();
        }

        private static T[] Components<T>(Scene scene)
            where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<T>(true))
                .ToArray();
        }
    }
}
