using System;
using System.Collections.Generic;

namespace SashimiBoy
{
    // Append new beats: existing integer values are persisted in version-one saves.
    public enum DayWorldBeat { Wake, MorningConversation, Work, AfterWorkConversation, Purchase, Placement, Practice, Sleep, Complete, EveningConversation }

    [Serializable]
    public sealed class DayWorldProgress
    {
        public bool active;
        public int day = 1;
        public DayWorldBeat beat = DayWorldBeat.Wake;
        public string checkpointScene = "KevinHome";
        public string checkpointSpawn = "Wake";
        public List<string> completedDialogues = new List<string>();
        public List<string> placedEquipment = new List<string>();
        public List<int> practicedDays = new List<int>();
        public int nightsSlept;
        public int pendingStageClear;
        // Kept for old saves; arrival at the former endpoint no longer blocks Stage2.
        public bool reachedStageTwoBoundary;

        public void Normalize()
        {
            completedDialogues ??= new List<string>();
            placedEquipment ??= new List<string>();
            practicedDays ??= new List<int>();
            day = Math.Max(1, Math.Min(3, day));
            if (string.IsNullOrWhiteSpace(checkpointScene)) checkpointScene = "KevinHome";
            // The former two-day endpoint becomes the next morning, with the old clear
            // screen retained until the player chooses Continue. No reward is issued here.
            if (active && nightsSlept >= 2 && nightsSlept < 3 &&
                (beat == DayWorldBeat.Complete || pendingStageClear == 2 && day == 2))
            {
                day = 3;
                beat = DayWorldBeat.Wake;
                pendingStageClear = 2;
                checkpointScene = "KevinHome";
                checkpointSpawn = "Wake";
            }
            else if (active && beat == DayWorldBeat.Complete && nightsSlept >= 3)
            {
                day = 3;
                pendingStageClear = 3;
            }
        }
    }

    public static class DayWorldRules
    {
        public const string Home = "KevinHome";
        public const string StageTwoScene = "Stage02_Rockfish";
        public const string StageThreeScene = SashimiBoyConstants.Scenes.Stage03Flounder;
        public static bool Active(SaveData save) => save != null && save.dayWorld != null && save.dayWorld.active;
        public static string StageId(SaveData save) => save.dayWorld.day == 1 ? SashimiBoyConstants.StageIds.Salmon :
            save.dayWorld.day == 2 ? SashimiBoyConstants.StageIds.Rockfish : SashimiBoyConstants.StageIds.Sole;
        public static string StageScene(SaveData save) => save.dayWorld.day == 1 ? SashimiBoyConstants.Scenes.Stage01Salmon :
            save.dayWorld.day == 2 ? StageTwoScene : StageThreeScene;
        public static EquipmentId Equipment(SaveData save) => ContentDefaults.FindStage(StageId(save)).rewardEquipment;
        public static string RequiredNpc(SaveData save) => save.dayWorld.beat == DayWorldBeat.MorningConversation
            ? (save.dayWorld.day == 1 ? "misuk" : save.dayWorld.day == 2 ? "seongho" : "junseok")
            : save.dayWorld.beat == DayWorldBeat.AfterWorkConversation ? (save.dayWorld.day == 2 ? "minjae" : "cheolsu")
            : save.dayWorld.day == 3 && save.dayWorld.beat == DayWorldBeat.EveningConversation ? "jaehoon" : "";

        public static bool NpcAvailable(SaveData save, int day, string npcId) => Active(save) &&
            (save.dayWorld.day == day || save.dayWorld.day == 3 && day == 1 && npcId == "cheolsu") &&
            !string.IsNullOrEmpty(npcId) && RequiredNpc(save) == npcId &&
            !save.dayWorld.completedDialogues.Contains(save.dayWorld.day + ":" + npcId);
        public static bool BedAvailable(SaveData save) => Active(save) &&
            save.dayWorld.beat == DayWorldBeat.Sleep && save.dayWorld.pendingStageClear == 0;
        public static bool ShopAvailable(SaveData save) => Active(save) && CanPurchase(save, StageId(save));
        public static bool EquipmentAvailable(SaveData save, EquipmentId equipment) => Active(save) &&
            Equipment(save) == equipment && save.HasEquipment(equipment) &&
            (save.dayWorld.beat == DayWorldBeat.Placement && !save.dayWorld.placedEquipment.Contains(equipment.ToString()) ||
             save.dayWorld.beat == DayWorldBeat.Practice && save.dayWorld.placedEquipment.Contains(equipment.ToString()));

