using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Linq;

// Structural sibling of GuardDeployUI — opens alongside AssignmentUI on a room click (see
// RoomClickHandler), but no-ops (stays closed) unless the clicked room is actually a HospitalRoom, same
// "conditionally shows up next to AssignmentUI" pattern GuardDeployUI already uses for invasions. Assigns
// injured bunnies to Hospital BedSpots rather than a job spot — see HospitalRoom.RequestBed /
// NPCBunny.AssignToHospitalBed.
public class PatientUI : MonoBehaviour
{
    public static PatientUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI roomNameLabel;
    [Tooltip("Shown instead of the roster when the target room's BedSpots are all occupied.")]
    [SerializeField] private TextMeshProUGUI statusLabel;

    [Header("Injured Bunnies (click to assign to a bed)")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject bunnyButtonPrefab; // same simple Button + TextMeshProUGUI prefab as AssignmentUI/GuardDeployUI
    [SerializeField] private GameObject typeIconPrefab;

    private HospitalRoom currentRoom;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);
    }

    // Called by RoomClickHandler alongside AssignmentUI.OpenForRoom — no-ops (stays closed) unless the
    // clicked room is actually a Hospital.
    public void OpenForRoom(RoomBase room, string roomDisplayName)
    {
        if (!(room is HospitalRoom hospitalRoom)) return;

        currentRoom = hospitalRoom;
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

    private void PopulateList()
    {
        foreach (Transform child in buttonContainer)
            Destroy(child.gameObject);

        if (!currentRoom.HasAvailableBed())
        {
            if (statusLabel != null)
            {
                statusLabel.gameObject.SetActive(true);
                statusLabel.text = "No free beds.";
            }
            return;
        }

        if (statusLabel != null) statusLabel.gameObject.SetActive(false);

        if (DwellerRoster.Instance == null) return;

        // Already sorted ascending by HP% (lowest first) by GetInjuredBunnies itself.
        foreach (NPCBunny bunny in DwellerRoster.Instance.GetInjuredBunnies())
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, buttonContainer);
            TextMeshProUGUI nameLabel = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            nameLabel.text = $"{bunny.name} — {bunny.HPValue}/{bunny.Stats.HP}";
            BunnyTypeIconHelper.AddIcon(typeIconPrefab, buttonObj.transform, nameLabel, bunny.TypeIcon);

            Button btn = buttonObj.GetComponent<Button>();
            btn.onClick.AddListener(() => AssignBunny(bunny));
        }
    }

    private void AssignBunny(NPCBunny bunny)
    {
        RoomSpot spot = currentRoom.RequestBed(bunny);
        if (spot == null)
        {
            NotificationToast.Instance.Show("No free beds.");
            PopulateList(); // refresh — someone else likely filled the last bed
            return;
        }

        AudioManager.EnsureInstance().PlayButtonClick();
        bunny.AssignToHospitalBed(spot, currentRoom);

        PopulateList(); // panel stays open for further assignments
    }
}
