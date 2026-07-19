using UnityEngine;
using System.Collections.Generic;

// No IJobRoom, no coroutine — energy regen ticks in NPCBunny.Update() itself since nothing is
// consumed here (unlike WaterRoom's production side).
public class Bedroom : RoomBase
{
    [SpotNamePrefix("SleepingSpot")]
    [SerializeField] private List<RoomSpot> sleepingSpots;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.RegisterBedroom(this);
        else
            Debug.LogWarning($"{name}: BaseManager.Instance was null during OnEnable.");
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.UnregisterBedroom(this);
    }

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in sleepingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
    }

    public bool HasAvailableSpot()
    {
        foreach (RoomSpot spot in sleepingSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }
}
