using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EquipmentShopTests
{
    public sealed class EquipmentShopPurchaseEditModeTests
    {
        private const string EquipmentShopScenePath =
            "Assets/_SashimiBoy/Scenes/EquipmentShop.unity";
        private const string EquipmentShopBackupScenePath =
            "Assets/_SashimiBoy/Scenes/Backups/" +
            "EquipmentShop_PreKevinCamera.unity";
        private const string SalmonStageId = "STAGE_01_SALMON";
        private const string RockfishStageId = "STAGE_02_ROCKFISH";

        private readonly List<GameObject> createdObjects =
            new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            RuntimeReflection.SetSingleton("SashimiBoy.SaveManager", null);
            RuntimeReflection.SetSingleton("SashimiBoy.GameFlowManager", null);
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeReflection.SetSingleton("SashimiBoy.SaveManager", null);
            RuntimeReflection.SetSingleton("SashimiBoy.GameFlowManager", null);

            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }

            createdObjects.Clear();
        }

        [Test]
        public void EnteringShop_DoesNotMutateProgression()
        {
            object save = CreateNewSave();
            CreateSaveManager(save);
            Component controller = CreateComponent(
                "EquipmentShop",
                "SashimiBoy.EquipmentShopController");
            string before = JsonUtility.ToJson(save);

            RuntimeReflection.Invoke(controller, "Start");

            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(before));
            Assert.That(IsStageCleared(save, SalmonStageId), Is.False);
            Assert.That(IsStageUnlocked(save, RockfishStageId), Is.False);
            Assert.That(GetPlates(save, SalmonStage()), Is.Zero);
        }

        [Test]
        public void Purchase_UnclearedStageWithEnoughPlates_IsRejected()
        {
            object save = CreateNewSave();
            Component manager = CreateSaveManager(save);
            object stage = SalmonStage();
            int cost = PurchaseCost(stage);
            AddPlates(save, stage, cost);

            bool purchased = TryPurchase(manager, stage);

            Assert.That(purchased, Is.False);
            Assert.That(GetPlates(save, stage), Is.EqualTo(cost));
            Assert.That(HasRewardEquipment(save, stage), Is.False);
        }

        [Test]
        public void Purchase_ClearedStageWithInsufficientPlates_IsRejected()
        {
            object save = CreateNewSave();
            Component manager = CreateSaveManager(save);
            object stage = SalmonStage();
            MarkStageCleared(save, stage);

            bool purchased = TryPurchase(manager, stage);

            Assert.That(purchased, Is.False);
            Assert.That(GetPlates(save, stage), Is.Zero);
            Assert.That(HasRewardEquipment(save, stage), Is.False);
        }

        [Test]
        public void Purchase_ValidRequest_DeductsExactCostOnce()
        {
            object save = CreateNewSave();
            Component manager = CreateSaveManager(save);
            object stage = SalmonStage();
            int cost = PurchaseCost(stage);
            const int remainder = 3;
            MarkStageCleared(save, stage);
            AddPlates(save, stage, cost + remainder);

            bool purchased = TryPurchase(manager, stage);

            Assert.That(purchased, Is.True);
            Assert.That(GetPlates(save, stage), Is.EqualTo(remainder));
            Assert.That(HasRewardEquipment(save, stage), Is.True);
        }

        [Test]
        public void Purchase_Repurchase_IsRejectedWithoutAnotherDeduction()
        {
            object save = CreateNewSave();
            Component manager = CreateSaveManager(save);
            object stage = SalmonStage();
            int cost = PurchaseCost(stage);
            MarkStageCleared(save, stage);
            AddPlates(save, stage, cost + 2);
            Assert.That(TryPurchase(manager, stage), Is.True);
            int platesAfterPurchase = GetPlates(save, stage);

            bool repurchased = TryPurchase(manager, stage);

            Assert.That(repurchased, Is.False);
            Assert.That(GetPlates(save, stage),
                Is.EqualTo(platesAfterPurchase));
            Assert.That(HasRewardEquipment(save, stage), Is.True);
        }

        [Test]
        public void Purchase_NullOrIncompleteMetadata_IsRejectedSafely()
        {
            object save = CreateNewSave();
            Component manager = CreateSaveManager(save);
            object stage = SalmonStage();
            MarkStageCleared(save, stage);
            AddPlates(save, stage, PurchaseCost(stage));

            bool nullStagePurchase = Convert.ToBoolean(
                RuntimeReflection.Invoke(
                    manager,
                    "TryPurchaseReward",
                    new object[] { null }));
            object incompleteStage = Activator.CreateInstance(
                RuntimeReflection.RuntimeType("SashimiBoy.StageRuntimeData"));
            RuntimeReflection.SetField(
                incompleteStage,
                "stageId",
                SalmonStageId);
            bool incompletePurchase = TryPurchase(manager, incompleteStage);

            RuntimeReflection.SetField(manager, "current", null);
            bool nullSavePurchase = TryPurchase(manager, stage);

            Assert.That(nullStagePurchase, Is.False);
            Assert.That(incompletePurchase, Is.False);
            Assert.That(nullSavePurchase, Is.False);
            Assert.That(GetPlates(save, stage),
                Is.EqualTo(PurchaseCost(stage)));
            Assert.That(HasRewardEquipment(save, stage), Is.False);
        }

        [Test]
        public void EquipmentShopScenes_HaveProductionDefaultsAndValidReferences()
        {
            AssertEquipmentShopScene(EquipmentShopScenePath, true);

            // The one-time pre-camera backup is not a production scene. Its
            // legacy YAML keys are inert because the runtime fields no longer
            // exist, which the SerializedObject checks below enforce.
            AssertEquipmentShopScene(EquipmentShopBackupScenePath, false);
        }

        private static void AssertEquipmentShopScene(
            string scenePath,
            bool requireCleanYaml)
        {
            Scene scene = EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Additive);
            try
            {
                Component controller = RuntimeReflection.FindComponentInScene(
                    scene,
                    "SashimiBoy.EquipmentShopController");
                Assert.That(controller, Is.Not.Null);

                var serializedController = new SerializedObject(controller);
                Assert.That(serializedController.FindProperty(
                    "allowFreePrototypePurchases"), Is.Null);
                Assert.That(serializedController.FindProperty(
                    "simulateStageOneCleared"), Is.Null);
                string[] requiredReferences =
                {
                    "titleText",
                    "kevinRequestText",
                    "shopkeeperText",
                    "itemNameText",
                    "itemDescriptionText",
                    "priceText",
                    "ownedText",
                    "buyButton",
                    "leaveButton"
                };
                for (int i = 0; i < requiredReferences.Length; i++)
                {
                    SerializedProperty property = serializedController
                        .FindProperty(requiredReferences[i]);
                    Assert.That(property, Is.Not.Null);
                    Assert.That(property.objectReferenceValue, Is.Not.Null,
                        requiredReferences[i] + " is null.");
                }

                if (requireCleanYaml)
                {
                    string sceneYaml = File.ReadAllText(scenePath);
                    StringAssert.DoesNotContain(
                        "allowFreePrototypePurchases",
                        sceneYaml);
                    StringAssert.DoesNotContain(
                        "simulateStageOneCleared",
                        sceneYaml);
                }

                AssertSceneIntegrity(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private Component CreateSaveManager(object save)
        {
            Component manager = CreateComponent(
                "SaveManager",
                "SashimiBoy.SaveManager");
            RuntimeReflection.SetField(manager, "current", save);
            RuntimeReflection.SetField(manager, "autoSaveOnChange", false);
            RuntimeReflection.SetSingleton("SashimiBoy.SaveManager", manager);
            return manager;
        }

        private Component CreateComponent(string name, string fullTypeName)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);
            createdObjects.Add(gameObject);
            return RuntimeReflection.AddComponent(gameObject, fullTypeName);
        }

        private static object CreateNewSave()
        {
            return RuntimeReflection.InvokeStatic(
                "SashimiBoy.SaveData",
                "CreateNew");
        }

        private static object SalmonStage()
        {
            return RuntimeReflection.InvokeStatic(
                "SashimiBoy.ContentDefaults",
                "FindStage",
                SalmonStageId);
        }

        private static bool TryPurchase(Component manager, object stage)
        {
            return Convert.ToBoolean(RuntimeReflection.Invoke(
                manager,
                "TryPurchaseReward",
                stage));
        }

        private static int PurchaseCost(object stage)
        {
            return Convert.ToInt32(RuntimeReflection.GetField(
                stage,
                "requiredPlatesForExchange"));
        }

        private static void MarkStageCleared(object save, object stage)
        {
            RuntimeReflection.Invoke(
                save,
                "MarkStageCleared",
                RuntimeReflection.GetField(stage, "stageId"));
        }

        private static void AddPlates(object save, object stage, int amount)
        {
            RuntimeReflection.Invoke(
                save,
                "AddPlates",
                RuntimeReflection.GetField(stage, "fishType"),
                amount);
        }

        private static int GetPlates(object save, object stage)
        {
            return Convert.ToInt32(RuntimeReflection.Invoke(
                save,
                "GetPlates",
                RuntimeReflection.GetField(stage, "fishType")));
        }

        private static bool HasRewardEquipment(object save, object stage)
        {
            return Convert.ToBoolean(RuntimeReflection.Invoke(
                save,
                "HasEquipment",
                RuntimeReflection.GetField(stage, "rewardEquipment")));
        }

        private static bool IsStageCleared(object save, string stageId)
        {
            return Convert.ToBoolean(RuntimeReflection.Invoke(
                save,
                "IsStageCleared",
                stageId));
        }

        private static bool IsStageUnlocked(object save, string stageId)
        {
            return Convert.ToBoolean(RuntimeReflection.Invoke(
                save,
                "IsStageUnlocked",
                stageId));
        }

        private static void AssertSceneIntegrity(Scene scene)
        {
            int activeAudioListeners = 0;
            int activeEventSystems = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                AudioListener[] listeners = roots[rootIndex]
                    .GetComponentsInChildren<AudioListener>(true);
                for (int i = 0; i < listeners.Length; i++)
                {
                    if (listeners[i].isActiveAndEnabled)
                    {
                        activeAudioListeners++;
                    }
                }

                EventSystem[] eventSystems = roots[rootIndex]
                    .GetComponentsInChildren<EventSystem>(true);
                for (int i = 0; i < eventSystems.Length; i++)
                {
                    if (eventSystems[i].isActiveAndEnabled)
                    {
                        activeEventSystems++;
                    }
                }

                Transform[] transforms = roots[rootIndex]
                    .GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Assert.That(
                        GameObjectUtility
                            .GetMonoBehavioursWithMissingScriptCount(
                                transforms[i].gameObject),
                        Is.Zero,
                        "Missing Script under " + transforms[i].name + ".");
                }
            }

            Assert.That(activeAudioListeners, Is.EqualTo(1));
            Assert.That(activeEventSystems, Is.EqualTo(1));
        }
    }
}
