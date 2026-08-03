using UnityEngine;

// Shared surface for anything that can fight — implemented by NPCBunny and EnemyInstance. Lets
// CombatResolver/AttackInstance/StatusEffectController work against either without caring which. Base
// (pre-status-modifier) stats only — StatusEffectController (optional component, looked up via
// CombatGameObject) layers temporary debuffs on top at the point of use; nothing here mutates Stats
// itself, matching how bunny Stats is otherwise only ever recomputed wholesale via BunnyStatCalculator.
public interface ICombatant
{
    BunnyType Type { get; }
    int Level { get; }
    BunnyStats Stats { get; }

    int CurrentHP { get; }
    bool IsAlive { get; }

    // The BunnyTypeDefinition this combatant's signature attack identity (name/isMelee/VFX) comes from —
    // its own typeDefinition for a bunny, EnemyDefinition.attackSource for an enemy. Single source of
    // truth so AttackInstance/CombatResolver never need to branch on "is this a bunny or an enemy."
    BunnyTypeDefinition AttackSource { get; }

    Transform CombatTransform { get; }
    GameObject CombatGameObject { get; }

    // Where attack VFX prefabs spawn from — CombatTransform.position plus each combatant's own
    // per-instance offset, so a projectile can leave from mouth height instead of the root pivot
    // (floor level) without disturbing CombatTransform itself, which targeting/distance math elsewhere
    // still relies on being the root.
    Vector3 AttackOrigin { get; }

    // The actual visual midpoint of this combatant's sprite(s) right now — computed from live renderer
    // bounds (see CombatEngagement.ComputeVisualCenter), not a hand-tuned offset like AttackOrigin. Used
    // as the arrival/homing point for attacks (AttackInstance) so a projectile lands center-of-body
    // regardless of whether CombatTransform's root pivot happens to sit at floor level, chest height, etc.
    // on any given prefab.
    Vector3 VisualCenter { get; }

    // True when this combatant's sprite(s) currently face right — same sign convention each implementer's
    // own SetFacing/visual-scale-root already uses internally. Lets AttackInstance mirror a stationary
    // (melee) attack's VFX to match whichever side the attacker is actually facing, without AttackInstance
    // needing to know whether it's working with a bunny or an enemy.
    bool IsFacingRight { get; }

    // Reduces HP, can reach 0 (unlike NPCBunny.ApplyForagingDamage's floor-at-1 — that method is a
    // separate, deliberately-non-lethal Foraging placeholder, not reused here). Implementers fire
    // OnDefeated exactly once, the moment CurrentHP first reaches 0.
    void TakeCombatDamage(int amount);

    event System.Action OnDefeated;
}
