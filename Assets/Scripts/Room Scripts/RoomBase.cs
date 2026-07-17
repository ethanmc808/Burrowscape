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

    // How wide this room's footprint is in world-X units. Tracked per-instance (not just read off a
    // RoomDefinition at build time) so a future merge/upgrade system can change a room's width by
    // swapping its prefab without needing to look up catalog data it may no longer have a 1:1 entry in.
    [SerializeField] protected float footprintWidth = 4f;
    public virtual float FootprintWidth => footprintWidth;

    // Grade 1-3, for the future room-upgrade system. Every room starts at Grade 1; upgrading is a
    // separate future system that isn't built yet.
    [SerializeField] protected int grade = 1;
    public int Grade => grade;

    // Identifies a room's TYPE (e.g. "Garden", "Kitchen", "Storage Room") independent of which
    // MonoBehaviour subclass it uses — needed because purely decorative room types share the bare
    // RoomBase class with no way to tell them apart via GetType(). Authored per-prefab, same pattern
    // as footprintWidth/grade. Merge eligibility and the upgrade Swap-step catalog lookup both key off
    // this rather than the concrete component type.
    [SerializeField] protected string roomTypeId;
    public string RoomTypeId => roomTypeId;

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

    // Query only — Unity gives no way to veto an in-progress Destroy(), so whatever future
    // build/demolish system removes rooms must call this BEFORE calling Destroy(), not rely on it to
    // block anything by itself. Default: blocked if any bunny is currently in or assigned to this room.
    // LiftRoom additionally requires the whole shaft (not just this segment) to be idle.
    public virtual bool CanBeDeleted(out string blockedReason)
    {
        if (DwellerRoster.Instance != null && DwellerRoster.Instance.IsRoomOccupied(this))
        {
            blockedReason = "a bunny is inside or assigned to this room";
            return false;
        }

        blockedReason = null;
        return true;
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