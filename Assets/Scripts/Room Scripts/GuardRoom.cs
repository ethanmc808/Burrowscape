using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// A Guard-job bunny's "work spot" is its own WatchSpot — a dedicated job-spot list, same shape as
// EntranceRoom's guardSpots, distinct from RoomBase's inherited CombatSpots. WatchSpots are where a
// posted guard patrols/stands during normal duty (and pick up patrol paths for free via
// RoomBase.GetWorkWanderChain, same as any other job spot); CombatSpots are the shared pool every room
// has for actual invasion combat (auto-defenders and manually-deployed guards alike, via
// NPCBunny.BeginDefending — see InvasionManager.TriggerAutoDefend / GuardDeployUI). The auto-populator
// already routes a WatchSpot -> CombatSpot RoomPath for every pairing (its "occupancy spot to CombatSpot"
// rule), so a guard called into battle reroutes cleanly from wherever it was posted/patrolling.
public class GuardRoom : RoomBase, IJobRoom
{
    [SpotNamePrefix("WatchSpot")]
    [SerializeField] private List<RoomSpot> watchSpots;

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

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        if (watchSpots == null) return null;
        foreach (RoomSpot spot in watchSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
        RevokeGuardBuff(bunny);
    }

    public bool HasAvailableSpot()
    {
        if (watchSpots == null) return false;
        foreach (RoomSpot spot in watchSpots)
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
