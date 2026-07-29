using UnityEngine;
using System.Collections.Generic;

// Scaffolding only — see Combat_DesignDoc.md's "Guard Room" section for what's actually designed
// (passive per-room defense, manual deploy to reinforce another room, single-guard-unit-per-room lock)
// and what's still open (deploy UX default, whether the enemy-side flank cap is truly symmetric). None
// of that behavior is implemented here yet; this is just the room shell + roster capacity, mirroring
// Bedroom/LivingRoom's shape (a room whose whole job is "up to N bunnies station here").
public class GuardRoom : RoomBase
{
    [SpotNamePrefix("CombatSpot")]
    [SerializeField] private List<RoomSpot> combatSpots;

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
        foreach (RoomSpot spot in combatSpots)
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
        foreach (RoomSpot spot in combatSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }
}
