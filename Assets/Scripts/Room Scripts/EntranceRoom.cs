using UnityEngine;
using System.Collections.Generic;

public class EntranceRoom : RoomBase, IJobRoom
{
    [SpotNamePrefix("GuardSpot")]
    [SerializeField] private List<RoomSpot> guardSpots;

    // No per-bunny production coroutine exists here (unlike Garden/Water/Coal) since guarding has no
    // production loop yet, so shutdown/restore tracks currently-active guards directly instead of
    // reusing a routines dictionary that doesn't exist for this room.
    private HashSet<NPCBunny> activeGuards = new HashSet<NPCBunny>();
    private List<NPCBunny> idledByShutdown = new List<NPCBunny>();

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
        activeGuards.Remove(bunny);
    }

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        // Arrived while this room is dark — idle immediately instead of counting as an active guard;
        // OnRoomRestored resumes them once it comes back.
        if (!IsOperational)
        {
            if (!idledByShutdown.Contains(bunny))
                idledByShutdown.Add(bunny);
            bunny.ForceIdleDueToRoomShutdown();
            return;
        }

        activeGuards.Add(bunny); // no production loop for guarding yet — hook for future combat/alert logic
    }

    public void NotifyBunnyLeavingToEat(NPCBunny bunny)
    {
        activeGuards.Remove(bunny);
    }

    // ---------- IJobRoom shutdown/restore ----------

    public void OnRoomShutdown()
    {
        foreach (NPCBunny bunny in activeGuards)
        {
            bunny.ForceIdleDueToRoomShutdown();
            idledByShutdown.Add(bunny);
        }
        activeGuards.Clear();
    }

    public void OnRoomRestored()
    {
        foreach (NPCBunny bunny in idledByShutdown)
            bunny.ResumeWorkAfterRoomRestored();
        idledByShutdown.Clear();
    }

    // There's only ever one Entrance Room per base (see BaseLayoutManager.entranceRoom, GateQueueManager),
    // and it's the sole floor-1 anchor everything else's Y -> floor conversion is derived from — losing
    // it isn't recoverable the way losing any other room is. Always blocked, independent of occupancy.
    public override bool CanBeDeleted(out string blockedReason)
    {
        blockedReason = "the Entrance Room can never be deleted";
        return false;
    }

    // The Entrance Room's outward-facing side (left, per this project's "higher X = visually left"
    // convention — see BaseLayoutManager.EntranceLeftEdgeX) always leads to the gate/surface, never
    // another room, so it must always read as a real doorway (filler hidden, frame shown). Nothing is
    // ever registered there for BaseLayoutManager's generic adjacency check to detect, so left
    // uncorrected it would always compute "no neighbor" and incorrectly seal shut. The right side
    // (where rooms actually get built into the base) is unaffected and behaves like any other room.
    public override void SetLeftDoorwayOpen(bool open)
    {
        base.SetLeftDoorwayOpen(true);
    }
}