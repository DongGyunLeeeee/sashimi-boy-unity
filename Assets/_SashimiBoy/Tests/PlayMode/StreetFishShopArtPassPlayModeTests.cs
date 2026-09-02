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
    public sealed class StreetFishShopArtPassPlayModeTests
    {
        [UnityTest]
        public IEnumerator StreetFrontage_LoadsWithDoorAndInputOwnership()
        {
            yield return SceneManager.LoadSceneAsync(
                "Street",
                LoadSceneMode.Single);
            yield return null;

            Scene scene = SceneManager.GetActiveScene();
            Assert.That(scene.name, Is.EqualTo("Street"));
            GameObject display = FindNamed(
                scene,
                "DisplayOutside_Validated");
            Assert.That(
                FindNamed(scene, "Task2_FishShopExteriorAssets"),
                Is.Not.Null);
            Assert.That(display, Is.Not.Null);
            Assert.That(
                display.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(FindNamed(scene, "NPCSpawn_Morning"), Is.Not.Null);
            Assert.That(FindNamed(scene, "NPCSpawn_AfterWork"), Is.Not.Null);

            GameObject signObject = FindNamed(
                scene,
                "FishShop_Sign_Text");
            Assert.That(signObject, Is.Not.Null);
            Assert.That(
                Vector3.Dot(-signObject.transform.forward, Vector3.forward),
                Is.GreaterThan(0.999f));

            Assert.That(
                Components(scene, "SashimiBoy.SceneDoor").Length,
                Is.EqualTo(3));
            Assert.That(
                Components(
                        scene,
                        "SashimiBoy.KevinFirstPersonCameraRig")
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
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }
    }
}
