using UnityEngine;

// Which raw Material family a Trinket crafts down into at a future Workshop room (see the Foraging Trip
// Detail Panel + Item Expansion design doc) — None means collectible/sellable only, no material output.
public enum TrinketCraftFamily { None, Cloth, Metal }

// One asset per trinket (20 total in the draft roster) — mirrors ForagingAccessoryDefinition's
// one-asset-per-entry shape. All trinkets are sellable regardless of craftsInto; only some also craft.
[CreateAssetMenu(fileName = "ForagingTrinketDefinition", menuName = "Burrowscape/Foraging Trinket Definition")]
public class ForagingTrinketDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public Sprite icon;
    [TextArea] public string description;
    [Tooltip("Fixed rarity for this item, independent of which weight band a location's loot table rolls it under — see Rarity_DesignDoc.md. Edit here or via Burrowscape/Rarity Manager.")]
    public ForagingLootRarity rarity = ForagingLootRarity.Common;

    [Header("Sell (all trinkets are sellable regardless of craftsInto)")]
    public int sellValue = 5;

    [Header("Crafting (future Workshop room)")]
    [Tooltip("None = collectible/sellable only, no material output. The tier produced when crafted is this trinket's own fixed rarity above, not the location entry that dropped it.")]
    public TrinketCraftFamily craftsInto = TrinketCraftFamily.None;
}