        public static bool Wake(SaveData save)
        {
            if (!Active(save) || save.dayWorld.beat != DayWorldBeat.Wake) return false;
            save.dayWorld.beat = DayWorldBeat.MorningConversation;
            return true;
        }
        public static bool FinishDialogue(SaveData save, string npcId)
        {
            if (!Active(save) || RequiredNpc(save) != npcId || string.IsNullOrEmpty(npcId)) return false;
            string id = save.dayWorld.day + ":" + npcId;
            if (save.dayWorld.completedDialogues.Contains(id)) return false;
            save.dayWorld.completedDialogues.Add(id);
            save.dayWorld.beat = save.dayWorld.beat == DayWorldBeat.MorningConversation ? DayWorldBeat.Work :
                save.dayWorld.beat == DayWorldBeat.EveningConversation ? DayWorldBeat.Sleep : DayWorldBeat.Purchase;
            return true;
        }
        public static bool CanStart(SaveData save, string stageId) => !Active(save) ||
            (save.dayWorld.beat == DayWorldBeat.Work && StageId(save) == stageId && save.IsStageUnlocked(stageId));
        public static bool CanRecordClear(SaveData save, string stageId) => !Active(save) ||
            (save.dayWorld.beat == DayWorldBeat.Work && StageId(save) == stageId && save.IsStageUnlocked(stageId));
        public static void RecordClear(SaveData save, string stageId)
        {
            if (!Active(save) || !CanRecordClear(save, stageId)) return;
            save.dayWorld.beat = DayWorldBeat.AfterWorkConversation;
            save.dayWorld.checkpointScene = SashimiBoyConstants.Scenes.FishShopDialogue;
            save.dayWorld.checkpointSpawn = "StageReturn";
        }
        public static bool CanPurchase(SaveData save, string stageId) => !Active(save) ||
            (save.dayWorld.beat == DayWorldBeat.Purchase && StageId(save) == stageId);
        public static void RecordPurchase(SaveData save, string stageId)
        {
            if (Active(save) && CanPurchase(save, stageId)) save.dayWorld.beat = DayWorldBeat.Placement;
        }
        public static bool Place(SaveData save, EquipmentId equipment)
        {
            if (!Active(save) || save.dayWorld.beat != DayWorldBeat.Placement || Equipment(save) != equipment || !save.HasEquipment(equipment)) return false;
            string id = equipment.ToString();
            if (!save.dayWorld.placedEquipment.Contains(id)) save.dayWorld.placedEquipment.Add(id);
            save.dayWorld.beat = DayWorldBeat.Practice;
            return true;
        }
        public static bool Practice(SaveData save, EquipmentId equipment)
        {
            if (!Active(save) || save.dayWorld.beat != DayWorldBeat.Practice || Equipment(save) != equipment ||
                !save.dayWorld.placedEquipment.Contains(equipment.ToString()) || save.dayWorld.practicedDays.Contains(save.dayWorld.day)) return false;
            save.dayWorld.practicedDays.Add(save.dayWorld.day);
            save.dayWorld.beat = save.dayWorld.day == 3 ? DayWorldBeat.EveningConversation : DayWorldBeat.Sleep;
            return true;
        }
        public static bool Sleep(SaveData save)
        {
            if (!Active(save) || save.dayWorld.beat != DayWorldBeat.Sleep || !save.dayWorld.practicedDays.Contains(save.dayWorld.day) ||
                !save.dayWorld.placedEquipment.Contains(Equipment(save).ToString()) || save.dayWorld.nightsSlept >= save.dayWorld.day) return false;
            save.dayWorld.nightsSlept = save.dayWorld.day;
            save.dayWorld.pendingStageClear = save.dayWorld.day;
            save.dayWorld.checkpointScene = Home;
            save.dayWorld.checkpointSpawn = "Wake";
            if (save.dayWorld.day < 3) { save.dayWorld.day++; save.dayWorld.beat = DayWorldBeat.Wake; }
            else save.dayWorld.beat = DayWorldBeat.Complete;
            return true;
        }
        public static bool ContinueAfterStageClear(SaveData save)
        {
            if (!Active(save) || save.dayWorld.pendingStageClear < 1 || save.dayWorld.pendingStageClear > 2 ||
                save.dayWorld.day != save.dayWorld.pendingStageClear + 1 || save.dayWorld.beat != DayWorldBeat.Wake ||
                save.dayWorld.nightsSlept < save.dayWorld.pendingStageClear) return false;
            save.dayWorld.pendingStageClear = 0;
            Wake(save);
            return true;
        }
        public static string Objective(SaveData save)
        {
            if (!Active(save)) return "";
            switch (save.dayWorld.beat)
            {
                case DayWorldBeat.Wake: return "새로운 하루를 시작합니다";
                case DayWorldBeat.MorningConversation: return save.dayWorld.day == 1 ? "집 앞에서 미숙과 대화하기" : save.dayWorld.day == 2 ? "출근길에서 성호와 대화하기" : "출근길에서 준석과 대화하기";
                case DayWorldBeat.Work: return save.dayWorld.day == 1 ? "횟집에서 연어 손질을 마치기" : save.dayWorld.day == 2 ? "횟집에서 우럭 손질을 마치기" : "횟집에서 가자미 손질을 마치기";
                case DayWorldBeat.AfterWorkConversation: return save.dayWorld.day == 2 ? "악기 상점 앞에서 민재와 대화하기" : "횟집 손님 자리의 철수와 대화하기";
                case DayWorldBeat.Purchase: return "악기 상점에서 " + ContentDefaults.FindEquipment(Equipment(save)).displayName + " 구매하기";
                case DayWorldBeat.Placement: return "집으로 돌아가 구매한 장비 배치하기";
                case DayWorldBeat.Practice: return "배치한 장비 앞에서 짧게 연습하기";
                case DayWorldBeat.EveningConversation: return "퇴근길에서 재훈과 대화하기";
                case DayWorldBeat.Sleep: return "침대에서 잠들기";
                default: return "현재 준비된 스테이지를 모두 마쳤습니다";
            }
        }
    }
}
