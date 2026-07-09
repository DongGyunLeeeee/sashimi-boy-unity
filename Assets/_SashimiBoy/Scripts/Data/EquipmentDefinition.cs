using UnityEngine;

namespace SashimiBoy
{
    [CreateAssetMenu(menuName = "Sashimi Boy/Data/Equipment Definition", fileName = "EquipmentDefinition")]
    public sealed class EquipmentDefinition : ScriptableObject
    {
        public EquipmentId equipmentId;
        public string displayName;
        [TextArea(2, 4)] public string role;
        [TextArea(2, 4)] public string experienceChange;
        [TextArea(2, 4)] public string visualFeedback;
        public Sprite icon;
    }
}
