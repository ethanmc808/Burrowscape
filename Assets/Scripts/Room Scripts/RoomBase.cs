using UnityEngine;
using System.Collections.Generic;

public class RoomBase : MonoBehaviour
{
    [Header("Entrances")]
    [SerializeField] protected Transform leftEntrance;
    [SerializeField] protected Transform rightEntrance;
    [SerializeField] protected Transform middleLeft;
    [SerializeField] protected Transform middleRight;

    [Header("Pass-Through Path (for bunnies just walking through this room)")]
    [SerializeField] protected List<Transform> passThroughWaypoints; // ordered left-to-right

    [Header("Spot Paths")]
    [SerializeField] protected List<RoomPath> paths; // entrance-to-spot paths, shared by Garden/Cafeteria

    public Transform LeftEntrance => leftEntrance;
    public Transform RightEntrance => rightEntrance;
    public Transform MiddleLeft => middleLeft;
    public Transform MiddleRight => middleRight;
    public List<Transform> PassThroughWaypoints => passThroughWaypoints;
    public List<Transform> GetWanderPoints()
    {
        List<Transform> points = new List<Transform>();
        if (leftEntrance != null) points.Add(leftEntrance);
        if (rightEntrance != null) points.Add(rightEntrance);
        if (middleLeft != null) points.Add(middleLeft);
        if (middleRight != null) points.Add(middleRight);
        return points;
    }
    // Auto-detected from this room's world Y position (see BaseLayoutManager.GetFloorIndexForY) the
    // moment it registers itself — there's no manually-typed field to forget to update, so a room's
    // floor can never silently drift out of sync with where it's actually placed. Rooms must be
    // vertically snapped to a consistent floorHeight grid for this to line up (same requirement
    // LiftRoom already has for stacking its segments).
    public int FloorIndex { get; private set; }
    public int GridX => Mathf.RoundToInt(transform.position.x);

    // Floor-aware entrance/waypoint lookups. A normal room only has one set of these regardless of
    // floor (the floorIndex parameter is ignored), but LiftRoom overrides all three to return the
    // correct set for whichever floor is actually being routed through — a lift spans multiple floors
    // and its single inherited FloorIndex only ever represents one of them.
    public virtual Transform GetLeftEntranceForFloor(int floorIndex) => leftEntrance;
    public virtual Transform GetRightEntranceForFloor(int floorIndex) => rightEntrance;
    public virtual List<Transform> GetPassThroughWaypointsForFloor(int floorIndex) => passThroughWaypoints;

    protected virtual void OnEnable()
    {
        if (BaseLayoutManager.Instance != null)
        {
            FloorIndex = BaseLayoutManager.Instance.GetFloorIndexForY(transform.position.y);
            BaseLayoutManager.Instance.RegisterRoom(this);
        }
        else
        {
            Debug.LogWarning($"{name}: BaseLayoutManager.Instance was null during OnEnable.");
        }
    }

    protected virtual void OnDisable()
    {
        if (BaseLayoutManager.Instance != null)
            BaseLayoutManager.Instance.UnregisterRoom(this);
    }

    // Path for a bunny walking straight through this room (not stopping at any spot), on a specific floor.
    public List<Transform> GetPassThroughPath(bool enteringFromLeft, int floorIndex)
    {
        Transform left = GetLeftEntranceForFloor(floorIndex);
        Transform right = GetRightEntranceForFloor(floorIndex);
        List<Transform> waypoints = GetPassThroughWaypointsForFloor(floorIndex);

        List<Transform> path = new List<Transform>();

        if (enteringFromLeft)
        {
            path.Add(left);
            path.AddRange(waypoints);
            path.Add(right);
        }
        else
        {
            path.Add(right);
            List<Transform> reversed = new List<Transform>(waypoints);
            reversed.Reverse();
            path.AddRange(reversed);
            path.Add(left);
        }

        return path;
    }

    // Path from a specific entrance to a specific spot (used when this room is the FINAL destination)
    public List<Transform> GetPathBetweenEntranceAndSpot(RoomSpot spot, Transform fromEntrance)
    {
        List<Transform> match = FindMatchingPath(spot, fromEntrance);
        if (match != null) return match;

        // Fallback if no exact path defined — shouldn't normally happen once paths are set up
        Debug.LogWarning($"No RoomPath found from {fromEntrance.name} to {spot.name} on {name}.");
        return new List<Transform> { fromEntrance, spot.transform };
    }

    // Path from a specific spot OUT to a specific entrance (used when this room is the STARTING room)
    public List<Transform> GetPathFromSpotToEntrance(RoomSpot spot, Transform towardEntrance)
    {
        List<Transform> match = FindMatchingPath(spot, towardEntrance);
        if (match == null)
        {
            Debug.LogWarning($"No RoomPath found from {spot.name} to {towardEntrance.name} on {name}.");
            return new List<Transform> { spot.transform, towardEntrance };
        }

        match.Reverse();
        return match;
    }

    private List<Transform> FindMatchingPath(RoomSpot spot, Transform entrance)
    {
        foreach (RoomPath p in paths)
        {
            if (p.targetSpot == spot && p.entrance == entrance)
            {
                List<Transform> result = new List<Transform> { p.entrance };
                result.AddRange(p.waypoints);
                result.Add(spot.transform);
                return result;
            }
        }
        return null;
    }
}