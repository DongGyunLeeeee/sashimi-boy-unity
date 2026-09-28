using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.EquipmentShopTests
{
    public sealed class EquipmentShopArtPassPlayModeTests
    {
        private const string SceneName = "EquipmentShop";

        [UnityTest]
        public IEnumerator EquipmentShopArtPass_LoadsWithGameplayAndOwnership()
        {
            yield return SceneManager.LoadSceneAsync(
                SceneName,
                LoadSceneMode.Single);
            yield return null;

            Scene scene = SceneManager.GetActiveScene();
            Assert.That(scene.name, Is.EqualTo(SceneName));
            GameObject artRoot = FindNamed(scene, "EquipmentShopArtRoot");
            Assert.That(artRoot, Is.Not.Null);
            Assert.That(FindNamed(scene, "CounterRepairZone"), Is.Not.Null);
            Assert.That(FindNamed(scene, "DemoZone"), Is.Not.Null);
            Assert.That(FindNamed(scene, "InstrumentZone"), Is.Not.Null);
            Assert.That(FindNamed(scene, "WaitingZone"), Is.Not.Null);
            Assert.That(FindNamed(scene, "Owner_Canonical"), Is.Not.Null);
            Assert.That(
                FindNamed(scene, "Instrument_ElectronicDrumKit"),
                Is.Not.Null);
            Assert.That(FindNamed(scene, "Waiting_WoodenSofa"), Is.Not.Null);

            Assert.That(
                Components(scene, "SashimiBoy.EquipmentShopController").Length,
                Is.EqualTo(1));
            Assert.That(
                Components(scene, "SashimiBoy.ReturnToStreetDoor").Length,
                Is.EqualTo(1));
            Assert.That(
                Components(scene, "SashimiBoy.KevinFirstPersonCameraRig")
                    .Length,
                Is.EqualTo(1));
            Assert.That(
                Components<AudioListener>(scene).Count(item =>
                    item.enabled && item.gameObject.activeInHierarchy),
                Is.EqualTo(1));
            Assert.That(
                Components<EventSystem>(scene).Count(item =>
                    item.enabled && item.gameObject.activeInHierarchy),
                Is.EqualTo(1));

            Assert.That(
                FindNamed(scene, "PlayerEntryExitAnchor").transform.position,
                Is.EqualTo(FindNamed(scene, "PlayerSpawnPoint").transform.position));
            Assert.That(
                FindNamed(scene, "OwnerDialogueAnchor").transform.position,
                Is.EqualTo(FindNamed(scene, "Shopkeeper").transform.position));
            Assert.That(
                FindNamed(scene, "PurchaseInteractionAnchor").transform.position,
                Is.EqualTo(FindNamed(scene, "Counter").transform.position));
            AssertProtectedRoutesAreClear(artRoot);
        }

        private static void AssertProtectedRoutesAreClear(GameObject artRoot)
        {
            Physics.SyncTransforms();
            Bounds[] protectedRoutes =
            {
                new Bounds(
                    new Vector3(0f, 1.2f, -0.35f),
                    new Vector3(1.8f, 2.4f, 3.8f)),
                new Bounds(
                    new Vector3(2.65f, 1.2f, -2.35f),
                    new Vector3(5.3f, 2.4f, 1.4f)),
                new Bounds(
                    new Vector3(0f, 1.2f, 1.15f),
                    new Vector3(2.2f, 2.4f, 0.8f)),
            };
            foreach (Collider collider in
                     artRoot.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Assert.That(
                    protectedRoutes.Any(route =>
                        collider.bounds.Intersects(route)),
                    Is.False,
                    collider.name + " blocks a protected route.");
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
