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
