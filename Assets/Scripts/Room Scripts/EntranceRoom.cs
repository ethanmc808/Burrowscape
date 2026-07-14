using UnityEngine;
using System.Collections.Generic;

public class EntranceRoom : RoomBase, IJobRoom
{
    [SerializeField] private List<RoomSpot> guardSpots;

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
}