using UnityEngine.EventSystems;

// OnMouseDown fires from Unity's own automatic camera-to-collider physics raycast, which has no
// awareness of the UI EventSystem — clicking a UI button does not stop OnMouseDown from also firing on
// whatever 3D collider happens to sit behind that button on screen. Every OnMouseDown-based click
// handler that opens a UI panel needs this guard; BuildModeController/DeleteModeController already do
// the equivalent check for their own mouse-driven world actions.
public static class UIPointerGuard
{
    public static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
