using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class BuildModeController : MonoBehaviour
{
    public static BuildModeController Instance { get; private set; }

    [Tooltip("Optional — Escape key always works regardless.")]
    [SerializeField] private Button cancelButton;

    private RoomDefinition selectedDefinition;
    private RoomGhostPreview ghost;

    private float pendingCenterX;
    private int pendingFloorIndex;
    private bool pendingIsValid;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Defensive against duplicated/re-wired buttons carrying over stale Inspector OnClick()
        // entries — same pattern AssignmentUI uses for its Unassign button.
        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(ExitBuildMode);
        }
    }

    private void Start()
    {
        // Subscribe in Start(), not OnEnable() — PlacementModeManager is a singleton that initializes
        // in its own Awake(), and there's no cross-object ordering guarantee between two different
        // objects' Awake() calls. Start() is guaranteed to run after every object's Awake().
        if (PlacementModeManager.Instance != null)
            PlacementModeManager.Instance.OnModeChanged += HandleModeChanged;
    }

    private void HandleModeChanged(PlacementMode mode)
    {
        if (mode != PlacementMode.Build && selectedDefinition != null)
            CancelInternal();
    }

    // Called by BuildMenuUI when the player picks a catalog entry.
    public void EnterBuildMode(RoomDefinition definition)
    {
        if (definition == null || definition.prefab == null) return;

        CancelInternal();

        selectedDefinition = definition;
        PlacementModeManager.Instance?.RequestMode(PlacementMode.Build);
        ghost = RoomGhostPreview.Create(definition.prefab);
    }

    public void ExitBuildMode()
    {
        CancelInternal();
        if (PlacementModeManager.Instance != null && PlacementModeManager.Instance.CurrentMode == PlacementMode.Build)
            PlacementModeManager.Instance.RequestMode(PlacementMode.None);
    }

    private void CancelInternal()
    {
        selectedDefinition = null;
        if (ghost != null)
        {
            ghost.DestroyGhost();
            ghost = null;
        }
    }

    private void Update()
    {
        if (selectedDefinition == null) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ExitBuildMode();
            return;
        }

        UpdateGhostFromMouse();

        if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            TryConfirmPlacement();
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void UpdateGhostFromMouse()
    {
        BaseLayoutManager layout = BaseLayoutManager.Instance;
        Camera cam = Camera.main;
        if (layout == null || cam == null) return;

        // Every room sits at the same world Z (confirmed against Home_Base.unity), so the mouse's
        // world position can be recovered with a plane raycast instead of needing any ground collider.
        float planeZ = layout.EntranceWorldZ;
        Plane plane = new Plane(Vector3.forward, new Vector3(0f, 0f, planeZ));
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!plane.Raycast(ray, out float distance)) return;

        Vector3 hit = ray.GetPoint(distance);

        int floorIndex = layout.GetFloorIndexForY(hit.y);
        float snappedY = layout.GetWorldYForFloor(floorIndex);
        float snappedX = BuildGridUtility.SnapCenterX(hit.x, selectedDefinition.footprint.x, layout.EntranceWorldX);

        pendingCenterX = snappedX;
        pendingFloorIndex = floorIndex;
        pendingIsValid = RoomPlacementValidator.IsValidPlacement(selectedDefinition, snappedX, floorIndex, out _);

        ghost.SetPosition(new Vector3(snappedX, snappedY, planeZ));
        ghost.SetValid(pendingIsValid);
    }

    private void TryConfirmPlacement()
    {
        if (!pendingIsValid) return;

        if (GoldManager.Instance == null || !GoldManager.Instance.TrySpendGold(selectedDefinition.goldCost))
        {
            NotificationToast.Instance?.Show("Not enough gold.");
            return;
        }

        BaseLayoutManager layout = BaseLayoutManager.Instance;
        float y = layout.GetWorldYForFloor(pendingFloorIndex);
        Vector3 position = new Vector3(pendingCenterX, y, layout.EntranceWorldZ);

        // Existing RoomBase.OnEnable -> BaseLayoutManager.RegisterRoom handles registration. For lift
        // segments, LiftRoom.Start()'s existing auto-grouping takes over from here automatically.
        Instantiate(selectedDefinition.prefab, position, Quaternion.Euler(0f, 180f, 0f));

        // Deliberately stay in build mode with the same definition selected — a lift shaft is meant to
        // be extended by placing several segments in a row (see design doc), and there's no reason a
        // standard room couldn't be placed repeatedly the same way. Escape/Cancel exits whenever done.
    }
}
