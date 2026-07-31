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
//
// Attack firing is split into two phases so the VFX/damage actually lines up with the attack animation's
// own "release" frame (an Animation Event on each type's Attacking clip), instead of firing instantly the
// moment cooldown expires: TryBeginAttack starts the wind-up (triggers the animation, snapshots the
// target), and ReleaseAttack — called by that Animation Event — is what actually spawns the AttackInstance.
// Cooldown resets at wind-up start, not at release, so attackIntervalSeconds represents the whole attack
// cycle (matching each type's own animation length) regardless of how the split plays out; a future
// attack-speed perk/trait would only ever need to scale attackIntervalSeconds, and the animation itself
// (playing at its own authored speed) still tells ReleaseAttack exactly when to fire.
public static class CombatEngagement
{
    // Shared by NPCBunny/EnemyInstance's own ICombatant.VisualCenter — encapsulates every renderer's live
    // world-space bounds into one box and returns its center, i.e. the true visual midpoint of however
    // many sprite parts (body, face overlay, etc. — see the bunny outline rework's multi-part rig) make up
    // this combatant right now, regardless of where its root Transform's pivot happens to sit. Renderers
    // should be cached once by the caller (GetComponentsInChildren allocates), not re-queried every call.
    public static Vector3 ComputeVisualCenter(Renderer[] renderers, Vector3 fallbackPosition)
    {
        if (renderers == null || renderers.Length == 0) return fallbackPosition;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds.center;
    }


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

    // Re-acquires target if it's null/dead/gone, then — once cooldownRemaining reaches 0 and there's a
    // melee-excluded ranged AttackSource with VFX authored — resets the cooldown and returns true with
    // attackTarget set to whoever this wind-up should eventually fire at. The caller is responsible for
    // triggering its own Animator on a true return (ICombatant deliberately has no Animator reference —
    // animation is each concrete type's own concern, not shared targeting logic's) and remembering
    // attackTarget until its Animation Event calls ReleaseAttack.
    public static bool TryBeginAttack(ICombatant self, ref ICombatant target, ref float cooldownRemaining, IEnumerable<ICombatant> candidatePool, out ICombatant attackTarget)
    {
        attackTarget = null;

        if (target == null || target.CombatGameObject == null || !target.IsAlive)
            target = FindClosestAliveTarget(self.CombatTransform.position, candidatePool);

        if (target == null) return false;

        cooldownRemaining -= Time.deltaTime;
        if (cooldownRemaining > 0f) return false;

        BunnyTypeDefinition attackSource = self.AttackSource;
        if (attackSource == null || attackSource.attackVFXPrefab == null) return false;
        if (attackSource.isMelee) return false; // melee approach/flanking not built yet — ranged only this pass

        cooldownRemaining = attackSource.attackIntervalSeconds;
        attackTarget = target;
        return true;
    }

    // Called by the Animation Event placed at the attacking clip's "release" frame — instantiates the
    // attacker's signature AttackInstance prefab at its own current position and launches it at
    // whichever target TryBeginAttack snapshotted. Re-validates the target (a few frames of wind-up may
    // have passed since it was chosen) and silently no-ops if it's dead/gone or now out of range — same
    // "just doesn't fire" tolerance TryBeginAttack itself already has, not an error state.
    public static void ReleaseAttack(ICombatant attacker, ICombatant target)
    {
        if (target == null || target.CombatGameObject == null || !target.IsAlive) return;

        BunnyTypeDefinition attackSource = attacker.AttackSource;
        if (attackSource == null || attackSource.attackVFXPrefab == null) return;

        float distance = Vector3.Distance(attacker.CombatTransform.position, target.CombatTransform.position);
        if (distance > CombatBalanceConfig.Instance.rangedMaxRange) return;

        // TEMP — chasing "enemy attackOriginOffset appears mirrored" bug.
        Debug.Log($"[VFXDEBUG] ReleaseAttack({attacker.CombatGameObject?.name}): attackerPos={attacker.CombatTransform.position} attackerRot={attacker.CombatTransform.rotation.eulerAngles} attackOrigin={attacker.AttackOrigin}");

        GameObject vfxObject = Object.Instantiate(attackSource.attackVFXPrefab, attacker.AttackOrigin, Quaternion.identity);
        AttackInstance instance = vfxObject.GetComponent<AttackInstance>();
        if (instance == null)
        {
            Debug.LogWarning($"CombatEngagement: {attackSource.displayName}'s attackVFXPrefab has no AttackInstance component.");
            Object.Destroy(vfxObject);
            return;
        }

        instance.Launch(attacker, target, isStationary: false);
    }
}
