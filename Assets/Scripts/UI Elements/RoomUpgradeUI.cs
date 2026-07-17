using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Click-to-open panel showing a room's current Grade and, if a higher-Grade variant of this room type
// has been authored, an "Upgrade to Grade N" button with its Gold cost. Deliberately independent of
// AssignmentUI/RoomClickHandler (which only exist for IJobRoom rooms) — upgrading applies to every room
// type, including purely decorative ones like LivingRoom/BedroomRoom that have no job-assignment flow
// at all. Opened by RoomUpgradeClickHandler.
public class RoomUpgradeUI : MonoBehaviour
{
    public static RoomUpgradeUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI roomNameLabel;
    [SerializeField] private TextMeshProUGUI gradeLabel;

    [Header("Upgrade Button")]
    [SerializeField] private Button upgradeButton;
    [SerializeField] private TextMeshProUGUI upgradeButtonLabel; // e.g. "Upgrade to Grade 2 (150 Gold)"
    [Tooltip("Shown instead of the button when there's nothing to upgrade to (already at cap Grade, or no variant authored yet).")]
    [SerializeField] private GameObject noUpgradeAvailableLabel;

    private RoomBase currentRoom;
    private RoomDefinition targetDefinition;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        // Defensive against duplicated/re-wired buttons carrying over stale Inspector OnClick() entries
        // — same pattern AssignmentUI/BuildModeController/DeleteModeController already use.
        upgradeButton.onClick.RemoveAllListeners();
        upgradeButton.onClick.AddListener(OnUpgradeClicked);
    }

    public void OpenForRoom(RoomBase room)
    {
        if (room == null) return;

        currentRoom = room;
        if (roomNameLabel != null) roomNameLabel.text = room.name;
        if (gradeLabel != null) gradeLabel.text = $"Grade {room.Grade}";
        panelRoot.SetActive(true);
        RefreshUpgradeOption();
    }

    public void Close()
    {
        panelRoot.SetActive(false);
        currentRoom = null;
        targetDefinition = null;
    }

    private void RefreshUpgradeOption()
    {
        targetDefinition = RoomCatalogRegistry.Instance != null
            ? RoomCatalogRegistry.Instance.FindVariant(currentRoom.RoomTypeId, Mathf.RoundToInt(currentRoom.FootprintWidth), currentRoom.Grade + 1)
            : null;

        bool hasUpgrade = targetDefinition != null;

        upgradeButton.gameObject.SetActive(hasUpgrade);
        if (noUpgradeAvailableLabel != null)
            noUpgradeAvailableLabel.SetActive(!hasUpgrade);

        if (hasUpgrade && upgradeButtonLabel != null)
            upgradeButtonLabel.text = $"Upgrade to Grade {targetDefinition.grade} ({targetDefinition.goldCost} Gold)";
    }

    private void OnUpgradeClicked()
    {
        if (currentRoom == null || targetDefinition == null) return;

        if (GoldManager.Instance == null || !GoldManager.Instance.TrySpendGold(targetDefinition.goldCost))
        {
            NotificationToast.Instance?.Show("Not enough gold.");
            return;
        }

        // Never routes through CanBeDeleted() — that guard exists to block an unintended destructive
        // click on an active room, but upgrading (like merging) needs to work on actively-staffed rooms.
        // This is what makes EntranceRoom upgradable despite CanBeDeleted() unconditionally blocking
        // deletion (a separate, orthogonal axis — see EntranceRoom.cs).
        RoomTransitionService.Instance?.UpgradeRoom(currentRoom, targetDefinition);
        Close();
    }
}
