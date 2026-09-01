using System.Collections;
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.Tests
{
    public sealed class FishShopArtPassPlayModeTests
    {
        private const string FishShopSceneName = "FishShopDialogue";

        [UnityTest]
        public IEnumerator FishShopArtPass_LoadsWithInteractionAndSceneOwnership()
        {
            yield return SceneManager.LoadSceneAsync(
                FishShopSceneName,
                LoadSceneMode.Single);
            yield return null;

            Scene scene = SceneManager.GetActiveScene();
            Assert.That(
                scene.name,
                Is.EqualTo(FishShopSceneName));
            Assert.That(
                FindNamed(scene, "Task2_FishShopInteriorAssets"),
                Is.Not.Null);
            Assert.That(
                FindNamed(scene, "DisplayFish_Salmon"),
                Is.Not.Null);
            Assert.That(
                FindNamed(scene, "DisplayFish_Rockfish"),
                Is.Not.Null);
            Assert.That(
                FindNamed(scene, "DisplayFish_Mullet"),
                Is.Not.Null);

            Assert.That(
                Components(scene, "SashimiBoy.DialogueTrigger").Length,
                Is.EqualTo(1));
            Assert.That(
                Components(
                    scene,
                    "SashimiBoy.StageStarterInteractable").Length,
                Is.EqualTo(1));
            Assert.That(
                Components(
                    scene,
                    "SashimiBoy.ReturnToStreetDoor").Length,
                Is.EqualTo(1));
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

            string[] legacyVisuals =
            {
                "Boss_Visual",
                "Kevin_Visual",
                "CounterFront",
                "CounterTrim",
                "Placeholder_Fish_Fallback",
            };
            for (int i = 0; i < legacyVisuals.Length; i++)
            {
                GameObject target = FindNamed(scene, legacyVisuals[i]);
                Assert.That(target, Is.Not.Null, legacyVisuals[i]);
                Assert.That(
                    target.GetComponentsInChildren<Renderer>(true)
                        .Any(renderer => renderer.enabled),
                    Is.False,
                    legacyVisuals[i] + " is still visibly rendered.");
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
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
        }
    }
}
