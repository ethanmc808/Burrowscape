using UnityEngine;
using System.Collections.Generic;

public class LivingRoom : RoomBase
{
    [SerializeField] private List<RoomSpot> relaxingSpots;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.RegisterLivingRoom(this);
        else
            Debug.LogWarning($"{name}: BaseManager.Instance was null during OnEnable.");
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (BaseManager.Instance != null)
            BaseManager.Instance.UnregisterLivingRoom(this);
    }

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in relaxingSpots)
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
        foreach (RoomSpot spot in relaxingSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }
}
