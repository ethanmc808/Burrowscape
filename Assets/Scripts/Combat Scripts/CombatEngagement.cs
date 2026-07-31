using System.Collections.Generic;
using UnityEngine;

// Shared "who do I shoot, and when" logic for both sides of a base invasion — used by NPCBunny's
// Defending state and EnemyInstance's own tick alike, so the two don't duplicate identical
// targeting/cooldown bookkeeping. Per Ethan's design: in-base combat always targets whoever is closest;
// a future quest system will let the player override a bunny's target by clicking an enemy, with enemies
// still always targeting closest — that override is a separate, not-yet-built concern and doesn't change
// anything here.
//
// Ranged-only for this pass (per Combat_DesignDoc.md's melee/flank rules being a separate, not-yet-
// resolved system) — a melee AttackSource (isMelee true) simply never fires from here; it just stands at
// its CombatSpot/EnemySpot doing nothing until the melee-approach/flanking system is built.
public static class CombatEngagement
{
    // Closest living candidate to fromPosition, or null if none qualify. Candidates already destroyed
    // (CombatGameObject == null, checked instead of a direct null/`!=` compare since a destroyed
    // MonoBehaviour referenced through an interface doesn't trip Unity's overridden null check) or
    // defeated (!IsAlive) are skipped — same fizzle criteria AttackInstance.Update already uses.
    public static ICombatant FindClosestAliveTarget(Vector3 fromPosition, IEnumerable<ICombatant> candidates)
    {
        ICombatant closest = null;
        float closestSqrDistance = float.MaxValue;

        foreach (ICombatant candidate in candidates)
        {
            if (candidate == null || candidate.CombatGameObject == null || !candidate.IsAlive) continue;

            float sqrDistance = (candidate.CombatTransform.position - fromPosition).sqrMagnitude;
            if (sqrDistance < closestSqrDistance)
            {
                closestSqrDistance = sqrDistance;
                closest = candidate;
            }
        }

        return closest;
    }

    // Re-acquires target if it's null/dead/gone, then fires once cooldownRemaining reaches 0 (resetting
    // it from the attacker's own AttackSource.attackIntervalSeconds — NOT a shared/global cadence, since
    // attack animations run different lengths per type). Called once per frame by whichever MonoBehaviour
    // owns target/cooldownRemaining as its own fields. Returns true the one frame an attack actually
    // fires, so the caller knows to trigger its own Animator (ICombatant deliberately has no Animator
    // reference — animation is each concrete type's own concern, not shared targeting logic's).
    public static bool Tick(ICombatant self, ref ICombatant target, ref float cooldownRemaining, IEnumerable<ICombatant> candidatePool)
    {
        if (target == null || target.CombatGameObject == null || !target.IsAlive)
            target = FindClosestAliveTarget(self.CombatTransform.position, candidatePool);

        if (target == null) return false;

        cooldownRemaining -= Time.deltaTime;
        if (cooldownRemaining > 0f) return false;

        if (!TryFireAttack(self, target)) return false;

        cooldownRemaining = self.AttackSource != null ? self.AttackSource.attackIntervalSeconds : 2f;
        return true;
    }

    // Instantiates the attacker's signature AttackInstance prefab at its own current position and
    // launches it at target. Returns false (no cooldown reset, retried next tick) if the attack couldn't
    // fire — no VFX authored yet, melee (not built this pass), or target currently out of range.
    private static bool TryFireAttack(ICombatant attacker, ICombatant target)
    {
        BunnyTypeDefinition attackSource = attacker.AttackSource;
        if (attackSource == null || attackSource.attackVFXPrefab == null) return false;
        if (attackSource.isMelee) return false; // melee approach/flanking not built yet — ranged only this pass

        float distance = Vector3.Distance(attacker.CombatTransform.position, target.CombatTransform.position);
        if (distance > CombatBalanceConfig.Instance.rangedMaxRange) return false;

        GameObject vfxObject = Object.Instantiate(attackSource.attackVFXPrefab, attacker.CombatTransform.position, Quaternion.identity);
        AttackInstance instance = vfxObject.GetComponent<AttackInstance>();
        if (instance == null)
        {
            Debug.LogWarning($"CombatEngagement: {attackSource.displayName}'s attackVFXPrefab has no AttackInstance component.");
            Object.Destroy(vfxObject);
            return false;
        }

        instance.Launch(attacker, target, isStationary: false);
        return true;
    }
}
