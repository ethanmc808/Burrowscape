using UnityEngine;
using UnityEditor;

// EDITOR-ONLY TOOL — this file must live inside a folder literally named "Editor" anywhere under
// Assets (e.g. Assets/Editor/SnapToFloorTool.cs). It uses the UnityEditor namespace, which is not
// available outside the Editor, so putting it anywhere else will break your build.
//
// Select one or more decoration objects in the Scene, then press the shortcut (or use the menu) to
// drop them straight onto the floor's walkable surface. No raycasts, no colliders required — every
// room uses the exact same floor height, so this just snaps to that fixed, known Y value. Works
// regardless of where each model's pivot sits (center, base, corner, etc.) because it measures the
// actual rendered bounds and corrects for the offset between the pivot and the true bottom of the mesh.
public static class SnapToFloorTool
{
    // %#f = Ctrl+Shift+F on Windows/Linux, Cmd+Shift+F on Mac.
    private const string MenuPath = "Tools/Burrowscape/Snap Selected To Floor %#f";

    // The floor's walkable top surface, in world Y. Derived from the floor object's own transform:
    // position.y (-0.95) + half its scale.y (0.1 / 2 = 0.05) = -0.9. Every room uses this same floor
    // height, so this is a fixed constant rather than something measured per-room. Update this single
    // number if the floor's position or thickness ever changes project-wide.
    private const float FloorSurfaceY = -0.9f;

    [MenuItem(MenuPath)]
    private static void SnapSelectionToFloor()
    {
        if (Selection.gameObjects.Length == 0)
        {
            Debug.LogWarning("SnapToFloorTool: nothing selected.");
            return;
        }

        int snappedCount = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            if (TrySnapToFloor(go))
                snappedCount++;
        }

        Debug.Log($"SnapToFloorTool: snapped {snappedCount}/{Selection.gameObjects.Length} object(s) to floor.");
    }

    private static bool TrySnapToFloor(GameObject go)
    {
        Bounds bounds = GetRendererBounds(go);
        if (bounds.size == Vector3.zero)
        {
            Debug.LogWarning($"SnapToFloorTool: '{go.name}' has no Renderer to measure — skipped.");
            return false;
        }

        // Offset from wherever the object's pivot currently sits to the actual bottom of its rendered
        // mesh — this is what makes it work regardless of whether the model's pivot is centered, at
        // its base, or somewhere else entirely. X and Z are left completely untouched; only Y moves.
        float pivotToBottomOffset = go.transform.position.y - bounds.min.y;
        float targetY = FloorSurfaceY + pivotToBottomOffset;

        Undo.RecordObject(go.transform, "Snap To Floor");
        go.transform.position = new Vector3(go.transform.position.x, targetY, go.transform.position.z);

        return true;
    }

    private static Bounds GetRendererBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}