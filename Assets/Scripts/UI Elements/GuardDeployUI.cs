using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// One-at-a-time manual reinforcement panel — structural sibling of AssignmentUI, but gated on invasion
// state: only actually shows while the clicked room has an active invasion (InvasionManager.
// IsRoomInvaded), and unlike AssignmentUI's "assign, then close" flow, stays open after each deploy so
// the player can send more reinforcements (see Combat_DesignDoc.md's Guard Room section, revised away
// from a whole-squad deploy in favor of one bunny per click). Originally scoped to Guard Room troops
// only; broadened to any safely-interruptible resident bunny once the "fainted defenders permanently
// deadlock the room" bug made clear that a Guard-Room-only pool left nobody to send in the common case.
public class GuardDeployUI : MonoBehaviour
{
    public static GuardDeployUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI roomNameLabel;
    [Tooltip("Shown instead of the roster when the target room's CombatSpots are all occupied.")]
    [SerializeField] private TextMeshProUGUI statusLabel;

    [Header("Deployable Bunnies (click to deploy)")]
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

        foreach (NPCBunny bunny in GetDeployableBunnies())
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, buttonContainer);
            TextMeshProUGUI nameLabel = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            nameLabel.text = bunny.name;
            BunnyTypeIconHelper.AddIcon(typeIconPrefab, buttonObj.transform, nameLabel, bunny.TypeIcon);

            Button btn = buttonObj.GetComponent<Button>();
            btn.onClick.AddListener(() => DeployBunny(bunny));
        }
    }

    // Any resident bunny safely pullable to reinforce — not just designated Guard Room troops. See
    // DwellerRoster.GetReinforceableBunnies for exactly which states are safe to interrupt this way.
    // TODO: once the click-hold-drag "drop a bunny into a room" feature exists, this list-based deploy
    // flow may be superseded by that for the common case — kept as-is for now.
    private List<NPCBunny> GetDeployableBunnies()
    {
        if (DwellerRoster.Instance == null) return new List<NPCBunny>();
        return DwellerRoster.Instance.GetReinforceableBunnies();
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
