using UnityEngine;

public enum ForagingMaterialType { Cloth, Metal, Herb }

// One asset per material TYPE, not per tier (Cloth, Metal, Herb — 3 assets total) — tier is a
// (ForagingMaterialDefinition, ForagingLootRarity) dictionary key in ForagingInventoryManager.materialStock,
// never a separate asset. Cloth/Metal only ever enter stock via crafting a same-rarity Trinket
// (ForagingInventoryManager.TryCraftTrinketIntoMaterial) — Herb is the one exception, granted directly by a
// Herb-kind loot roll since it's already usable as found, no crafting step. See the Foraging Trip Detail
// Panel + Item Expansion design doc.
[CreateAssetMenu(fileName = "ForagingMaterialDefinition", menuName = "Burrowscape/Foraging Material Definition")]
public class ForagingMaterialDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("\"Cloth\", \"Metal\", \"Herb\" — a tier prefix (Common/Fine/Rare) is added at display time, not stored here.")]
    public string displayName;
    [TextArea] public string description;

    public ForagingMaterialType materialType;

    [Header("Icons (one asset spans all 3 tiers, so each tier needs its own art)")]
    public Sprite commonIcon;
    public Sprite fineIcon;
    public Sprite rareIcon;

    public Sprite GetIcon(ForagingLootRarity rarity)
    {
        switch (rarity)
        {
            case ForagingLootRarity.Uncommon: return fineIcon;
            case ForagingLootRarity.Rare: return rareIcon;
            default: return commonIcon;
        }
    }
}
