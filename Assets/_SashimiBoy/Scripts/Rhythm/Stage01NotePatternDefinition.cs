using System;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    [Serializable]
    public sealed class Stage01PatternNote
    {
        [Min(1)] public int barIndex = 1;
        [Range(0, 7)] public int eighthStepInBar;
        public string label;
    }

    [CreateAssetMenu(
        menuName = "Sashimi Boy/Rhythm/Stage 01 Note Pattern",
        fileName = "Stage01NotePattern")]
    public sealed class Stage01NotePatternDefinition : ScriptableObject
    {
        [Min(1)] public int manualBarCount = 16;
        [Min(1)] public int repeatFromBar = 9;
        [Min(1)] public int subdivisionsPerBeat = 2;
        public List<Stage01PatternNote> notes =
            new List<Stage01PatternNote>();
    }
}
