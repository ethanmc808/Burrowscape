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

    // Raw "is this combatant's rig art currently X-mirrored from its own authored default pose" signal —
    // true exactly when the implementer's own scale-flip transform (bunnyScaleRoot / visualScaleRoot) has
    // a negative local X scale right now. Distinct from IsFacingRight, which normalizes each rig's own
    // facesLeftByDefault-style convention into a human-readable "facing screen-right" bool and therefore
    // can't tell FloatingComboEffect which raw sign to counter. Needed because FloatingComboEffect is
    // spawned as a child of CombatTransform (for correct world position/scale — the actual flip transform
    // sits 1-2 levels deeper on every current rig and is scaled far smaller, e.g. bunnyScaleRoot at 0.075x),
    // so it can never see the flip via ordinary Transform-hierarchy inheritance (Transform.lossyScale only
    // ever composes ANCESTORS, never descendants) — confirmed backwards on slimes despite looking fine on
    // bunnies (2026-08-03): both were actually rendering a constant, non-reactive orientation the whole
    // time, since CombatTransform's own scale never changes for either rig. This is passed explicitly into
    // FloatingComboEffect.Show instead of relying on that broken inference.
    bool IsVisuallyMirrored { get; }

    // Reduces HP, can reach 0 (unlike NPCBunny.ApplyForagingDamage's floor-at-1 — that method is a
    // separate, deliberately-non-lethal Foraging placeholder, not reused here). Implementers fire
    // OnDefeated exactly once, the moment CurrentHP first reaches 0.
    void TakeCombatDamage(int amount);

    // Briefly tints every sprite this combatant is made of to `color` (fade in, hold, fade out — see
    // CombatBalanceConfig.hitFlashFadeInSeconds/hitFlashFadeOutSeconds) then restores the original color.
    // Called by AttackInstance.Resolve() on a landed hit, passing TypeHitFlashPalette.GetColor(attacker.
    // Type) — the flash represents the ATTACK's type, not the target's own.
    void PlayHitFlash(Color color);

    event System.Action OnDefeated;
}
