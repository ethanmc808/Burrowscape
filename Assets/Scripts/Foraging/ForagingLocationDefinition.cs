using System;
using System.Collections.Generic;
using UnityEngine;

public enum ForagingLootRarity { Common, Uncommon, Rare }

// Gold is deliberately NOT one of these — it's a separate per-tick mechanic driven by
// ForagingDifficultyTierConfig instead (see the design doc's "Loot economy" section: Gold doesn't count
// toward the carry limit and its amount scales with difficulty tier, not rarity band).
public enum ForagingLootKind { Carrot, Potion, Accessory }

[Serializable]
public class ForagingLootEntry
{
    public ForagingLootRarity rarity = ForagingLootRarity.Common;
    public ForagingLootKind kind = ForagingLootKind.Carrot;
    [Tooltip("Only used when kind == Accessory.")]
    public ForagingAccessoryDefinition accessory;
    [Tooltip("Amount granted for a Carrot/Potion find (rolled between min/max, inclusive). Ignored for Accessory finds, which always grant 1.")]
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

    [Header("Loot Table (Common/Uncommon/Rare — Carrots/Potions/Accessories; Gold is separate, see ForagingDifficultyTierConfig)")]
    public List<ForagingLootEntry> lootTable = new List<ForagingLootEntry>();

    // Uses this asset's own name as its unique ID, same reasoning as RoomDefinition.Id — asset names are
    // already unique within a folder.
    public string Id => name;
}
