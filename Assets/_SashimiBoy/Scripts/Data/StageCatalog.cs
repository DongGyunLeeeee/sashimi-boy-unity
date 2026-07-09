using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    [CreateAssetMenu(menuName = "Sashimi Boy/Data/Stage Catalog", fileName = "StageCatalog")]
    public sealed class StageCatalog : ScriptableObject
    {
        public List<StageDefinition> stages = new List<StageDefinition>();

        public StageDefinition FindById(string stageId)
        {
            if (string.IsNullOrWhiteSpace(stageId))
            {
                return null;
            }

            for (int i = 0; i < stages.Count; i++)
            {
                StageDefinition stage = stages[i];
                if (stage != null && stage.stageId == stageId)
                {
                    return stage;
                }
            }

            return null;
        }
    }
}
