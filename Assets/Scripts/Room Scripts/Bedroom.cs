using UnityEngine;
using System.Collections.Generic;

// No IJobRoom, no coroutine — energy regen ticks in NPCBunny.Update() itself since nothing is
// consumed here (unlike WaterRoom's production side).
public class Bedroom : RoomBase
{
    [SpotNamePrefix("SleepingSpot")]
    [SerializeField] private List<RoomSpot> sleepingSpots;

    // This Bedroom's contribution to PopulationManager's population cap — a separately-tunable Inspector
    // value rather than derived from sleepingSpots.Count, same shape as WaterRoom's
    // waterRationingPoolAmount/powerRationingPoolAmount: the spot count still governs how many bunnies can
    // actually sleep here, but the cap contribution is free to differ (e.g. a nicer Grade 2/3 Bedroom
    // could grant more cap than its raw bed count implies). Grade 1 4x2x6 default of 4 matches its current
    // sleeping spot count as a baseline.
    [SerializeField] private int populationCapContribution = 4;
    public int PopulationCapContribution => populationCapContribution;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.RegisterBedroom(this);
        else
            Debug.LogWarning($"{name}: BaseManager.Instance was null during OnEnable.");

        if (PopulationManager.Instance != null)
            PopulationManager.Instance.RegisterBedroomCapacity(this);
        else
            Debug.LogWarning($"{name}: PopulationManager.Instance was null during OnEnable.");
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.UnregisterBedroom(this);

        if (PopulationManager.Instance != null)
            PopulationManager.Instance.UnregisterBedroomCapacity(this);
    }

    public RoomSpot RequestSleepSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in sleepingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public void ReleaseSleepSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
    }

    public bool HasAvailableSleepSpot()
    {
        foreach (RoomSpot spot in sleepingSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }
}
