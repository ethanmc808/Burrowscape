using System.Collections.Generic;
using UnityEngine;

// Traits are not unique per type (unlike Passives) and have no content authored yet — this is a flat
// pool edited together, closer to WildBunnyNames' serialized-array pattern (NPC Bunny
// Scripts/WildBunnyNames.cs) than to a one-asset-per-entry catalog, since there's no per-trait prefab
// or unique behavior to justify a ScriptableObject each.
//
// Deliberately kept in its own file, separate from BunnyTraitCatalog (the MonoBehaviour that holds a
// list of these) — a MonoBehaviour needs to be the one-and-only class in a file matching that file's
// name to get its own MonoScript asset, which is what Unity's Add Component search/browse actually
// resolves against. Pairing a differently-named MonoBehaviour into this file used to silently hide
// BunnyTraitCatalog from Add Component even though it compiled fine — the type existed, it just had no
// MonoScript of its own for the Editor UI to point at.
//
// Proof-of-concept effect model: each trait scales exactly ONE of a bunny's rates by a flat multiplier.
// Combat/quest traits aren't listed here yet (no combat/quest system to hook into) — this enum only
// covers the base-related traits implemented so far. Extend with new cases as more traits get real
// effects; a trait with effectType == None is purely cosmetic (matches every trait before this pass).
public enum TraitEffectType
{
    None,
    EnergyDecayMultiplier,    // NPCBunny's energy decay rates (all contexts — passive, Working, Questing, Foraging)
    MoodDecayMultiplier,      // NPCBunny's mood DECAY rates only (Working, idle-pacing) — gain rates untouched
    HungerDecayMultiplier,    // NPCBunny.hungerDecayPerSecond
    ThirstDecayMultiplier,    // NPCBunny.thirstDecayPerSecond
    MoveSpeedMultiplier,      // NPCBunny.moveSpeed
    ProductionMultiplier,     // Read by GardenRoom/WaterRoom when computing a working bunny's output. NOT wired into CoalRoom/PowerManager yet — its headcount-based production model would need restructuring first (deliberately deferred, see BunnyTypeSystem_DesignDoc.md).
    IgnoresNature,            // Suppresses NPCBunny's Nature (Zodiac) stat modifier entirely (used by the Stoic trait) — unlike every case above, this doesn't scale one of the bunny's OWN rates, it suppresses another system's output. See NPCBunny.ApplyNatureEffects.
    RareLootChanceBonus,      // Foraging's Treasure Finder trait. ADDITIVE percentage points (not a multiplier like every case above) into NPCBunny.ForagingRareLootBonus, read by ForagingManager's loot roll — stacks alongside Luck's own separate contribution to the same roll rather than competing with it. See Foraging_DesignDoc.md.
    XPGainMultiplier,         // NPCBunny.XPGainMultiplier — applied universally to XP from every source (Foraging, Work Rooms, and any future source), inside NPCBunny.AddExperience itself. See WorkRoomXP_DesignDoc.md.
}

[System.Serializable]
public class BunnyTraitDefinition
{
    public string id;
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Traits that can never co-occur with this one on the same bunny, e.g. Energetic/Lazy.")]
    public List<string> incompatibleTraitIds = new List<string>();

    [Header("Effect (proof of concept — one multiplier per trait)")]
    [Tooltip("Which of the bunny's rates this trait scales. None = purely cosmetic, no gameplay effect.")]
    public TraitEffectType effectType = TraitEffectType.None;
    [Tooltip("Flat multiplier applied wherever effectType says it applies — e.g. 1.25 = 25% faster/more, 0.75 = 25% slower/less.")]
    public float effectMultiplier = 1f;
}
