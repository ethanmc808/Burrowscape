using UnityEngine;
using System.Collections.Generic;

public class EntranceRoom : RoomBase, IJobRoom
{
    [SerializeField] private List<RoomSpot> guardSpots;

    // Self-registers as THE entrance room on every enable, not just the first — necessary because an
    // Entrance Room upgrade destroys the old instance and instantiates a new one (RoomTransitionService),
    // and both BaseLayoutManager and GateQueueManager cache a direct reference to whichever instance is
    // current. See BaseLayoutManager.SetEntranceRoom for what breaks without this.
    protected override void OnEnable()
    {
        base.OnEnable();
        BaseLayoutManager.Instance?.SetEntranceRoom(this);
        GateQueueManager.Instance?.SetEntranceRoom(this);
    }

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in guardSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public bool HasAvailableSpot()
    {
        foreach (RoomSpot spot in guardSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
    }

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        // No production loop for guarding — hook for future combat/alert logic
    }

    public void NotifyBunnyLeavingToEat(NPCBunny bunny)
    {
        // No per-bunny routine to pause for guarding
    }

    // There's only ever one Entrance Room per base (see BaseLayoutManager.entranceRoom, GateQueueManager),
    // and it's the sole floor-1 anchor everything else's Y -> floor conversion is derived from — losing
    // it isn't recoverable the way losing any other room is. Always blocked, independent of occupancy.
    public override bool CanBeDeleted(out string blockedReason)
    {
        blockedReason = "the Entrance Room can never be deleted";
        return false;
    }
}