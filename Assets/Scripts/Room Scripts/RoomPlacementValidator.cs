using UnityEngine;
using System.Linq;

// The four placement checks from the design spec. Branches standard-room vs. lift-segment adjacency
// rather than using one universal adjacency function, since a lift's "extend the shaft downward" case
// is a genuinely different axis (vertical/floor-based) than every other room's (horizontal/same-floor).
public static class RoomPlacementValidator
{
    // Internal (not private) — RoomMergeResolver reuses this exact edge-touching math to find merge
    // neighbors rather than duplicating it.
    internal const float Epsilon = 0.01f;

    public static bool IsValidPlacement(RoomDefinition definition, float centerX, int floorIndex, out string reason)
    {
        BaseLayoutManager layout = BaseLayoutManager.Instance;
        if (layout == null)
        {
            reason = "base layout is not available";
            return false;
        }

        float width = definition.footprint.x;

        // 1. Floor range: never above the entrance (the base is underground).
        if (floorIndex < layout.EntranceFloorIndex)
        {
            reason = "can't build above the entrance";
            return false;
        }

        // 4. Buildable bounds.
        if (Mathf.Abs(centerX - layout.EntranceWorldX) > layout.MaxGridXFromEntrance)
        {
            reason = "outside the buildable area";
            return false;
        }
        if (floorIndex - layout.EntranceFloorIndex > layout.MaxFloorDepth)
        {
            reason = "too deep";
            return false;
        }

        var roomsOnFloor = layout.GetAllRoomsOnFloor(floorIndex);
        float targetMin = centerX - width / 2f;
        float targetMax = centerX + width / 2f;

        // 2. No overlap (touching at exactly the boundary is fine — that's what adjacency needs).
        foreach (RoomBase existing in roomsOnFloor)
        {
            GetInterval(existing, out float existingMin, out float existingMax);
            bool overlaps = targetMin < existingMax - Epsilon && targetMax > existingMin + Epsilon;
            if (overlaps)
            {
                reason = "overlaps an existing room";
                return false;
            }
        }

        // 3. Adjacency.
        bool touchesSomethingOnThisFloor = roomsOnFloor.Any(existing =>
        {
            GetInterval(existing, out float existingMin, out float existingMax);
            return Mathf.Abs(targetMin - existingMax) < Epsilon || Mathf.Abs(targetMax - existingMin) < Epsilon;
        });

        if (!definition.isLiftSegment)
        {
            if (!touchesSomethingOnThisFloor)
            {
                reason = "must be built next to an existing room";
                return false;
            }
            reason = null;
            return true;
        }

        // Lift segments: horizontal touch (same rule as above, needed to start a brand-new shaft) OR
        // stacked directly on an existing segment of the SAME shaft column on the immediately
        // adjacent floor (how a shaft extends deeper/taller).
        if (touchesSomethingOnThisFloor)
        {
            reason = null;
            return true;
        }

        bool stacksOnLiftColumn = HasLiftSegmentAt(layout, centerX, floorIndex - 1)
            || HasLiftSegmentAt(layout, centerX, floorIndex + 1);

        if (!stacksOnLiftColumn)
        {
            reason = "must be built next to an existing room, or stacked on this shaft's column";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool HasLiftSegmentAt(BaseLayoutManager layout, float centerX, int floorIndex)
    {
        return layout.GetAllRoomsOnFloor(floorIndex)
            .Any(r => r is LiftRoom && Mathf.Abs(r.transform.position.x - centerX) < Epsilon);
    }

    internal static void GetInterval(RoomBase room, out float min, out float max)
    {
        float half = room.FootprintWidth / 2f;
        min = room.transform.position.x - half;
        max = room.transform.position.x + half;
    }
}
