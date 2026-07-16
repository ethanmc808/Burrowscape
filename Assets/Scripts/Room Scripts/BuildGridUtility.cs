using UnityEngine;

// Grid snapping math for room placement. The "1 unit in X" grid from the design spec is really a grid
// of 1-unit-spaced EDGE/boundary lines anchored to the entrance room, not a grid of 1-unit-spaced room
// CENTERS — rooms are center-pivoted (confirmed against DefaultRoom_4x2x6_Grade1.prefab: its walls sit
// at local x = +/-1.95, centered on the root), so an even-width room's center lands on an integer
// boundary line while the odd-width (1-wide) LiftRoom's center lands exactly halfway between two of
// them. Both are correct as long as every room's EDGES land on the shared boundary grid, which is what
// SnapCenterX actually guarantees.
public static class BuildGridUtility
{
    public static float SnapCenterX(float desiredWorldX, float footprintWidth, float anchorX)
    {
        float edgeOffsetFromAnchor = (desiredWorldX - anchorX) - footprintWidth / 2f;
        float snappedEdgeOffset = Mathf.Round(edgeOffsetFromAnchor);
        return anchorX + snappedEdgeOffset + footprintWidth / 2f;
    }
}
