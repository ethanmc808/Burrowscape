using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Reuses the same underlying mechanism RoomClickHandler/OnMouseDown already rely on (a camera-to-collider
// physics raycast), just centralized here so delete mode works on every room type without needing a new
// component added to every room prefab.
public class DeleteModeController : MonoBehaviour
{
    public static DeleteModeController Instance { get; private set; }

    [Tooltip("Optional — Escape key always works regardless.")]
    [SerializeField] private Button cancelButton;

    private bool isActive;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(ExitDeleteMode);
        }
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
    }

    public void EnterDeleteMode()
    {
        PlacementModeManager.Instance?.RequestMode(PlacementMode.Delete);
    }

    public void ExitDeleteMode()
    {
        if (PlacementModeManager.Instance != null && PlacementModeManager.Instance.CurrentMode == PlacementMode.Delete)
            PlacementModeManager.Instance.RequestMode(PlacementMode.None);
    }

    private void Update()
    {
        if (!isActive) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ExitDeleteMode();
            return;
        }

        if (Input.GetMouseButtonDown(0) && !IsPointerOverUI())
            TryDeleteAtMouse();
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void TryDeleteAtMouse()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        RoomBase room = hit.collider.GetComponentInParent<RoomBase>();
        if (room == null) return;

        if (!room.CanBeDeleted(out string blockedReason))
        {
            RoomDeletionNotification.Instance?.Show($"Can't delete room: {blockedReason}.");
            return;
        }

        Destroy(room.gameObject);
    }
}
