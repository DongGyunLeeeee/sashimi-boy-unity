using UnityEngine;

namespace SashimiBoy
{
    public static class SashimiBoyConstants
    {
        public const string GameRootName = "SashimiBoy_Runtime";

        public static class Scenes
        {
            public const string Bootstrap = "Bootstrap";
            public const string Street = "Street";
            public const string FishShopDialogue = "FishShopDialogue";
            public const string EquipmentShop = "EquipmentShop";
            public const string Club = "Club";
            public const string Stage01Salmon = "Stage01_Salmon";
            public const string StageSelect = "StageSelect";
            public const string FishStageTemplate = "FishStageTemplate";
        }

        public static class StageIds
        {
            public const string Salmon = "STAGE_01_SALMON";
            public const string Rockfish = "STAGE_02_ROCKFISH";
            public const string Sole = "STAGE_03_SOLE";
            public const string Mullet = "STAGE_04_MULLET";
            public const string Mackerel = "STAGE_05_MACKEREL";
            public const string Yellowtail = "STAGE_06_YELLOWTAIL";
            public const string RoughscaleSole = "STAGE_07_ROUGHSCALE_SOLE";
            public const string ConvictGrouper = "STAGE_08_CONVICT_GROUPER";
            public const string Tuna = "STAGE_09_TUNA";
            public const string SawedgedPerch = "STAGE_10_SAWEDGED_PERCH";
        }

        public static class SaveKeys
        {
            public const string SaveFileName = "sashimi_boy_save.json";
        }
    }
}
