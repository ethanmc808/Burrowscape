using System.Collections.Generic;
using UnityEngine;

// Shared "who do I shoot, and when" logic for both sides of a base invasion — used by NPCBunny's
// Defending state and EnemyInstance's own tick alike, so the two don't duplicate identical
// targeting/cooldown bookkeeping. Per Ethan's design: in-base combat always targets whoever is closest;
// a future quest system will let the player override a bunny's target by clicking an enemy, with enemies
// still always targeting closest — that override is a separate, not-yet-built concern and doesn't change
// anything here.
//
// Melee and ranged both fire through TryBeginAttack/ReleaseAttack now. Melee gating lives with the
// CALLER instead of here: NPCBunny.HandleMeleeDefending/EnemyInstance.HandleMeleeUpdate only ever invoke
// TryBeginAttack once a melee combatant has already claimed a FlankSlots slot and walked into position,
// passing a 1-element "pinned" candidate pool (that one flanked target) rather than the full room-wide
// candidate pool ranged uses — so TryBeginAttack itself stays attack-type-agnostic and doesn't need to
// know about flanking at all.
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

    // Always re-acquires the closest alive candidate (Ethan's design — see this class's own header
    // comment: "always targets whoever is closest," not sticky-until-death), then — once cooldownRemaining
    // reaches 0 and there's a melee-excluded ranged AttackSource with VFX authored — resets the cooldown
    // and returns true with attackTarget set to whoever this wind-up should eventually fire at. Re-running
    // FindClosestAliveTarget every call keeps the existing target locked whenever it's genuinely still
    // closest, and switches immediately once something closer becomes valid (e.g. a second bunny reaching
    // a nearer CombatSpot) — a target already mid-wind-up isn't affected, since attackTarget is snapshotted
    // into the caller's own pendingAttackTarget field at that point, decoupled from target's future changes.
    // The caller is responsible for triggering its own Animator on a true return (ICombatant deliberately
    // has no Animator reference — animation is each concrete type's own concern, not shared targeting
    // logic's) and remembering attackTarget until its Animation Event calls ReleaseAttack.
    // attacksFiredInBurst is the caller's own per-instance counter (mirrors cooldownRemaining's ref shape)
    // tracking progress through attackSource.attacksPerBurst — see BunnyTypeDefinition.attacksPerBurst/
    // burstPauseSeconds. Lives on the caller (NPCBunny/EnemyInstance), not here or on BunnyTypeDefinition
    // itself, since the latter is a shared ScriptableObject asset and multiple combatants of the same type
    // attacking simultaneously would stomp a single shared counter.
    public static bool TryBeginAttack(ICombatant self, ref ICombatant target, ref float cooldownRemaining, ref int attacksFiredInBurst, IEnumerable<ICombatant> candidatePool, out ICombatant attackTarget)
    {
        attackTarget = null;

        target = FindClosestAliveTarget(self.CombatTransform.position, candidatePool);
        if (target == null) return false;

        cooldownRemaining -= Time.deltaTime;
        if (cooldownRemaining > 0f) return false;

        BunnyTypeDefinition attackSource = self.AttackSource;
        if (attackSource == null || attackSource.attackVFXPrefab == null) return false;

        cooldownRemaining = attackSource.attackIntervalSeconds;

        // Burst grouping: attacksPerBurst <= 1 (every existing type, until one opts in) skips this
        // entirely, so cooldownRemaining behaves exactly as before. Otherwise, count this wind-up toward
        // the current burst and, once it completes the Nth attack, tack burstPauseSeconds onto the cooldown
        // just started and reset the counter for the next burst.
        if (attackSource.attacksPerBurst > 1)
        {
            attacksFiredInBurst++;
            if (attacksFiredInBurst >= attackSource.attacksPerBurst)
            {
                attacksFiredInBurst = 0;
                cooldownRemaining += attackSource.burstPauseSeconds;
            }
        }

        attackTarget = target;
        return true;
    }

    // Melee-specific target selection: prefers the closest candidate that currently has an OPEN flank
    // slot over a closer-but-fully-flanked one, since claiming a slot is a hard prerequisite for a melee
    // attacker to ever engage that target at all — a candidate with both slots taken is simply not a
    // valid choice right now, no matter how close. Candidates with no FlankSlots component (never added
    // to their prefab yet) are silently skipped, same "content not authored yet, not an error" tolerance
    // as the rest of this system. Returns null if every alive candidate is fully flanked (or has no
    // FlankSlots at all) — callers should idle at their CombatSpot/EnemySpot rather than treat this as a
    // hard failure.
    public static ICombatant FindBestMeleeTarget(Vector3 fromPosition, IEnumerable<ICombatant> candidates)
    {
        ICombatant best = null;
        float bestSqrDistance = float.MaxValue;

        foreach (ICombatant candidate in candidates)
        {
            if (candidate == null || candidate.CombatGameObject == null || !candidate.IsAlive) continue;

            FlankSlots slots = GetFlankSlots(candidate);
            if (slots == null || !slots.HasOpenSlot) continue;

            float sqrDistance = (candidate.CombatTransform.position - fromPosition).sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                best = candidate;
            }
        }

        return best;
    }

    // Shared by EnemyInstance.Update/HandleSiegeUpdate and NPCBunny.HandleDefending — both re-acquire the
    // closest candidate into their own currentTarget-style field EVERY call (see TryBeginAttack's own
    // comment), which is correct for WHO to eventually shoot but wrong for WHICH WAY TO FACE while a wind-up
    // is already in flight: a wind-up's actual attack fires at pendingTarget (snapshotted once, held for the
    // whole animation), so if a different candidate becomes momentarily closer mid-wind-up, facing off the
    // live re-acquired target turns the attacker toward that new candidate while its attack still lands on
    // the original one — confirmed bug (Slime "facing the wrong way while attacking," 2026-08-03), far more
    // visible on the enemy side since a slime typically has many defending bunnies to churn through versus a
    // ranged bunny's usual 1-2 enemies. Prefers pendingTarget whenever a wind-up is active so the visible
    // facing always matches wherever the in-flight attack will actually fire; falls back to liveTarget the
    // rest of the time (idle tracking between attacks). Returns null (caller should skip the SetFacing call
    // entirely, not just no-op on a repeated value) when the chosen target's X is within deadzone of self's
    // X — without this, two combatants sitting nearly vertically aligned flip-flop facing every frame purely
    // from ordinary position jitter, since targetX < selfX has no hysteresis of its own.
    public static bool? ComputeFacing(ICombatant liveTarget, ICombatant pendingTarget, Vector3 selfPosition, float deadzone)
    {
        ICombatant faceTarget = pendingTarget != null && pendingTarget.CombatGameObject != null ? pendingTarget : liveTarget;
        if (faceTarget == null || faceTarget.CombatGameObject == null) return null;

        float deltaX = faceTarget.CombatTransform.position.x - selfPosition.x;
        if (Mathf.Abs(deltaX) <= deadzone) return null;

        return deltaX < 0f; // higher world-X = screen-left convention — see AttackOrigin's own comment
    }

    // Single lookup point for "the FlankSlots on this ICombatant" — a GetComponent call rather than an
    // ICombatant interface addition, since FlankSlots is optional per-prefab content (not every bunny/
    // enemy prefab has it added yet), same pattern StatusEffectController is already looked up by.
    public static FlankSlots GetFlankSlots(ICombatant combatant)
    {
        return combatant?.CombatGameObject != null ? combatant.CombatGameObject.GetComponent<FlankSlots>() : null;
    }

    // World-space point meleeStandingDistance units to the given side of target's CombatTransform — never
    // front/behind, per Combat_DesignDoc.md's positioning rules. Deliberately CombatTransform, not
    // VisualCenter: VisualCenter is a bounds-center point built for aiming a projectile at center-of-body
    // (see its own doc comment on ICombatant), which usually sits well above a sprite's ground-level root
    // pivot — anchoring flanking to it stood attackers at the target's mid-body height instead of its
    // actual ground position (confirmed empirically: identical Z, ~0.12 unit Y gap, in Play mode). NOTE:
    // this project's world-X axis is inverted relative to screen space (increasing world X = screen-LEFT —
    // see EnemyInstance.AttackOrigin's own comment on this same convention). The sign below is a first
    // guess, not yet verified empirically against an actual Left/Right claim in Play mode — flip if a
    // "Left"-claimed attacker visually lands on the target's right.
    public static Vector3 ComputeFlankPosition(ICombatant target, FlankSide side)
    {
        float offset = CombatBalanceConfig.Instance.meleeStandingDistance;
        float signedOffset = side == FlankSide.Left ? -offset : offset;
        return target.CombatTransform.position + new Vector3(signedOffset, 0f, 0f);
    }

    // Called by the Animation Event placed at the attacking clip's "release" frame — instantiates the
    // attacker's signature AttackInstance prefab at its own current position and launches it at
    // whichever target TryBeginAttack snapshotted. Re-validates the target (a few frames of wind-up may
    // have passed since it was chosen) and silently no-ops if it's dead/gone or now out of range — same
    // "just doesn't fire" tolerance TryBeginAttack itself already has, not an error state.
    //
    // retargetPool: optional — if the original target died during the wind-up AND a pool is given, picks
    // the closest still-alive replacement from it instead of just no-op'ing (Ethan's ask: a ranged bunny's
    // slow-cadence attack, e.g. Shock's ~3.5s interval, was otherwise wasted every time its specific target
    // got sniped by a faster ally first, even with other enemies still very much alive and in range).
    // NPCBunny.ReleasePendingAttack only passes this for ranged attacks — a melee attacker is physically
    // committed to the one target it walked up to and claimed a FlankSlots slot on (see
    // HandleMeleeDefending's own pinned-pool comment), so silently swapping to a different enemy elsewhere
    // in the room would visually attack from the wrong position. EnemyInstance.ReleasePendingAttack never
    // passes one either, deliberately — see that method's own comment for why fizzle-only must stay as-is
    // for enemies attacking bunnies.
    public static void ReleaseAttack(ICombatant attacker, ICombatant target, IEnumerable<ICombatant> retargetPool = null)
    {
        bool targetGone = target == null || target.CombatGameObject == null || !target.IsAlive;
        if (targetGone)
        {
            target = retargetPool != null ? FindClosestAliveTarget(attacker.CombatTransform.position, retargetPool) : null;
            if (target == null) return;
        }

        BunnyTypeDefinition attackSource = attacker.AttackSource;
        if (attackSource == null || attackSource.attackVFXPrefab == null) return;

        float distance = Vector3.Distance(attacker.CombatTransform.position, target.CombatTransform.position);
        if (distance > CombatBalanceConfig.Instance.rangedMaxRange) return;

        GameObject vfxObject = Object.Instantiate(attackSource.attackVFXPrefab, attacker.AttackOrigin, Quaternion.identity);
        AttackInstance instance = vfxObject.GetComponent<AttackInstance>();
        if (instance == null)
        {
            Debug.LogWarning($"CombatEngagement: {attackSource.displayName}'s attackVFXPrefab has no AttackInstance component.");
            Object.Destroy(vfxObject);
            return;
        }

        // Melee attacks resolve stationary (in place at the target, no travel) — see AttackInstance.Launch.
        instance.Launch(attacker, target, isStationary: attackSource.isMelee);
    }
}
