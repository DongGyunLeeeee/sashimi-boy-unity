using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SashimiBoy.EquipmentShopTests
{
    public sealed class EquipmentShopPurchasePlayModeTests
    {
        private const string EquipmentShopSceneName = "EquipmentShop";
        private const string SalmonStageId = "STAGE_01_SALMON";

        [UnityTest]
        public IEnumerator EquipmentShopScene_EnforcesPurchaseLifecycle()
        {
            yield return null;

            Component manager = RuntimeReflection.FindActiveComponent(
                "SashimiBoy.SaveManager");
            Assert.That(manager, Is.Not.Null);

            object originalSave = RuntimeReflection.GetField(
                manager,
                "current");
            bool originalAutoSave = Convert.ToBoolean(
                RuntimeReflection.GetField(manager, "autoSaveOnChange"));
            object testSave = RuntimeReflection.InvokeStatic(
                "SashimiBoy.SaveData",
                "CreateNew");
            object stage = RuntimeReflection.InvokeStatic(
                "SashimiBoy.ContentDefaults",
                "FindStage",
                SalmonStageId);
            int cost = Convert.ToInt32(RuntimeReflection.GetField(
                stage,
                "requiredPlatesForExchange"));

            RuntimeReflection.SetField(manager, "autoSaveOnChange", false);
            RuntimeReflection.SetField(manager, "current", testSave);
            string beforeEntry = JsonUtility.ToJson(testSave);

            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(
                    EquipmentShopSceneName,
                    LoadSceneMode.Single);
                Assert.That(load, Is.Not.Null);
                yield return load;
                yield return null;

                Assert.That(JsonUtility.ToJson(testSave),
                    Is.EqualTo(beforeEntry),
                    "Entering EquipmentShop changed progression.");

                Scene scene = SceneManager.GetActiveScene();
                Component controller = RuntimeReflection.FindComponentInScene(
                    scene,
                    "SashimiBoy.EquipmentShopController");
                Assert.That(controller, Is.Not.Null);

                AddPlates(testSave, stage, cost);
                RuntimeReflection.Invoke(controller, "Refresh");
                RuntimeReflection.Invoke(controller, "BuyRecommended");
                Assert.That(GetPlates(testSave, stage), Is.EqualTo(cost));
                Assert.That(HasRewardEquipment(testSave, stage), Is.False,
                    "Uncleared stage purchase was accepted.");

                Assert.That(Convert.ToBoolean(RuntimeReflection.Invoke(
                    testSave,
                    "TryConsumePlates",
                    RuntimeReflection.GetField(stage, "fishType"),
                    cost)), Is.True);
                RuntimeReflection.Invoke(
                    testSave,
                    "MarkStageCleared",
                    SalmonStageId);
                RuntimeReflection.Invoke(controller, "Refresh");
                RuntimeReflection.Invoke(controller, "BuyRecommended");
                Assert.That(GetPlates(testSave, stage), Is.Zero);
                Assert.That(HasRewardEquipment(testSave, stage), Is.False,
                    "Insufficient-plate purchase was accepted.");

                const int expectedRemainder = 2;
                AddPlates(testSave, stage, cost + expectedRemainder);
                RuntimeReflection.Invoke(controller, "Refresh");
                RuntimeReflection.Invoke(controller, "BuyRecommended");
                Assert.That(GetPlates(testSave, stage),
                    Is.EqualTo(expectedRemainder));
                Assert.That(HasRewardEquipment(testSave, stage), Is.True);

                RuntimeReflection.Invoke(controller, "BuyRecommended");
                Assert.That(GetPlates(testSave, stage),
                    Is.EqualTo(expectedRemainder),
                    "Repurchase deducted plates again.");
            }
            finally
            {
                RuntimeReflection.SetField(manager, "current", originalSave);
                RuntimeReflection.SetField(
                    manager,
                    "autoSaveOnChange",
                    originalAutoSave);
            }
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
    }
}
