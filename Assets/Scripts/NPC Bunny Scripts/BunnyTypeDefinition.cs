using System.Collections.Generic;
using UnityEngine;

// One catalog entry per BunnyType (see BunnyTypeSystem_DesignDoc.md at the project root), mirroring
// RoomDefinition's one-asset-per-entry pattern (Room Scripts/RoomDefinition.cs). Base stats here are
// placeholders (source: BunnyBaseStats.xlsx) — edit directly in the Inspector, or via the bulk grid
// editor at Burrowscape > Bunny Base Stats Editor (Editor/BunnyBaseStatsWindow.cs).
[CreateAssetMenu(fileName = "BunnyTypeDefinition", menuName = "Burrowscape/Bunny Type Definition")]
public class BunnyTypeDefinition : ScriptableObject
{
    [Header("Identity")]
    public BunnyType type;
    public string displayName;
    [Tooltip("Shown next to the Type label in BunnyInfoUI. Null until custom type-symbol art exists for this type — the icon Image just hides itself in that case.")]
    public Sprite icon;

    [Header("Prefab")]
    [Tooltip("Null until this type's art/rig exists — WildBunnySpawner skips types with no prefab even if population-unlocked.")]
    public GameObject prefab;

    [Header("Base Stats (placeholders — see BunnyBaseStats.xlsx)")]
    public int baseHP;
    public int baseAttack;
    public int baseDefense;
    public int baseSpeed;
    public int baseLuck;

    public int BaseTotal => baseHP + baseAttack + baseDefense + baseSpeed + baseLuck;

    [Header("Combat")]
    [Tooltip("Every type has exactly one signature attack; its Base Power scales with level via CombatMath.GetBasePower, not authored here. Empty until this type's attack is designed — enemies of this type (see EnemyDefinition.attackSource) reuse this same identity/animation/VFX rather than getting their own.")]
    public string attackName;
    [Tooltip("Ranged (false) can fire from a distance (see CombatBalanceConfig.rangedMaxRange); melee (true) must stand adjacent, per-type, not a fixed roster. Neutral/Melee are melee; most other types are ranged. Assign whenever this type's attack actually gets designed.")]
    public bool isMelee;
    [Tooltip("The particle-effect AttackInstance prefab for this type's signature attack — layered on top of the existing Attack animation, not a replacement for it. Null until this type's VFX is authored.")]
    public GameObject attackVFXPrefab;
    [Tooltip("How often this type can fire its attack once engaged, in seconds — per-type rather than a shared global value, since attack animations run different lengths (Fire Ball is much quicker than Giga Drain). Speed does NOT affect this — Speed only affects hit/evasion chance (see CombatMath.GetHitChance); this is purely the animation-driven cadence.")]
    public float attackIntervalSeconds = 2f;

    [Header("Unlock")]
    [Tooltip("Organizational only (matches the design groupings) — actual gating is populationThreshold.")]
    public int group;
    [Tooltip("Population needed to permanently unlock this type — see BunnyTypeUnlockTracker.")]
    public int populationThreshold;

    [Header("Passives (unique per type, not random)")]
    [Tooltip("Empty until content is authored. A passive is active once the bunny's level >= unlockLevel.")]
    public List<BunnyPassiveDefinition> passives = new List<BunnyPassiveDefinition>();
}

[System.Serializable]
public class BunnyPassiveDefinition
{
    public string id;
    public string displayName;
    [TextArea] public string description;
    public int unlockLevel = 1;
    // No effect/behavior hook yet — that gameplay system doesn't exist. This is a pure data tag today.
}
