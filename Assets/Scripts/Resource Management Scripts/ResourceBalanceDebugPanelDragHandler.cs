using UnityEngine;
using UnityEngine.EventSystems;

// Lets the player click-drag the debug panel's header bar to reposition the whole panel — added because
// it can otherwise sit on top of other HUD elements. Moves the PARENT panel's RectTransform, not this
// header strip's own, since the header is just a thin child region within that panel. Divides by the
// Canvas's scaleFactor (CanvasScaler.ScaleWithScreenSize means one screen pixel of mouse movement isn't
// one anchored-position unit once the actual screen size differs from the reference resolution) so drag
// speed tracks the mouse 1:1 regardless of resolution.
public class ResourceBalanceDebugPanelDragHandler : MonoBehaviour, IDragHandler
{
    private RectTransform targetRect;
    private Canvas parentCanvas;

    public void Initialize(RectTransform target, Canvas canvas)
    {
        targetRect = target;
        parentCanvas = canvas;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (targetRect == null) return;
        float scale = parentCanvas != null ? parentCanvas.scaleFactor : 1f;
        targetRect.anchoredPosition += eventData.delta / scale;
    }
}
