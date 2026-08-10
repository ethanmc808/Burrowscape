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
    [Tooltip("Egg.cs's SpriteRenderer for this type — a litter's shared Type selects which color egg shows in the Hatchery (see the Breeding System plan). Null until this type's egg art exists; Egg falls back to whatever sprite is already on its prefab's SpriteRenderer if unset, same 'not yet authored isn't an error state' philosophy as icon above.")]
    public Sprite eggSprite;

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
    [Tooltip("This type's signature attack's Base Power at level 1-9 (tier 0) — see CombatMath.GetBasePower, which multiplies this by the level tier (x1 at 1-9, x2 at 10-19, ... x5 at 40+; same tier shape for every type). Per-type rather than a shared global 20, since attackIntervalSeconds varies a lot by type (Fire fires every 1s, Plant every 5s) — at equal Base Power a slow attacker does far less DPS than a fast one. Ethan's explicit call: hand-tune this per type rather than auto-deriving it from interval, since base Attack stat also differs by type. Tune directly here or via the Bunny Base Stats Editor grid.")]
    public int attackBasePower = 20;

    [Header("Unlock")]
    [Tooltip("Organizational only (matches the design groupings) — actual gating is populationThreshold.")]
    public int group;
    [Tooltip("Population needed to permanently unlock this type — see BunnyTypeUnlockTracker.")]
    public int populationThreshold;

    [Header("Passives (unique per type, not random)")]
    [Tooltip("Empty until content is authored. A passive is active once the bunny's level >= unlockLevel.")]
    public List<BunnyPassiveDefinition> passives = new List<BunnyPassiveDefinition>();
}

// First entry with an actual effect hook is Plant's "Regrowth" (HPRegenMultiplier) — see
// NPCBunny.GetPassiveHPRegenMultiplier. Deliberately separate from TraitEffectType/BunnyTraitDefinition:
// a Passive is universal to every bunny of a given TYPE (authored here, on BunnyTypeDefinition), whereas
// a Trait is a per-individual roll. Add new cases here as more passives get built.
//
// TraitReroll (Neutral's "Adaptable", see NPCBunny.TryRerollTrait/CanRerollTrait) is the second entry and
// doesn't fit the "read effectMultiplier as a stat multiplier" shape HPRegenMultiplier uses — it grants an
// activatable ability with a cooldown instead of a passive stat effect, hence BunnyPassiveDefinition's
// separate abilityCooldownSeconds field below rather than overloading effectMultiplier's meaning.
public enum PassiveEffectType
{
    None,
    HPRegenMultiplier,
    TraitReroll,
}

[System.Serializable]
public class BunnyPassiveDefinition
{
    public string id;
    public string displayName;
    [TextArea] public string description;
    public int unlockLevel = 1;
    public PassiveEffectType effectType;
    public float effectMultiplier = 1f;
    [Tooltip("Only used when effectType == TraitReroll — how long after use before the ability is available again. Placeholder value, needs playtesting.")]
    public float abilityCooldownSeconds = 1f;
    [Tooltip("Whether this passive has actually been unlocked yet. Defaults to true so every passive authored before this field existed (e.g. Plant's Regrowth) keeps working unchanged — set explicitly to false for passives that are meant to be dormant until some other system unlocks them (e.g. Neutral's Adaptable, gated behind Ghost's Ancient Knowledge / Shrine Room — see BunnyTypeNiches_DesignDoc.md — which doesn't exist in code yet, so this has no unlock path today beyond hand-flipping it in the Inspector). Checked by both BunnyPassiveResolver.ResolvePassives and any live read like GetPassiveHPRegenMultiplier/GetAdaptablePassive, so an undiscovered passive never shows up as active anywhere.")]
    public bool discovered = true;
}
