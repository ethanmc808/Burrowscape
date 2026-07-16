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
        // entrance to the job room, skipping the gate/queue entirely.
        return allBunnies.Where(b => !b.IsAssignedToJob && b.HasEnteredBase).ToList();
    }
    public List<NPCBunny> GetBunniesAssignedTo(IJobRoom room)
    {
        return allBunnies.Where(b => b.AssignedJobRoom == room).ToList();
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