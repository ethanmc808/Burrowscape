using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class BaseLayoutManager : MonoBehaviour
{
    public static BaseLayoutManager Instance { get; private set; }

    [SerializeField] private Transform baseEntrance;
    public Transform BaseEntrance => baseEntrance;

    [Header("Floor Detection")]
    [SerializeField] private RoomBase entranceRoom; // reference point for Y -> floor conversion; there's only ever one per base, and it never moves
    [SerializeField] private float floorHeight = 4f; // vertical world-unit distance between floors

    private Dictionary<int, List<RoomBase>> roomsByFloor = new Dictionary<int, List<RoomBase>>();
    private List<LiftRoom> allLifts = new List<LiftRoom>();

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
            fullPath.AddRange(startRoom.GetPathFromSpotToEntrance(startSpot, exitEntrance));
        else
            fullPath.Add(exitEntrance);

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
            fullPath.AddRange(startRoom.GetPathFromSpotToEntrance(startSpot, exitEntrance));
        else
            fullPath.Add(exitEntrance);

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