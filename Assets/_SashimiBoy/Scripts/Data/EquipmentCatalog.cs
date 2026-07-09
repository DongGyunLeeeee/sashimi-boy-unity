using System.Collections.Generic;
using UnityEngine;

namespace SashimiBoy
{
    [CreateAssetMenu(menuName = "Sashimi Boy/Data/Equipment Catalog", fileName = "EquipmentCatalog")]
    public sealed class EquipmentCatalog : ScriptableObject
    {
        public List<EquipmentDefinition> equipment = new List<EquipmentDefinition>();

        public EquipmentDefinition Find(EquipmentId equipmentId)
        {
            for (int i = 0; i < equipment.Count; i++)
            {
                EquipmentDefinition item = equipment[i];
                if (item != null && item.equipmentId == equipmentId)
                {
                    return item;
                }
            }

            return null;
        }
    }
}
