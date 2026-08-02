using UnityEngine;

// Optional add-on component (same "put it on any NPCBunny GameObject" convention as
// StatusEffectController) — tracks whether this bunny is currently posted at a Guard Room and, if so,
// what grade. Deliberately separate from StatusEffectController rather than folded into it: that
// component tracks only ONE active status at a time, but a guarding bunny needs both a status debuff
// (e.g. Burn) AND its guard buff active simultaneously — CombatResolver consults both independently.
//
// Not routed through the Trait system (BunnyTraitDefinition/ApplyTraitEffects) despite the original
// design's intent to extend it with an "Assignment" category — ApplyTraitEffects is confirmed not safely
// re-runnable for most of its effect types (no stored "base" value to reset from before reapplying), and
// Attack/Defense/Speed are resolved BunnyStats values, not raw mutable fields like the decay rates that
// system was built around. This mirrors StatusEffectController's proven "modifier applied at the point of
// use" shape instead, which already solves exactly this problem for temporary buffs/debuffs.
//
// Granted/revoked directly by GuardRoom on assignment/unassignment (NotifyBunnyReadyToWork/ReleaseSpot/
// NotifyBunnyLeavingToEat/OnRoomShutdown) — no persistence, no timer, full reset lifecycle.
public class GuardBuffController : MonoBehaviour
{
    private int guardGrade; // 0 = not currently guarding

    public bool IsGuarding => guardGrade > 0;

    public void SetGuarding(int grade) => guardGrade = grade;
    public void ClearGuarding() => guardGrade = 0;

    // ---------- Stat modifiers, applied on top of the owner's base Stats at point of use ----------
    // (CombatResolver calls these instead of reading owner.Stats.Attack/Defense/Speed directly, chained
    // alongside StatusEffectController's own Modify* calls at the same call sites.)

    public int ModifyAttack(int baseAttack) =>
        guardGrade >= 1 ? Mathf.RoundToInt(baseAttack * CombatBalanceConfig.Instance.guardGrade1AttackMultiplier) : baseAttack;

    public int ModifyDefense(int baseDefense) =>
        guardGrade >= 1 ? Mathf.RoundToInt(baseDefense * CombatBalanceConfig.Instance.guardGrade1DefenseMultiplier) : baseDefense;

    // Grade 3 only — Grade 1/2 guards get no Speed change.
    public int ModifySpeed(int baseSpeed) =>
        guardGrade >= 3 ? Mathf.RoundToInt(baseSpeed * CombatBalanceConfig.Instance.guardGrade3SpeedMultiplier) : baseSpeed;
}
