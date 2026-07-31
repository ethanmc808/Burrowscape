using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

// One-at-a-time Guard Room deploy panel — structural sibling of AssignmentUI, but gated on invasion
// state: only actually shows while the clicked room has an active invasion (InvasionManager.
// IsRoomInvaded), and unlike AssignmentUI's "assign, then close" flow, stays open after each deploy so
// the player can send more guards (see Combat_DesignDoc.md's Guard Room section, revised away from a
// whole-squad deploy in favor of one bunny per click).
public class GuardDeployUI : MonoBehaviour
{
    public static GuardDeployUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI roomNameLabel;
    [Tooltip("Shown instead of the roster when the target room's CombatSpots are all occupied.")]
    [SerializeField] private TextMeshProUGUI statusLabel;

    [Header("Deployable Guards (click to deploy)")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject bunnyButtonPrefab; // same simple Button + TextMeshProUGUI prefab as AssignmentUI
    [SerializeField] private GameObject typeIconPrefab;

    private RoomBase currentRoom;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (InvasionManager.Instance != null)
            InvasionManager.Instance.OnInvasionCleared += HandleInvasionCleared;
    }

    private void OnDisable()
    {
        if (InvasionManager.Instance != null)
            InvasionManager.Instance.OnInvasionCleared -= HandleInvasionCleared;
    }

    // Called by RoomClickHandler alongside AssignmentUI.OpenForRoom — no-ops (stays closed) unless the
    // clicked room actually has an active invasion right now.
    public void OpenForRoom(RoomBase room, string roomDisplayName)
    {
        if (InvasionManager.Instance == null || !InvasionManager.Instance.IsRoomInvaded(room))
            return;

        currentRoom = room;
        roomNameLabel.text = roomDisplayName;
        panelRoot.SetActive(true);
        PopulateList();
    }

    public void Close() => Close(true);

    public void Close(bool playSound)
    {
        if (!panelRoot.activeSelf) return;

        panelRoot.SetActive(false);
        if (playSound) AudioManager.EnsureInstance().PlayUIClose();
        currentRoom = null;
    }

    private void HandleInvasionCleared(RoomBase room)
    {
        if (currentRoom == room)
            Close(false);
    }

    private void PopulateList()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        if (!currentRoom.HasAvailableCombatSpot())
        {
            if (statusLabel != null)
            {
                statusLabel.gameObject.SetActive(true);
                statusLabel.text = "Room full.";
            }
            return;
        }

        if (statusLabel != null) statusLabel.gameObject.SetActive(false);

        foreach (NPCBunny bunny in GetDeployableGuards())
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, buttonContainer);
            TextMeshProUGUI nameLabel = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            nameLabel.text = bunny.name;
            BunnyTypeIconHelper.AddIcon(typeIconPrefab, buttonObj.transform, nameLabel, bunny.TypeIcon);

            Button btn = buttonObj.GetComponent<Button>();
            btn.onClick.AddListener(() => DeployBunny(bunny));
        }
    }

    // Every Guard Room's posted (CurrentState == Working, not already deployed elsewhere) roster, across
    // every registered Guard Room — no "nearest Guard Room" picker, since deploy is now per-bunny rather
    // than per-room.
    private List<NPCBunny> GetDeployableGuards()
    {
        if (BaseManager.Instance == null || DwellerRoster.Instance == null) return new List<NPCBunny>();

        List<NPCBunny> deployable = new List<NPCBunny>();
        foreach (GuardRoom guardRoom in BaseManager.Instance.GuardRooms)
        {
            deployable.AddRange(DwellerRoster.Instance.GetBunniesAssignedTo(guardRoom)
                .Where(b => b.CurrentState == BunnyState.Working && !b.IsDefending));
        }
        return deployable;
    }

    private void DeployBunny(NPCBunny bunny)
    {
        RoomSpot spot = currentRoom.ClaimCombatSpot(bunny);
        if (spot == null)
        {
            NotificationToast.Instance.Show("Room full.");
            PopulateList(); // refresh — someone else likely filled the last spot
            return;
        }

        AudioManager.EnsureInstance().PlayButtonClick();
        bunny.BeginDefending(spot, currentRoom);

        PopulateList(); // panel stays open for further deploys
    }
}
