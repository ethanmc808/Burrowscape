using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class BaseLayoutManager : MonoBehaviour
{
    public static BaseLayoutManager Instance { get; private set; }

    // Fired after a floor's room layout settles (per-floor add/remove, same batched timing as the
    // doorway refresh below) — DirtFillManager listens to regenerate just that floor's dirt segments.
    public static event Action<int> OnFloorLayoutChanged;

    // Fired when ExpandBuildableBounds grows the buildable area — DirtFillManager listens to do a full
    // regenerate across every floor, since new floors/width may now need dirt that didn't exist before.
    public static event Action OnBuildableBoundsChanged;

    [SerializeField] private Transform baseEntrance;
    public Transform BaseEntrance => baseEntrance;

    [Header("Floor Detection")]
    [SerializeField] private RoomBase entranceRoom; // reference point for Y -> floor conversion; initial scene wiring only — see SetEntranceRoom
    // The actual Entrance Room object — read by InvasionManager.PickTargetRoom so gate-siege survivors'
    // first stop is always this literal room, not whatever's built next to it.
    public RoomBase EntranceRoom => entranceRoom;
    [SerializeField] private float floorHeight = 2f; // vertical world-unit distance between floors

    [Header("Buildable Bounds")]
    // Mutable (not const) on purpose: nothing expands these yet, but a future tech unlock will call
    // ExpandBuildableBounds() below rather than needing a refactor to make these editable at runtime.
    [SerializeField] private float maxGridXFromEntrance = 20f;
    [SerializeField] private int maxFloorDepth = 10;

    private Dictionary<int, List<RoomBase>> roomsByFloor = new Dictionary<int, List<RoomBase>>();
    private List<LiftRoom> allLifts = new List<LiftRoom>();
    private readonly HashSet<int> pendingLiftColumnRegroups = new HashSet<int>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Derives which floor a world Y position belongs to, using the entrance room as floor 1 and
    // each floorHeight step BELOW it as the next floor number (2, 3, 4...) — matching this project's
    // convention where the entrance is always floor 1 and floor numbers increase going down.
    // Rooms must be vertically snapped to a consistent floorHeight grid for this to line up.
    public int GetFloorIndexForY(float worldY)
    {
        if (entranceRoom == null) return 1;
        return 1 + Mathf.RoundToInt((entranceRoom.transform.position.y - worldY) / floorHeight);
    }

    // Inverse of GetFloorIndexForY — needed by the build system to actually place a room at a validated
    // floor index (rather than just detecting which floor an existing world Y belongs to).
    public float GetWorldYForFloor(int floorIndex)
    {
        if (entranceRoom == null) return 0f;
        return entranceRoom.transform.position.y - (floorIndex - 1) * floorHeight;
    }

    public float EntranceWorldX => entranceRoom != null ? entranceRoom.transform.position.x : 0f;
    public float EntranceWorldZ => entranceRoom != null ? entranceRoom.transform.position.z : 0f;
    public int EntranceFloorIndex => entranceRoom != null ? entranceRoom.FloorIndex : 1;

    // The entrance room's own outward-facing edge (the higher-X side, since higher X = visually left in
    // this project — see GetRouteToSpot's BaseEntrance-ordering comment) — the boundary past which
    // nothing may be built on the entrance's OWN floor, since that's where bunnies physically enter/exit
    // the base (gate, queue spots, BaseEntrance transform), not real floor space. Only meaningful on
    // EntranceFloorIndex — floors below it have no gate to protect and may build past this X freely (see
    // RoomPlacementValidator.IsValidPlacement).
    public float EntranceLeftEdgeX => entranceRoom != null ? entranceRoom.transform.position.x + entranceRoom.FootprintWidth / 2f : 0f;

    // Called by EntranceRoom.OnEnable() so this reference stays valid across a room-upgrade swap, where
    // RoomTransitionService destroys the old EntranceRoom instance and instantiates a new one in its
    // place. Without this, the Inspector-wired reference above goes stale the moment the old instance is
    // actually destroyed (end of frame) — entranceRoom == null thereafter — and every Y<->floor
    // conversion above silently falls back to its 0f/floor-1 default, misaligning every room's grid
    // position by however far off that default is from the real entrance Y.
    public void SetEntranceRoom(RoomBase room)
    {
        entranceRoom = room;
    }

    public float MaxGridXFromEntrance => maxGridXFromEntrance;
    public int MaxFloorDepth => maxFloorDepth;

    // Entry point for a future tech-unlock system to grow the buildable area — deliberately additive
    // (not a setter) so multiple unlocks can each contribute without needing to know the current total.
    public void ExpandBuildableBounds(float additionalGridX, int additionalFloorDepth)
    {
        maxGridXFromEntrance += additionalGridX;
        maxFloorDepth += additionalFloorDepth;
        OnBuildableBoundsChanged?.Invoke();
    }

    public void RegisterRoom(RoomBase room)
    {
        RegisterRoomOnFloor(room, room.FloorIndex);
    }

    public void UnregisterRoom(RoomBase room)
    {
        UnregisterRoomOnFloor(room, room.FloorIndex);
    }

    // Lets a room (namely a lift) appear on a floor other than its own single FloorIndex — a lift
    // spans multiple floors and needs to be reachable/pathable-to from every one of them, not just
    // the one its inherited FloorIndex happens to point at.
    public void RegisterRoomOnFloor(RoomBase room, int floorIndex)
    {
        if (!roomsByFloor.ContainsKey(floorIndex))
            roomsByFloor[floorIndex] = new List<RoomBase>();

        if (!roomsByFloor[floorIndex].Contains(room))
            roomsByFloor[floorIndex].Add(room);

        RequestDoorwayRefresh(floorIndex);
    }

    public void UnregisterRoomOnFloor(RoomBase room, int floorIndex)
    {
        if (roomsByFloor.ContainsKey(floorIndex))
            roomsByFloor[floorIndex].Remove(room);

        RequestDoorwayRefresh(floorIndex);
    }

    // ---------- DOORWAY ADJACENCY (dynamic wall filler / door frame) ----------
    // See Docs/RoomVisualSystems_Design.md Feature A. Deliberately centralized here rather than added
    // to BuildModeController/RoomTransitionService/DeleteModeController individually — every placement,
    // deletion, merge/upgrade swap, and scene-load registration already funnels through
    // RegisterRoomOnFloor/UnregisterRoomOnFloor above, so hooking in here covers all of them for free.

    private readonly HashSet<int> dirtyDoorwayFloors = new HashSet<int>();
    private Coroutine doorwayRefreshRoutine;

    // Deferred one frame (same pattern as RequestLiftColumnRegroup) so multiple rooms registering or
    // unregistering on the same floor within one frame — e.g. a multi-room merge's Evacuate->Swap, or
    // several hand-placed rooms all enabling at scene load — collapse into a single recompute pass
    // instead of recomputing (and briefly showing stale state) after each individual change.
    private void RequestDoorwayRefresh(int floorIndex)
    {
        dirtyDoorwayFloors.Add(floorIndex);
        if (doorwayRefreshRoutine == null)
            doorwayRefreshRoutine = StartCoroutine(DoorwayRefreshRoutine());
    }

    private IEnumerator DoorwayRefreshRoutine()
    {
        yield return null; // let this frame's registrations/unregistrations settle first
        foreach (int floor in dirtyDoorwayFloors)
            RefreshDoorwaysForFloor(floor);
        dirtyDoorwayFloors.Clear();
        doorwayRefreshRoutine = null;
    }

    // Recomputes every room's left/right doorway state on a floor from scratch, purely from live
    // transform positions (never cached) — same edge-touching adjacency test RoomPlacementValidator and
    // RoomMergeResolver already use, so a room only ever shows an open doorway on a side that's
    // genuinely touching another registered room right now.
    private void RefreshDoorwaysForFloor(int floorIndex)
    {
        List<RoomBase> rooms = GetOrderedRooms(floorIndex);

        foreach (RoomBase room in rooms)
        {
            RoomPlacementValidator.GetInterval(room, out float min, out float max);
            bool hasLeftNeighbor = false;  // higher-X side — see "higher X = visually left" convention above
            bool hasRightNeighbor = false; // lower-X side

            foreach (RoomBase other in rooms)
            {
                if (other == room) continue;

                RoomPlacementValidator.GetInterval(other, out float otherMin, out float otherMax);
                if (Mathf.Abs(otherMin - max) < RoomPlacementValidator.Epsilon) hasLeftNeighbor = true;
                if (Mathf.Abs(otherMax - min) < RoomPlacementValidator.Epsilon) hasRightNeighbor = true;
            }

            room.SetLeftDoorwayOpen(hasLeftNeighbor);
            room.SetRightDoorwayOpen(hasRightNeighbor);
        }

        OnFloorLayoutChanged?.Invoke(floorIndex);
    }

    // Every floor that currently has at least one registered room (or lift stop) on it.
    public List<int> GetAllFloorIndices()
    {
        return roomsByFloor.Keys.ToList();
    }

    public void RegisterLift(LiftRoom lift)
    {
        if (!allLifts.Contains(lift))
            allLifts.Add(lift);
    }

    public void UnregisterLift(LiftRoom lift)
    {
        allLifts.Remove(lift);
    }

    // Schedules a shaft (re)grouping pass for every LiftRoom segment sharing this X column, deferred
    // one frame so multiple segments added/removed in the same frame collapse into a single pass.
    // Lives here (not on LiftRoom itself) because a segment that's being destroyed can't reliably run
    // its own coroutine to defer this — this manager is a persistent singleton that can.
    public void RequestLiftColumnRegroup(int gridX)
    {
        if (!pendingLiftColumnRegroups.Add(gridX)) return;
        StartCoroutine(RegroupLiftColumnNextFrame(gridX));
    }

    private IEnumerator RegroupLiftColumnNextFrame(int gridX)
    {
        yield return null;
        pendingLiftColumnRegroups.Remove(gridX);
        LiftRoom.RegroupColumn(gridX);
    }

    // Finds a lift that services both floors, so a bunny can ride it between them. originRoom/
    // targetRoom (where the bunny actually is and where it's actually trying to go) are used to
    // prefer a shaft that's genuinely USABLE for this specific trip over one that merely spans the
    // right two floors: two lift shafts can both service the same floor pair while each having its
    // own attached rooms that never connect to the other shaft's (see IsContiguousRun) — picking the
    // wrong one strands the bunny on the wrong side of an unbuilt gap once it disembarks. Among
    // shafts where both the origin-floor walk (originRoom -> boarding segment) and the destination-
    // floor walk (landing segment -> targetRoom) are physically contiguous, the nearest one wins;
    // if none qualify (e.g. neither shaft actually reaches the target without a gap), falls back to
    // nearest among ALL servicing shafts so a trip is still attempted rather than doing nothing.
    public LiftRoom FindLiftServicing(int floorA, int floorB, RoomBase originRoom, RoomBase targetRoom)
    {
        List<LiftRoom> candidates = allLifts.Where(l => l.ServicesFloor(floorA) && l.ServicesFloor(floorB)).ToList();
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1 || originRoom == null || targetRoom == null) return candidates[0];

        List<LiftRoom> fullyReachable = candidates
            .Where(l => IsRoomReachableOnFloor(originRoom, l.GetSegmentForFloor(floorA), floorA)
                     && IsRoomReachableOnFloor(l.GetSegmentForFloor(floorB), targetRoom, floorB))
            .ToList();

        List<LiftRoom> pool = fullyReachable.Count > 0 ? fullyReachable : candidates;
        return pool.OrderBy(l => Mathf.Abs(l.GridX - originRoom.GridX) + Mathf.Abs(l.GridX - targetRoom.GridX)).First();
    }

    // True if `from` and `to` are the same room, or connected by an unbroken run of touching rooms
    // on the given floor (see IsContiguousRun). False if either isn't registered on that floor.
    private bool IsRoomReachableOnFloor(RoomBase from, RoomBase to, int floorIndex)
    {
        if (from == null || to == null) return false;
        if (from == to) return true;

        List<RoomBase> ordered = GetOrderedRooms(floorIndex);
        int fromIndex = ordered.IndexOf(from);
        int toIndex = ordered.IndexOf(to);
        if (fromIndex == -1 || toIndex == -1) return false;

        return IsContiguousRun(ordered, fromIndex, toIndex);
    }

    // True if every room from ordered[fromIndex] to ordered[toIndex] (inclusive, walking whichever
    // direction connects them) touches the next one — i.e. genuinely walkable floor space with no
    // unbuilt gap. Two rooms can be adjacent in this GridX-sorted list without being physically
    // connected: a lift segment only needs to stack on its own shaft column to be placed (see
    // RoomPlacementValidator), not touch a neighbor on its own floor, so two independently-grown
    // lift+room clusters can end up sharing a floor with an unbuilt gap between them.
    private static bool IsContiguousRun(List<RoomBase> ordered, int fromIndex, int toIndex)
    {
        int step = toIndex >= fromIndex ? 1 : -1;
        for (int i = fromIndex; i != toIndex; i += step)
        {
            RoomPlacementValidator.GetInterval(ordered[i], out float min, out float max);
            RoomPlacementValidator.GetInterval(ordered[i + step], out float nextMin, out float nextMax);
            bool touching = Mathf.Abs(max - nextMin) < RoomPlacementValidator.Epsilon
                || Mathf.Abs(min - nextMax) < RoomPlacementValidator.Epsilon;
            if (!touching) return false;
        }
        return true;
    }

    private List<RoomBase> GetOrderedRooms(int floor)
    {
        if (!roomsByFloor.ContainsKey(floor)) return new List<RoomBase>();
        return roomsByFloor[floor].OrderBy(r => r.GridX).ToList();
    }

    // startFloorOverride: the floor the bunny is ACTUALLY standing on right now. Defaults to
    // startRoom.FloorIndex, which is correct for every normal room (one room, one floor) — but a
    // LiftRoom's FloorIndex only ever represents its primary floor, so callers routing a bunny that's
    // currently on a lift's landing spot on some OTHER floor must pass that floor explicitly.
    public List<Transform> GetRouteToSpot(RoomBase startRoom, RoomSpot startSpot, Transform startWanderPoint, RoomBase targetRoom, RoomSpot targetSpot, int startFloorOverride = -1)
    {
        List<Transform> fullPath = new List<Transform>();

        // Starting from the base entrance (outside arrival) rather than a known room
        if (startRoom == null)
        {
            List<RoomBase> orderedFromEntrance = GetOrderedRooms(targetRoom.FloorIndex);
            int targetIndexFromEntrance = orderedFromEntrance.IndexOf(targetRoom);

            if (targetIndexFromEntrance == -1)
            {
                Debug.LogWarning("Target room not registered in BaseLayoutManager.");
                fullPath.Add(targetSpot.transform);
                return fullPath;
            }

            if (!IsContiguousRun(orderedFromEntrance, orderedFromEntrance.Count - 1, targetIndexFromEntrance))
            {
                Debug.LogWarning($"No contiguous floor path from the base entrance to {targetRoom.name} on floor {targetRoom.FloorIndex} (unbuilt gap in between).");
                return fullPath;
            }

            fullPath.Add(baseEntrance);

            // BaseEntrance sits at the visually-leftmost position, which is the LAST index
            // in our ascending-GridX-sorted list (since higher X = visually left in this project).
            for (int i = orderedFromEntrance.Count - 1; i > targetIndexFromEntrance; i--)
            {
                fullPath.AddRange(orderedFromEntrance[i].GetPassThroughPath(enteringFromLeft: true, targetRoom.FloorIndex));
            }

            fullPath.AddRange(targetRoom.GetPathBetweenEntranceAndSpot(targetSpot, targetRoom.LeftEntrance));
            return fullPath;
        }

        int startFloor = startFloorOverride >= 0 ? startFloorOverride : startRoom.FloorIndex;

        if (startRoom == targetRoom)
        {
            // Prefer a known RoomSpot, then a remembered wander point, and only fall back to the
            // room's own root Transform (its grid-alignment pivot, NOT a walkable point) as a last resort.
            Transform startPoint = startSpot != null ? startSpot.transform : startWanderPoint;

            if (startPoint != null)
            {
                // Try the room's authored path first — entrance-to-spot, OR spot-to-spot (e.g. a
                // Defending reroute from a claimed job/relax/guard spot to a CombatSpot, see
                // RoomSpotPathAutoPopulator) — so a same-room move doesn't cut a straight line through
                // walls/scenery the path was specifically routed around. GetPathBetweenEntranceAndSpot
                // matches RoomPath.entrance by reference, and that field isn't actually restricted to a
                // real room entrance — any Transform (including another RoomSpot's) works. Falls back to
                // a straight line (with a warning) if no RoomPath's entrance matches startPoint.
                return startRoom.GetPathBetweenEntranceAndSpot(targetSpot, startPoint);
            }

            fullPath.Add(startRoom.transform);
            fullPath.Add(targetSpot.transform);
            return fullPath;
        }

        if (startFloor != targetRoom.FloorIndex)
        {
            Debug.LogWarning("Cross-floor pathing (lifts) not yet supported.");
            return fullPath;
        }

        List<RoomBase> ordered = GetOrderedRooms(startFloor);
        int startIndex = ordered.IndexOf(startRoom);
        int targetIndex = ordered.IndexOf(targetRoom);

        if (startIndex == -1 || targetIndex == -1)
        {
            Debug.LogWarning("Room not registered in BaseLayoutManager.");
            return fullPath;
        }

        if (!IsContiguousRun(ordered, startIndex, targetIndex))
        {
            Debug.LogWarning($"No contiguous floor path from {startRoom.name} to {targetRoom.name} on floor {startFloor} (unbuilt gap in between) — refusing to route through it.");
            return fullPath;
        }

        bool movingRight = targetIndex > startIndex;
        int step = movingRight ? 1 : -1;

        Transform exitEntrance = movingRight ? startRoom.GetLeftEntranceForFloor(startFloor) : startRoom.GetRightEntranceForFloor(startFloor);
        if (startSpot != null)
        {
            fullPath.AddRange(startRoom.GetPathFromSpotToEntrance(startSpot, exitEntrance));
        }
        else
        {
            // No claimed RoomSpot to path from (e.g. just disembarked a lift at a landing spot) — start
            // from wherever the bunny is actually standing instead of jumping straight to the entrance,
            // which would cut a diagonal line across the room if the two aren't in the same place.
            if (startWanderPoint != null && startWanderPoint != exitEntrance)
                fullPath.Add(startWanderPoint);
            fullPath.Add(exitEntrance);
        }

        for (int i = startIndex + step; i != targetIndex; i += step)
        {
            fullPath.AddRange(ordered[i].GetPassThroughPath(enteringFromLeft: !movingRight, startFloor));
        }

        Transform entryEntrance = movingRight ? targetRoom.GetRightEntranceForFloor(startFloor) : targetRoom.GetLeftEntranceForFloor(startFloor);
        fullPath.AddRange(targetRoom.GetPathBetweenEntranceAndSpot(targetSpot, entryEntrance));

        return fullPath;
    }

    public List<Transform> GetRouteToWanderPoint(RoomBase startRoom, RoomSpot startSpot, Transform startWanderPoint, RoomBase targetRoom, Transform destination, int startFloorOverride = -1)
    {
        List<Transform> fullPath = new List<Transform>();

        if (startRoom == null)
        {
            List<RoomBase> orderedFromEntrance = GetOrderedRooms(targetRoom.FloorIndex);
            int targetIdx = orderedFromEntrance.IndexOf(targetRoom);
            if (targetIdx == -1) return fullPath;

            if (!IsContiguousRun(orderedFromEntrance, orderedFromEntrance.Count - 1, targetIdx))
            {
                Debug.LogWarning($"No contiguous floor path from the base entrance to {targetRoom.name} on floor {targetRoom.FloorIndex} (unbuilt gap in between).");
                return fullPath;
            }

            fullPath.Add(baseEntrance);
            for (int i = orderedFromEntrance.Count - 1; i > targetIdx; i--)
                fullPath.AddRange(orderedFromEntrance[i].GetPassThroughPath(enteringFromLeft: true, targetRoom.FloorIndex));

            if (destination != targetRoom.LeftEntrance)
                fullPath.Add(targetRoom.LeftEntrance);
            fullPath.Add(destination);
            return fullPath;
        }

        int startFloor = startFloorOverride >= 0 ? startFloorOverride : startRoom.FloorIndex;

        if (startRoom == targetRoom)
        {
            // Prefer a known RoomSpot, then a remembered wander point, and only fall back to the
            // room's own root Transform (its grid-alignment pivot, NOT a walkable point) as a last resort.
            Transform startPoint = startSpot != null ? startSpot.transform : (startWanderPoint != null ? startWanderPoint : startRoom.transform);
            fullPath.Add(startPoint);
            fullPath.Add(destination);
            return fullPath;
        }

        List<RoomBase> ordered = GetOrderedRooms(startFloor);
        int startIndex = ordered.IndexOf(startRoom);
        int targetIndex = ordered.IndexOf(targetRoom);
        if (startIndex == -1 || targetIndex == -1) return fullPath;

        if (!IsContiguousRun(ordered, startIndex, targetIndex))
        {
            Debug.LogWarning($"No contiguous floor path from {startRoom.name} to {targetRoom.name} on floor {startFloor} (unbuilt gap in between) — refusing to route through it.");
            return fullPath;
        }

        bool movingRight = targetIndex > startIndex;
        int step = movingRight ? 1 : -1;

        Transform exitEntrance = movingRight ? startRoom.GetLeftEntranceForFloor(startFloor) : startRoom.GetRightEntranceForFloor(startFloor);
        if (startSpot != null)
        {
            fullPath.AddRange(startRoom.GetPathFromSpotToEntrance(startSpot, exitEntrance));
        }
        else
        {
            // Same reasoning as GetRouteToSpot: start from the bunny's actual current position (e.g. a
            // lift landing spot) rather than jumping straight to the entrance.
            if (startWanderPoint != null && startWanderPoint != exitEntrance)
                fullPath.Add(startWanderPoint);
            fullPath.Add(exitEntrance);
        }

        for (int i = startIndex + step; i != targetIndex; i += step)
            fullPath.AddRange(ordered[i].GetPassThroughPath(enteringFromLeft: !movingRight, startFloor));

        Transform entryEntrance = movingRight ? targetRoom.GetRightEntranceForFloor(startFloor) : targetRoom.GetLeftEntranceForFloor(startFloor);
        if (destination != entryEntrance)
            fullPath.Add(entryEntrance);
        fullPath.Add(destination);

        return fullPath;
    }
    public List<RoomBase> GetAllRoomsOnFloor(int floor)
    {
        return GetOrderedRooms(floor);
    }

    // Every room across every floor, unordered — unlike GetAllRoomsOnFloor, callers here don't care about
    // left-to-right position (see InvasionManager's in-room spawn type, which has no floor restriction and
    // no reason to prefer one floor's ordering over another's).
    public List<RoomBase> GetAllRooms()
    {
        List<RoomBase> all = new List<RoomBase>();
        foreach (List<RoomBase> floorRooms in roomsByFloor.Values)
            all.AddRange(floorRooms);
        return all;
    }
}