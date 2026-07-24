using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class DwellerRoster : MonoBehaviour
{
    public static DwellerRoster Instance { get; private set; }

    private List<NPCBunny> allBunnies = new List<NPCBunny>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Register(NPCBunny bunny)
    {
        if (!allBunnies.Contains(bunny))
            allBunnies.Add(bunny);
    }

    public void Unregister(NPCBunny bunny)
    {
        allBunnies.Remove(bunny);
    }

    public List<NPCBunny> GetUnassignedBunnies()
    {
        // Exclude bunnies still spawned-but-queued at the gate (or mid-approval) — they haven't
        // entered the base yet, so assigning them to a job would route them straight from the base
        // entrance to the job room, skipping the gate/queue entirely. Also exclude bunnies currently out
        // Foraging (parked offscreen — see NPCBunny.DepartForForaging) — they have no job and
        // HasEnteredBase is still true, so without this they'd wrongly appear assignable while away.
        return allBunnies.Where(b => !b.IsAssignedToJob && b.HasEnteredBase && b.CurrentState != BunnyState.Foraging).ToList();
    }

    // Every resident bunny eligible to be sent Foraging — HasEnteredBase (a real dweller, not still
    // queued/awaiting approval), not already out on a trip, and not mid-transit toward/through the gate
    // for an existing trip. Unlike GetUnassignedBunnies, a bunny with a job/relax/sleep claim IS
    // included — NPCBunny.CanDepartForForaging/ReleaseAllClaimsForDeparture handle releasing that claim
    // at dispatch time, same as sending a working bunny on break.
    public List<NPCBunny> GetForageableBunnies()
    {
        return allBunnies.Where(b => b.HasEnteredBase && b.CanDepartForForaging()).ToList();
    }
    public List<NPCBunny> GetBunniesAssignedTo(IJobRoom room)
    {
        return allBunnies.Where(b => b.AssignedJobRoom == room).ToList();
    }

    // Used by RoomTransitionService's Evacuate step to gather everyone tied to any of the rooms being
    // replaced by a merge/upgrade — reuses the same per-bunny check RoomBase.CanBeDeleted relies on
    // (via IsRoomOccupied, below), just collecting names instead of only checking "any at all."
    public List<NPCBunny> GetBunniesAssociatedWithRooms(List<RoomBase> rooms)
    {
        return allBunnies.Where(b => rooms.Any(r => b.IsAssociatedWithRoom(r))).ToList();
    }

    // Used by RoomTransitionService's quiescence wait — true while anyone is still mid-flight into,
    // through, or actively eating/drinking in any of the rooms about to be replaced.
    public bool IsAnyBunnyTransientlyInRooms(List<RoomBase> rooms)
    {
        return allBunnies.Any(b => rooms.Any(r => b.IsTransientlyInRoom(r)));
    }

    // Used by RoomBase.CanBeDeleted to block demolishing a room that's currently in use.
    public bool IsRoomOccupied(RoomBase room)
    {
        // Guarded as a block, not per-line, so the (DebugState()/IsAssociatedWithRoom() per bunny)
        // work itself is skipped when verbose logging is off, not just the resulting Debug.Log call.
        if (DebugLog.Verbose)
        {
            Debug.Log($"[OccupancyCheck] room={room.name} @ {room.transform.position}");
            foreach (NPCBunny b in allBunnies)
                Debug.Log($"[OccupancyCheck]   {b.DebugState()}, associated={b.IsAssociatedWithRoom(room)}");
        }

        return allBunnies.Any(b => b.IsAssociatedWithRoom(room));
    }
}