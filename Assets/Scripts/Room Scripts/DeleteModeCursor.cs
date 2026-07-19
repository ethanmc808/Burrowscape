using UnityEngine;
using UnityEngine.UI;

// Replaces the system cursor with an animated hammer icon while Delete mode is active. A plain UI
// Image tracking the mouse in screen space, not Cursor.SetCursor — Unity's cursor API only takes a
// single static texture per call, so animating it would mean juggling texture swaps plus OS-level
// hotspot/size quirks. Hiding the system cursor and driving a UI Image instead gives full control.
//
// The GameObject itself stays active at all times — toggling it off would stop Unity from ever calling
// this script's own Start() again (a GameObject disabled from within its own Awake() never gets Start()
// called), which would permanently break the PlacementModeManager.OnModeChanged subscription below.
// Visibility is toggled via the Image component instead, which doesn't touch this script's lifecycle.
public class DeleteModeCursor : MonoBehaviour
{
    [SerializeField] private RectTransform canvasRectTransform; // the parent Canvas's own RectTransform
    [SerializeField] private float bobAmplitude = 8f; // pixels
    [SerializeField] private float bobSpeed = 6f;

    private RectTransform selfRect;
    private Image image;
    private bool isActive;

    private void Awake()
    {
        selfRect = GetComponent<RectTransform>();
        image = GetComponent<Image>();
        SetVisible(false);
    }

    private void Start()
    {
        // Start(), not OnEnable() — see BuildModeController for why (singleton Awake ordering).
        if (PlacementModeManager.Instance != null)
            PlacementModeManager.Instance.OnModeChanged += HandleModeChanged;
    }

    private void HandleModeChanged(PlacementMode mode)
    {
        isActive = mode == PlacementMode.Delete;
        SetVisible(isActive);
    }

    private void SetVisible(bool visible)
    {
        image.enabled = visible;
        Cursor.visible = !visible;
    }

    private void Update()
    {
        if (!isActive || canvasRectTransform == null) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRectTransform, Input.mousePosition, null, out Vector2 localPoint))
        {
            float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
            selfRect.anchoredPosition = localPoint + new Vector2(0f, bobOffset);
        }
    }
}
