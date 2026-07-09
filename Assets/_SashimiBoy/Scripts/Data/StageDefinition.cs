using System;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    [CreateAssetMenu(menuName = "Sashimi Boy/Data/Stage Definition", fileName = "StageDefinition")]
    public sealed class StageDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string stageId;
        public string displayName;
        public int order;
        public FishType fishType;

        [Header("Music / Genre")]
        public string hiphopGenre;
        public float bpm = 90f;
        public RhythmDifficulty defaultDifficulty = RhythmDifficulty.Normal;

        [Header("Progression")]
        public string sceneName = SashimiBoyConstants.Scenes.FishStageTemplate;
        public string requiredPreviousStageId;
        public string nextStageId;
        public EquipmentId rewardEquipment;
        public int rewardPlates = 1;
        public int requiredPlatesForExchange = 1;
        public bool implementedInPrototype = true;

        [Header("Narrative Hooks")]
        [TextArea(2, 4)] public string inspirationPopup;
        [TextArea(2, 4)] public string kevinShopRequest;
        [TextArea(2, 4)] public string shopkeeperRecommendation;

        [Header("Distraction Cues")]
        public List<DistractionCue> distractionCues = new List<DistractionCue>();

        public bool HasReward => !string.IsNullOrWhiteSpace(stageId);
    }

    [Serializable]
    public sealed class DistractionCue
    {
        public DistractionType type;
        public float beat;
        public float durationBeats = 1f;
        public float intensity = 1f;
        public string label;
    }
}
