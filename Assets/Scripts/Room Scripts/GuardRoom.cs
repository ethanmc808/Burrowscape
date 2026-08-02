using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// A Guard-job bunny's "work spot" is its combat post — RequestSpot/ReleaseSpot/HasAvailableSpot are thin
// wrappers around RoomBase's inherited CombatSpots (see RoomBase.ClaimCombatSpot), the same list used by
// auto-defending residents and manually-deployed guards in any other room. No production of its own (see
// GardenRoom for that shape) — a posted guard just stands there until InvasionManager sends them into
// combat via NPCBunny.BeginDefending, same as any other room's defenders.
public class GuardRoom : RoomBase, IJobRoom
{
    // Bunnies idled because this room lost Power/Water while posted — resumed automatically by
    // OnRoomRestored, same shape as GardenRoom's idledByShutdown.
    private List<NPCBunny> idledByShutdown = new List<NPCBunny>();

    // Every bunny currently granted this room's guard buff/shield — separate bookkeeping from the
    // CombatSpot claim itself, mirroring EntranceRoom's own activeGuards HashSet, so OnRoomShutdown can
    // revoke the buff from everyone currently posted without needing to also force them idle (GuardRoom
    // deliberately doesn't do that — see OnRoomShutdown's own comment below).
    private readonly HashSet<NPCBunny> activeGuards = new HashSet<NPCBunny>();

    protected override void OnEnable()
    {
        base.OnEnable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.RegisterGuardRoom(this);
        else
            Debug.LogWarning($"{name}: BaseManager.Instance was null during OnEnable.");
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.UnregisterGuardRoom(this);
    }

    public RoomSpot RequestSpot(NPCBunny bunny) => ClaimCombatSpot(bunny);

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        ReleaseCombatSpot(spot, bunny);
        RevokeGuardBuff(bunny);
    }

    public bool HasAvailableSpot()
    {
        if (CombatSpots == null) return false;
        foreach (RoomSpot spot in CombatSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }

    // No production routine to pause — a posted guard just stands at its spot until deployed. Still
    // revokes the buff/shield though, same as EntranceRoom's activeGuards.Remove here — a bunny that's
    // stepped away to eat isn't actively "on duty."
    public void NotifyBunnyLeavingToEat(NPCBunny bunny) => RevokeGuardBuff(bunny);

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        if (!IsOperational)
        {
            if (!idledByShutdown.Contains(bunny))
                idledByShutdown.Add(bunny);
            bunny.ForceIdleDueToRoomShutdown();
            return;
        }

        GrantGuardBuff(bunny);
    }

    public void OnRoomShutdown()
    {
        // Nothing actively running to stop (no production coroutines) — idling happens the next time a
        // bunny would otherwise arrive/resume via NotifyBunnyReadyToWork's own IsOperational check.
        // Already-posted bunnies stay put (deliberately not force-idled the way NotifyBunnyReadyToWork's
        // shutdown branch handles fresh arrivals), but their guard buff/shield still needs to drop while
        // the room is dark — an unpowered room shouldn't keep granting combat bonuses.
        foreach (NPCBunny bunny in activeGuards.ToList())
            RevokeGuardBuff(bunny);
    }

    // Grant/revoke the Grade-scaled buff (GuardBuffController) and, at Grade 3+, the shield buffer — see
    // GuardBuffController's own header comment for why this isn't routed through the Trait system.
    private void GrantGuardBuff(NPCBunny bunny)
    {
        activeGuards.Add(bunny);

        GuardBuffController buff = bunny.GetComponent<GuardBuffController>();
        if (buff != null) buff.SetGuarding(Grade);

        if (Grade >= 3)
            bunny.GrantShield(CombatBalanceConfig.Instance.guardGrade3ShieldAmount);
    }

    private void RevokeGuardBuff(NPCBunny bunny)
    {
        activeGuards.Remove(bunny);

        GuardBuffController buff = bunny.GetComponent<GuardBuffController>();
        if (buff != null) buff.ClearGuarding();

        if (Grade >= 3)
            bunny.ClearShield();
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored();
        idledByShutdown.Clear();
    }
}
