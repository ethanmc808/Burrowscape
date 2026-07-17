using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// Resolves whether a just-placed room should auto-merge with an immediate same-type, same-Grade
// neighbor, and if so which neighbor(s) and which target RoomDefinition to swap into. A pure query —
// callers (BuildModeController) decide when to invoke it (only on explicit player placement, never on
// BaseLayoutManager.RegisterRoom, which also fires for every hand-placed room at scene load) and what
// to do with the result (RoomTransitionService.MergeRooms).
public static class RoomMergeResolver
{
    public static bool TryResolveMerge(RoomBase newRoom, out List<RoomBase> roomsToMerge, out RoomDefinition targetDefinition)
    {
        roomsToMerge = null;
        targetDefinition = null;

        if (newRoom == null || string.IsNullOrEmpty(newRoom.RoomTypeId)) return false;
        // Entrance never merges (width is always fixed at 4x2x6); Lift never merges (extends vertically
        // only, never horizontally).
        if (newRoom is EntranceRoom || newRoom is LiftRoom) return false;
        if (RoomCatalogRegistry.Instance == null || BaseLayoutManager.Instance == null) return false;

        List<RoomBase> roomsOnFloor = BaseLayoutManager.Instance.GetAllRoomsOnFloor(newRoom.FloorIndex);
        RoomPlacementValidator.GetInterval(newRoom, out float newMin, out float newMax);

        RoomBase left = FindTouchingNeighbor(roomsOnFloor, newRoom, newMin, findLeftNeighbor: true);
        RoomBase right = FindTouchingNeighbor(roomsOnFloor, newRoom, newMax, findLeftNeighbor: false);

        bool leftQualifies = Qualifies(left, newRoom);
        bool rightQualifies = Qualifies(right, newRoom);

        if (!leftQualifies) left = null;
        if (!rightQualifies) right = null;

        if (left == null && right == null) return false;

        int grade = newRoom.Grade;
        float cap = RoomCatalogRegistry.Instance.MaxMergedWidth;

        // Step 1: both sides qualify — try merging all three into one room first.
        if (left != null && right != null)
        {
            float tripleWidth = left.FootprintWidth + newRoom.FootprintWidth + right.FootprintWidth;
            RoomDefinition tripleVariant = RoomCatalogRegistry.Instance.FindVariant(newRoom.RoomTypeId, Mathf.RoundToInt(tripleWidth), grade);

            if (tripleWidth <= cap + RoomPlacementValidator.Epsilon && tripleVariant != null)
            {
                roomsToMerge = new List<RoomBase> { left, newRoom, right };
                targetDefinition = tripleVariant;
                return true;
            }
        }

        // Step 2 (or the only path when just one side qualifies): evaluate each valid pairing and take
        // the larger one. leftPairWidth/rightPairWidth are only meaningful when that side is non-null.
        bool leftPairValid = TryGetPairVariant(newRoom, left, grade, cap, out RoomDefinition leftPairVariant, out float leftPairWidth);
        bool rightPairValid = TryGetPairVariant(newRoom, right, grade, cap, out RoomDefinition rightPairVariant, out float rightPairWidth);

        if (leftPairValid && rightPairValid)
        {
            // Larger resulting room wins (strictly more space-efficient). Tie is arbitrary — prefer left.
            if (leftPairWidth >= rightPairWidth)
            {
                roomsToMerge = new List<RoomBase> { left, newRoom };
                targetDefinition = leftPairVariant;
            }
            else
            {
                roomsToMerge = new List<RoomBase> { newRoom, right };
                targetDefinition = rightPairVariant;
            }
            return true;
        }

        if (leftPairValid)
        {
            roomsToMerge = new List<RoomBase> { left, newRoom };
            targetDefinition = leftPairVariant;
            return true;
        }

        if (rightPairValid)
        {
            roomsToMerge = new List<RoomBase> { newRoom, right };
            targetDefinition = rightPairVariant;
            return true;
        }

        // Step 3: the qualifying neighbor(s) don't produce anything valid (e.g. already at the cap) —
        // no merge happens. The new room just sits there as its own instance.
        return false;
    }

    private static bool Qualifies(RoomBase neighbor, RoomBase newRoom)
    {
        return neighbor != null
            && neighbor.RoomTypeId == newRoom.RoomTypeId
            && neighbor.Grade == newRoom.Grade;
    }

    private static bool TryGetPairVariant(RoomBase newRoom, RoomBase side, int grade, float cap, out RoomDefinition variant, out float width)
    {
        variant = null;
        width = 0f;
        if (side == null) return false;

        width = side.FootprintWidth + newRoom.FootprintWidth;
        variant = RoomCatalogRegistry.Instance.FindVariant(newRoom.RoomTypeId, Mathf.RoundToInt(width), grade);
        return width <= cap + RoomPlacementValidator.Epsilon && variant != null;
    }

    // Mirrors RoomPlacementValidator's own adjacency check (touching at exactly the shared boundary),
    // but returns the actual neighbor RoomBase rather than just a bool, and searches every room on the
    // floor (not just sorted-list neighbors) so it's robust to a newly-placed room filling a gap
    // between two others that weren't adjacent to each other before.
    private static RoomBase FindTouchingNeighbor(List<RoomBase> roomsOnFloor, RoomBase newRoom, float newEdge, bool findLeftNeighbor)
    {
        foreach (RoomBase candidate in roomsOnFloor)
        {
            if (candidate == newRoom) continue;

            RoomPlacementValidator.GetInterval(candidate, out float min, out float max);

            if (findLeftNeighbor && Mathf.Abs(max - newEdge) < RoomPlacementValidator.Epsilon)
                return candidate;
            if (!findLeftNeighbor && Mathf.Abs(min - newEdge) < RoomPlacementValidator.Epsilon)
                return candidate;
        }
        return null;
    }
}
