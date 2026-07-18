using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class BaseLayoutManager : MonoBehaviour
{
    public static BaseLayoutManager Instance { get; private set; }

    [SerializeField] private Transform baseEntrance;
    public Transform BaseEntrance => baseEntrance;

    [Header("Floor Detection")]
    [SerializeField] private RoomBase entranceRoom; // reference point for Y -> floor conversion; initial scene wiring only — see SetEntranceRoom
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
    }

    public void UnregisterRoomOnFloor(RoomBase room, int floorIndex)
    {
        if (roomsByFloor.ContainsKey(floorIndex))
            roomsByFloor[floorIndex].Remove(room);
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

    // Finds a lift that services both floors, so a bunny can ride it between them.
    public LiftRoom FindLiftServicing(int floorA, int floorB)
    {
        foreach (LiftRoom lift in allLifts)
        {
            if (lift.ServicesFloor(floorA) && lift.ServicesFloor(floorB))
                return lift;
        }
        return null;
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
            if (startSpot == null && startWanderPoint != null)
            {
                // Try the room's authored entrance-to-spot path first, so a same-room assignment
                // doesn't cut a straight line through walls/scenery the path was specifically routed
                // around. Falls back to a straight line (with a warning) if no RoomPath's entrance
                // matches wherever the bunny currently is.
                return startRoom.GetPathBetweenEntranceAndSpot(targetSpot, startWanderPoint);
            }

            // Prefer a known RoomSpot, then a remembered wander point, and only fall back to the
            // room's own root Transform (its grid-alignment pivot, NOT a walkable point) as a last resort.
            Transform startPoint = startSpot != null ? startSpot.transform : (startWanderPoint != null ? startWanderPoint : startRoom.transform);
            fullPath.Add(startPoint);
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
}