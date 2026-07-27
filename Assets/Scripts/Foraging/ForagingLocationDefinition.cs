using System;
using System.Collections.Generic;
using UnityEngine;

public enum ForagingLootRarity { Common, Uncommon, Rare }

// Presentation-layer only (e.g. building "Fine Cloth") — not a new data field, just how a
// ForagingLootRarity-tiered Material displays. See the Foraging Trip Detail Panel + Item Expansion
// design doc's Materials section for why Common/Uncommon/Rare aren't used verbatim here.
public static class ForagingRarityDisplay
{
    public static string GetTierLabel(ForagingLootRarity rarity)
    {
        switch (rarity)
        {
            case ForagingLootRarity.Uncommon: return "Fine";
            case ForagingLootRarity.Rare: return "Rare";
            default: return "Common";
        }
    }
}

// Gold is deliberately NOT one of these — it's a separate per-tick mechanic driven by
// ForagingDifficultyTierConfig instead (see the design doc's "Loot economy" section: Gold doesn't count
// toward the carry limit and its amount scales with difficulty tier, not rarity band).
// Herb IS a ForagingMaterialType (see ForagingMaterialDefinition) granted directly by this roll, at
// whatever rarity the entry rolled at — Cloth/Metal (the other two Material types) are deliberately absent
// here since those are never rolled, only produced by crafting a Trinket (see the Foraging Trip Detail
// Panel + Item Expansion design doc).
public enum ForagingLootKind { Carrot, Potion, Accessory, Fruit, Trinket, Herb, CrystalCarrot }

[Serializable]
public class ForagingLootEntry
{
    public ForagingLootRarity rarity = ForagingLootRarity.Common;
    public ForagingLootKind kind = ForagingLootKind.Carrot;
    [Tooltip("Only used when kind == Accessory.")]
    public ForagingAccessoryDefinition accessory;
    [Tooltip("Only used when kind == Fruit.")]
    public ForagingFruitDefinition fruit;
    [Tooltip("Only used when kind == Trinket.")]
    public ForagingTrinketDefinition trinket;
    [Tooltip("Amount granted for a Carrot/Potion/Herb/CrystalCarrot find (rolled between min/max, inclusive). Ignored for Accessory finds, which always grant 1. Herb needs no asset reference of its own — rarity alone identifies which Herb tier is granted, see ForagingMaterialType.Herb.")]
    public int minAmount = 1;
    public int maxAmount = 3;
}

// One asset per location — mirrors BunnyTypeDefinition/RoomDefinition's one-asset-per-entry pattern.
// See Foraging_DesignDoc.md's "Locations" section. Not all 6 unlock tiers need a location authored yet,
// same as the Bunny Type roster's Group-1-only-has-art approach.
[CreateAssetMenu(fileName = "ForagingLocationDefinition", menuName = "Burrowscape/Foraging Location Definition")]
public class ForagingLocationDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public Sprite icon;

    [Header("Unlock")]
    [Tooltip("Population needed to permanently unlock this location — see ForagingLocationUnlockTracker. Staggered against BunnyTypeDefinition's 25/50/75/100/150/200 thresholds on purpose.")]
    public int populationThreshold;

    [Header("Difficulty")]
    public ForagingDifficultyTier difficultyTier = ForagingDifficultyTier.Weak;
    [Tooltip("A bunny whose Type is in this list gets a tick-rate bonus AND a flat trip-wide XP multiplier — see ForagingManager. A list, not a single type, kept flexible even though early locations only use one entry.")]
    public List<BunnyType> recommendedTypes = new List<BunnyType>();

    [Header("Loot Table (Common/Uncommon/Rare — Carrots/Potions/Accessories/Fruits/Trinkets/Herbs/Crystal Carrots; Gold is separate, see ForagingDifficultyTierConfig)")]
    public List<ForagingLootEntry> lootTable = new List<ForagingLootEntry>();

    [Header("Encounter Flavor (log text only — no gameplay effect)")]
    [Tooltip("Include the article, e.g. \"a Wasp\", \"an Ash Hound\" — ResolveEncounter picks one at random per encounter roll purely to word the trip-log line. Empty list falls back to \"a wild creature\".")]
    public List<string> enemyNames = new List<string>();

    // Uses this asset's own name as its unique ID, same reasoning as RoomDefinition.Id — asset names are
    // already unique within a folder.
    public string Id => name;
}
