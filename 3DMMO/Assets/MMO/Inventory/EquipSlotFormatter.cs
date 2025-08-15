using System.Text;
using MMO.Shared.Item;

namespace MMO.Inventory
{
    public static class EquipSlotFormatter
    {
        public static string MaskToNiceString(EquipSlot mask)
        {
            if (mask == EquipSlot.None) return "Not equippable";
            var sb = new StringBuilder();
            foreach (EquipSlot v in System.Enum.GetValues(typeof(EquipSlot)))
            {
                if (v == EquipSlot.None) continue;
                if ((mask & v) != 0)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(v);
                }
            }
            return sb.ToString();
        }
    }
}
