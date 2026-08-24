using System;
using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    [Serializable]
    public sealed class JudgementVisualDefinition
    {
        public JudgeGrade grade;
        public string displayLabel;
        public Sprite sprite;
    }

    [CreateAssetMenu(
        menuName = "Sashimi Boy/Art/Judgement Visual Library",
        fileName = "JudgementVisualLibrary")]
    public sealed class JudgementVisualLibrary : ScriptableObject
    {
        public List<JudgementVisualDefinition> visuals =
            new List<JudgementVisualDefinition>();

        public bool TryGet(
            JudgeGrade grade,
            out JudgementVisualDefinition definition)
        {
            for (int i = 0; i < visuals.Count; i++)
            {
                JudgementVisualDefinition candidate = visuals[i];
                if (candidate != null && candidate.grade == grade)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }
    }
}
