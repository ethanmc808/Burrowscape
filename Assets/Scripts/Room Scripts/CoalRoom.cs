using UnityEngine;
using System.Collections.Generic;
using System.Collections;

// Structural copy of GardenRoom's production shape, producing Power instead of Carrots. The only real
// difference: it reports active/inactive status to the global PowerManager once per bunny at the same
// points it starts/stops that bunny's own production coroutine, since (unlike Carrots/Water) Power
// production is something other rooms actively arbitrate against, not just a passive counter to add to.
// PowerManager tracks how many bunnies are active here (not just whether any are), so 2 workers produce
// twice what 1 does.
public class CoalRoom : RoomBase, IJobRoom
{
    [SpotNamePrefix("ShovelingSpot")]
    [SerializeField] private List<RoomSpot> shovelingSpots;

    private Dictionary<NPCBunny, Coroutine> activeProductionRoutines = new Dictionary<NPCBunny, Coroutine>();

    // Bunnies idled because THIS room shut down (lost Power/Water) while they were actively working —
    // resumed automatically by OnRoomRestored. Mirrors GardenRoom/WaterRoom.
    private List<NPCBunny> idledByShutdown = new List<NPCBunny>();

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in shovelingSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public bool HasAvailableSpot()
    {
        foreach (RoomSpot spot in shovelingSpots)
        {
            if (!spot.IsOccupied)
                return true;
        }
        return false;
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
        StopProductionRoutine(bunny);
    }

    // Bunny is heading off to eat/drink/sleep but keeps its reserved spot — the room shouldn't hand
    // that spot to anyone else while it's gone, just pause production.
    public void NotifyBunnyLeavingToEat(NPCBunny bunny)
    {
        StopProductionRoutine(bunny);
    }

    private void StopProductionRoutine(NPCBunny bunny)
    {
        if (activeProductionRoutines.TryGetValue(bunny, out Coroutine routine))
        {
            StopCoroutine(routine);
            activeProductionRoutines.Remove(bunny);
            NotifyPowerManagerInactive(); // one bunny stopped — decrement by exactly one
        }
    }

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        // Arrived while this room itself is dark (e.g. mid-walk when it shut down) — idle immediately
        // instead of starting production; OnRoomRestored resumes them once it comes back.
        if (!IsOperational)
        {
            if (!idledByShutdown.Contains(bunny))
                idledByShutdown.Add(bunny);
            bunny.ForceIdleDueToRoomShutdown();
            return;
        }

        if (!activeProductionRoutines.ContainsKey(bunny))
        {
            Coroutine routine = StartCoroutine(ProducePowerRoutine(bunny));
            activeProductionRoutines[bunny] = routine;
            NotifyPowerManagerActive();
        }
    }

    private IEnumerator ProducePowerRoutine(NPCBunny bunny)
    {
        while (true)
        {
            yield return new WaitForSeconds(PowerProductionInterval);

            if (bunny.CurrentState != BunnyState.Working)
            {
                activeProductionRoutines.Remove(bunny);
                NotifyPowerManagerInactive(); // one bunny stopped — decrement by exactly one
                yield break;
            }

            // No resource is consumed to produce Power — PowerManager reads PowerProductionAmount/
            // PowerProductionInterval directly off this room (multiplied by however many bunnies are
            // currently active here) while it's registered as active; this loop just needs to keep
            // ticking for as long as the bunny stays Working.
        }
    }

    private void NotifyPowerManagerActive()
    {
        PowerManager.EnsureInstance().NotifyProducerActive(this);
    }

    private void NotifyPowerManagerInactive()
    {
        PowerManager.EnsureInstance().NotifyProducerInactive(this);
    }

    // ---------- IJobRoom shutdown/restore ----------
    // Only reachable if this room is ever configured to itself consume Power or Water (not the normal
    // case for a producer, but the interface is generic — every IJobRoom implements these).

    public void OnRoomShutdown()
    {
        foreach (KeyValuePair<NPCBunny, Coroutine> kvp in activeProductionRoutines)
        {
            StopCoroutine(kvp.Value);
            kvp.Key.ForceIdleDueToRoomShutdown();
            idledByShutdown.Add(kvp.Key);
        }
        activeProductionRoutines.Clear();
        PowerManager.EnsureInstance().NotifyProducerAllInactive(this); // reset this room's worker count to zero, regardless of how many were active
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored();
        idledByShutdown.Clear();
    }
}
