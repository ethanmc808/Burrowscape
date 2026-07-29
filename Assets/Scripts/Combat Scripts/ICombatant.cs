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

    // Reduces HP, can reach 0 (unlike NPCBunny.ApplyForagingDamage's floor-at-1 — that method is a
    // separate, deliberately-non-lethal Foraging placeholder, not reused here). Implementers fire
    // OnDefeated exactly once, the moment CurrentHP first reaches 0.
    void TakeCombatDamage(int amount);

    event System.Action OnDefeated;
}
