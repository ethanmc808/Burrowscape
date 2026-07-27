using UnityEngine;

// Fixed rarity for the 3 loot kinds with no ScriptableObject asset of their own (Carrot/Potion/
// CrystalCarrot) — same "always has one intrinsic rarity, independent of which band a location's loot
// table rolls it under" rule item assets get via their own `rarity` field. One asset, edited either
// directly in the Inspector or via Burrowscape/Rarity Manager alongside every other item's rarity. See
// Rarity_DesignDoc.md.
[CreateAssetMenu(fileName = "ForagingKindRarityConfig", menuName = "Burrowscape/Foraging Kind Rarity Config")]
public class ForagingKindRarityConfig : ScriptableObject
{
    public ForagingLootRarity carrotRarity = ForagingLootRarity.Common;
    public ForagingLootRarity potionRarity = ForagingLootRarity.Uncommon;
    public ForagingLootRarity crystalCarrotRarity = ForagingLootRarity.Rare;
}
