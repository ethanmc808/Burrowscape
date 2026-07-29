using UnityEngine;

// One catalog entry per base-invasion pest enemy, mirroring BunnyTypeDefinition's one-asset-per-entry
// pattern. Enemies are simple stat-sticks, not individual characters — no IV/EV/Nature/traits, just
// flat stats. Attack power is NOT flat, though — enemies level (see Combat_DesignDoc.md's "Enemy
// leveling by population"), so their attack's Base Power scales via CombatMath.GetBasePower(level) at
// runtime exactly like a bunny's, using whatever level the spawned instance was given. Nothing on this
// asset stores that level or a base power — there's no per-instance level field here because
// EnemyDefinition is a shared template, same relationship BunnyTypeDefinition has to a bunny's own level.
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
}
