using UnityEngine;
using System.Collections.Generic;

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

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny) => ReleaseCombatSpot(spot, bunny);

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

    // No production routine to pause — a posted guard just stands at its spot until deployed.
    public void NotifyBunnyLeavingToEat(NPCBunny bunny) { }

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        if (!IsOperational)
        {
            if (!idledByShutdown.Contains(bunny))
                idledByShutdown.Add(bunny);
            bunny.ForceIdleDueToRoomShutdown();
        }
    }

    public void OnRoomShutdown()
    {
        // Nothing actively running to stop (no production coroutines) — idling happens the next time a
        // bunny would otherwise arrive/resume via NotifyBunnyReadyToWork's own IsOperational check.
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored();
        idledByShutdown.Clear();
    }
}
