using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace SashimiBoy.Tests
{
    public sealed class Day3ProgressionEditModeTests
    {
        static object Get(object target, string name) => RuntimeReflection.GetField(target, name);
        static void Set(object target, string name, object value) => RuntimeReflection.SetField(target, name, value);
        static object Rule(string name, params object[] args) => RuntimeReflection.InvokeStatic("SashimiBoy.DayWorldRules", name, args);
        static object Beat(string name) => Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.DayWorldBeat"), name);
        static object Equipment(string name) => Enum.Parse(RuntimeReflection.RuntimeType("SashimiBoy.EquipmentId"), name);
        static object FreshDay3()
        {
            var save = RuntimeReflection.InvokeStatic("SashimiBoy.SaveData", "CreateNew");
            var progress = Get(save, "dayWorld");
            Set(progress, "active", true);
            Set(progress, "day", 3);
            Set(progress, "nightsSlept", 2);
            ((IList)Get(save, "unlockedStageIds")).Add("STAGE_03_SOLE");
            ((IList)Get(progress, "completedDialogues")).Add("1:cheolsu");
            return save;
        }

        [Test]
        public void PersistedBeatValues_StayStable_AndEveningIsAppended()
        {
            string[] original = { "Wake", "MorningConversation", "Work", "AfterWorkConversation", "Purchase", "Placement", "Practice", "Sleep", "Complete" };
            for (int i = 0; i < original.Length; i++) Assert.That(Convert.ToInt32(Beat(original[i])), Is.EqualTo(i), original[i]);
            Assert.That(Convert.ToInt32(Beat("EveningConversation")), Is.EqualTo(9));
        }

        [Test]
        public void Day3_RequiresAllThreeConversations_AndPluginPracticeBeforeSleep()
        {
            var save = FreshDay3();
            var progress = Get(save, "dayWorld");
            var plugin = Equipment("Plugin");
            Assert.That(Rule("StageId", save), Is.EqualTo("STAGE_03_SOLE"));
            Assert.That(Rule("StageScene", save), Is.EqualTo("Stage03_Flounder"));
            Assert.That(Rule("Wake", save), Is.EqualTo(true));
            Assert.That(Rule("NpcAvailable", save, 3, "junseok"), Is.EqualTo(true));
            Assert.That(Rule("NpcAvailable", save, 1, "cheolsu"), Is.EqualTo(false));
            Assert.That(Rule("CanStart", save, "STAGE_03_SOLE"), Is.EqualTo(false));
            Assert.That(Rule("FinishDialogue", save, "jaehoon"), Is.EqualTo(false));
            Assert.That(Rule("FinishDialogue", save, "junseok"), Is.EqualTo(true));
            Assert.That(Rule("CanStart", save, "STAGE_03_SOLE"), Is.EqualTo(true));
            Assert.That(Rule("CanStart", save, "STAGE_02_ROCKFISH"), Is.EqualTo(false));
            Rule("RecordClear", save, "STAGE_03_SOLE");
            Assert.That(Rule("CanRecordClear", save, "STAGE_03_SOLE"), Is.EqualTo(false));
            Assert.That(Rule("NpcAvailable", save, 1, "cheolsu"), Is.EqualTo(true), "Reuse the same correctly seated Cheolsu object.");
            Assert.That(Rule("FinishDialogue", save, "cheolsu"), Is.EqualTo(true));
            Assert.That(((IList)Get(progress, "completedDialogues")).Contains("1:cheolsu"), Is.True);
            Assert.That(((IList)Get(progress, "completedDialogues")).Contains("3:cheolsu"), Is.True);
            ((IList)Get(save, "ownedEquipmentIds")).Add("Plugin");
            Rule("RecordPurchase", save, "STAGE_03_SOLE");
            Assert.That(Rule("Place", save, Equipment("DawSoftware")), Is.EqualTo(false));
            Assert.That(Rule("Place", save, plugin), Is.EqualTo(true));
            Assert.That(Rule("Sleep", save), Is.EqualTo(false));
            Assert.That(Rule("Practice", save, plugin), Is.EqualTo(true));
            Assert.That(Get(progress, "beat").ToString(), Is.EqualTo("EveningConversation"));
            Assert.That(Rule("BedAvailable", save), Is.EqualTo(false));
            Assert.That(Rule("Sleep", save), Is.EqualTo(false));
            Assert.That(Rule("FinishDialogue", save, "cheolsu"), Is.EqualTo(false));
            Assert.That(Rule("NpcAvailable", save, 3, "jaehoon"), Is.EqualTo(true));
            Assert.That(Rule("FinishDialogue", save, "jaehoon"), Is.EqualTo(true));
            Assert.That(Rule("BedAvailable", save), Is.EqualTo(true));
            Assert.That(Rule("Sleep", save), Is.EqualTo(true));
            Assert.That(Get(progress, "pendingStageClear"), Is.EqualTo(3));
            Assert.That(Get(progress, "beat").ToString(), Is.EqualTo("Complete"));
            string once = JsonUtility.ToJson(save);
            Assert.That(Rule("ContinueAfterStageClear", save), Is.EqualTo(false));
            Assert.That(Rule("Sleep", save), Is.EqualTo(false));
            Assert.That(Rule("FinishDialogue", save, "jaehoon"), Is.EqualTo(false));
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(once));
        }

        [TestCase(0)]
        [TestCase(2)]
        public void FormerStage2Endpoint_NormalizesOnce_WithoutCreatingRewards(int pending)
        {
            var save = FreshDay3();
            var progress = Get(save, "dayWorld");
            Set(progress, "day", 2);
            Set(progress, "beat", Beat("Complete"));
            Set(progress, "pendingStageClear", pending);
            Set(progress, "checkpointScene", "Street");
            RuntimeReflection.Invoke(progress, "Normalize");
            Assert.That(Get(progress, "day"), Is.EqualTo(3));
            Assert.That(Get(progress, "beat").ToString(), Is.EqualTo("Wake"));
            Assert.That(Get(progress, "pendingStageClear"), Is.EqualTo(2));
            Assert.That(Get(progress, "checkpointScene"), Is.EqualTo("KevinHome"));
            Assert.That(((IList)Get(save, "clearedStageIds")).Count, Is.Zero);
            Assert.That(((IList)Get(save, "fishPlates")).Count, Is.Zero);
            Assert.That(((IList)Get(save, "ownedEquipmentIds")).Count, Is.Zero);
            string once = JsonUtility.ToJson(save);
            RuntimeReflection.Invoke(progress, "Normalize");
            Assert.That(JsonUtility.ToJson(save), Is.EqualTo(once));
            Assert.That(Rule("ContinueAfterStageClear", save), Is.EqualTo(true));
            Assert.That(Get(progress, "beat").ToString(), Is.EqualTo("MorningConversation"));
            Assert.That(Get(progress, "pendingStageClear"), Is.EqualTo(0));
            Assert.That(Rule("ContinueAfterStageClear", save), Is.EqualTo(false));
        }

        [Test]
        public void Day3Json_RetainsEveningGateAndIdentityAcrossRoundTrip()
        {
            var save = FreshDay3();
            var progress = Get(save, "dayWorld");
            Set(progress, "beat", Beat("EveningConversation"));
            ((IList)Get(progress, "placedEquipment")).Add("Plugin");
            ((IList)Get(progress, "practicedDays")).Add(3);
            string json = JsonUtility.ToJson(save);
            var restored = JsonUtility.FromJson(json, save.GetType());
            RuntimeReflection.Invoke(Get(restored, "dayWorld"), "Normalize");
            Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(json));
            Assert.That(Rule("RequiredNpc", restored), Is.EqualTo("jaehoon"));
            Assert.That(Rule("BedAvailable", restored), Is.EqualTo(false));
            Assert.That(Rule("StageId", restored), Is.EqualTo("STAGE_03_SOLE"));
        }

        [Test]
        public void PendingClear_CannotSkipAWholeDayOrStartStageBeforeMorningConversation()
        {
            var save = FreshDay3();
            var progress = Get(save, "dayWorld");
            Set(progress, "pendingStageClear", 1);
            Assert.That(Rule("ContinueAfterStageClear", save), Is.EqualTo(false));
            Assert.That(Rule("CanStart", save, "STAGE_03_SOLE"), Is.EqualTo(false));
            Set(progress, "pendingStageClear", 2);
            Set(progress, "nightsSlept", 1);
            Assert.That(Rule("ContinueAfterStageClear", save), Is.EqualTo(false));
        }
    }
}
