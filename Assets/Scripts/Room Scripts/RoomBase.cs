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

    [Header("Grid")]
    [SerializeField] private int floorIndex = 0; // set this per-instance when the room is placed

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
    public int FloorIndex => floorIndex;
    public int GridX => Mathf.RoundToInt(transform.position.x);

    protected virtual void OnEnable()
    {
        if (BaseLayoutManager.Instance != null)
            BaseLayoutManager.Instance.RegisterRoom(this);
        else
            Debug.LogWarning($"{name}: BaseLayoutManager.Instance was null during OnEnable.");
    }

    protected virtual void OnDisable()
    {
        if (BaseLayoutManager.Instance != null)
            BaseLayoutManager.Instance.UnregisterRoom(this);
    }

    // Path for a bunny walking straight through this room (not stopping at any spot)
    public List<Transform> GetPassThroughPath(bool enteringFromLeft)
    {
        List<Transform> path = new List<Transform>();

        if (enteringFromLeft)
        {
            path.Add(leftEntrance);
            path.AddRange(passThroughWaypoints);
            path.Add(rightEntrance);
        }
        else
        {
            path.Add(rightEntrance);
            List<Transform> reversed = new List<Transform>(passThroughWaypoints);
            reversed.Reverse();
            path.AddRange(reversed);
            path.Add(leftEntrance);
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