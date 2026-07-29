public enum FlankSide { Left, Right }

// Claim/release bookkeeping for melee engagement, mirroring RoomSpot's TryClaim/Release pattern but for
// "who's currently flanking this target" instead of a room position. Per Combat_DesignDoc.md: capped at
// 2 melee attackers per target (left + right — there are only two physical sides, which is why this is a
// fixed pair of slots rather than a generic N-slot list even though CombatBalanceConfig.maxFlankersPerTarget
// documents that number). Symmetric by construction — the same component works whether a bunny is
// flanking an enemy or an enemy is flanking a bunny, since it doesn't care what kind of ICombatant sits
// in either role.
//
// Put this on any GameObject that can be a melee target (a bunny or an EnemyInstance) alongside
// StatusEffectController. Nothing creates/queries this yet — the actual "which target should I approach,
// and which side is closer" AI decision is part of the not-yet-built combat movement/AI layer.
public class FlankSlots : UnityEngine.MonoBehaviour
{
    private ICombatant leftOccupant;
    private ICombatant rightOccupant;

    public bool HasOpenSlot => leftOccupant == null || rightOccupant == null;

    public bool TryClaimSlot(ICombatant attacker, out FlankSide side)
    {
        if (leftOccupant == null)
        {
            leftOccupant = attacker;
            side = FlankSide.Left;
            return true;
        }
        if (rightOccupant == null)
        {
            rightOccupant = attacker;
            side = FlankSide.Right;
            return true;
        }
        side = default;
        return false;
    }

    public void ReleaseSlot(ICombatant attacker)
    {
        if (ReferenceEquals(leftOccupant, attacker)) leftOccupant = null;
        else if (ReferenceEquals(rightOccupant, attacker)) rightOccupant = null;
    }

    // For an attacker choosing between two potential targets, "already flanking me?" (rather than
    // silently re-claiming a slot they already hold) is worth checking first — TryClaimSlot alone would
    // otherwise report false for an attacker that already occupies a slot, since both slots would read
    // as "occupied" from its own claim.
    public bool IsOccupiedBy(ICombatant attacker)
    {
        return ReferenceEquals(leftOccupant, attacker) || ReferenceEquals(rightOccupant, attacker);
    }
}
