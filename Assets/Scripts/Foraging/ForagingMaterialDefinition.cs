using UnityEngine;

public enum ForagingMaterialType { Cloth, Metal, Herb }

// One asset per material TIER now, not per family (5 Cloth assets, 5 Metal assets, 5 Herb assets — e.g.
// Mint Leaf/Silverwort/Moonpetal/Sparkling Thistle/Golden Lotus Petal for Herb) — see Rarity_DesignDoc.md.
// Each asset has its own fixed rarity, same shape as Fruit/Trinket/Accessory. materialType groups the
// family (Cloth/Metal/Herb) for crafting lookups (ForagingInventoryManager.clothTiers/metalTiers/
// herbTiers), orthogonal to rarity. Cloth/Metal only ever enter stock via crafting a Trinket
// (ForagingInventoryManager.TryCraftTrinketIntoMaterial, tier = the trinket's own fixed rarity) — Herb is
// the one exception, granted directly by a Herb-kind loot roll since it's already usable as found, no
// crafting step. See the Foraging Trip Detail Panel + Item Expansion design doc.
[CreateAssetMenu(fileName = "ForagingMaterialDefinition", menuName = "Burrowscape/Foraging Material Definition")]
public class ForagingMaterialDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public Sprite icon;
    [TextArea] public string description;
    [Tooltip("Fixed rarity for this item, independent of which weight band a location's loot table rolls it under — see Rarity_DesignDoc.md. Edit here or via Burrowscape/Rarity Manager.")]
    public ForagingLootRarity rarity = ForagingLootRarity.Common;

    [Tooltip("Which family this tier belongs to (Cloth/Metal/Herb) — used to look this asset up by tier in ForagingInventoryManager.")]
    public ForagingMaterialType materialType;
}
