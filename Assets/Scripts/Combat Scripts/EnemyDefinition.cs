using UnityEngine;

// One catalog entry per base-invasion pest enemy, mirroring BunnyTypeDefinition's one-asset-per-entry
// pattern. Enemies are simple stat-sticks, not individual characters — no IV/EV/Nature/traits, just
// flat stats and a single fixed-power attack.
[CreateAssetMenu(fileName = "EnemyDefinition", menuName = "Burrowscape/Enemy Definition")]
public class EnemyDefinition : ScriptableObject
{
    [Header("Identity")]
    public string displayName;
    public BunnyType type;
    [Tooltip("Null until this enemy's art/rig exists.")]
    public GameObject prefab;

    [Header("Base Stats")]
    public int baseHP;
    public int baseAttack;
    public int baseDefense;
    public int baseSpeed;
    public int baseLuck;

    [Header("Attack")]
    [Tooltip("Reused for this enemy's attack name/animation/VFX instead of authoring enemy-specific art. Must match this enemy's type.")]
    public BunnyTypeDefinition attackSource;
    [Tooltip("Enemies don't level, so this is a flat value rather than computed via CombatMath.GetBasePower. Use one of the 5 tier values (20/40/60/80/100) to stay consistent with bunny attacks.")]
    public int basePower;
}
