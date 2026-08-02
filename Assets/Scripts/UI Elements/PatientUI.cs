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

    [Header("Currently Bedded Patients (click to select, then Unassign)")]
    [SerializeField] private Transform assignedButtonContainer;
    [SerializeField] private Button unassignButton;

    [Header("Selection Highlight")]
    [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.4f);
    [SerializeField] private Color normalColor = Color.white;

    private HospitalRoom currentRoom;
    private NPCBunny selectedPatient;
    private Button selectedPatientButton;

    private void Awake()
    {
        Instance = this;
        panelRoot.SetActive(false);

        if (unassignButton != null)
        {
            unassignButton.onClick.RemoveAllListeners();
            unassignButton.onClick.AddListener(OnUnassignClicked);
            unassignButton.interactable = false;
        }
    }

    // Called by RoomClickHandler alongside AssignmentUI.OpenForRoom — no-ops (stays closed) unless the
    // clicked room is actually a Hospital.
    public void OpenForRoom(RoomBase room, string roomDisplayName)
    {
        if (!(room is HospitalRoom hospitalRoom)) return;

        currentRoom = hospitalRoom;
        roomNameLabel.text = roomDisplayName;
        panelRoot.SetActive(true);
        ClearSelection();
        PopulateLists();
    }

    public void Close() => Close(true);

    public void Close(bool playSound)
    {
        if (!panelRoot.activeSelf) return;

        panelRoot.SetActive(false);
        if (playSound) AudioManager.EnsureInstance().PlayUIClose();
        currentRoom = null;
        ClearSelection();
    }

    private void PopulateLists()
    {
        PopulateInjuredList();
        PopulateAssignedList();
    }

    private void PopulateInjuredList()
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

    // Every bunny currently claimed to a bed here (walking to it or already Recovering), so the player has
    // a way to manually pull one out early — previously there was no way to release a bed claim at all
    // short of the patient healing to full on its own.
    private void PopulateAssignedList()
    {
        if (assignedButtonContainer == null) return;

        foreach (Transform child in assignedButtonContainer)
            Destroy(child.gameObject);

        if (DwellerRoster.Instance == null) return;

        foreach (NPCBunny bunny in DwellerRoster.Instance.GetPatientsIn(currentRoom))
        {
            GameObject buttonObj = Instantiate(bunnyButtonPrefab, assignedButtonContainer);
            TextMeshProUGUI nameLabel = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            nameLabel.text = $"{bunny.name} — {bunny.HPValue}/{bunny.Stats.HP}";
            BunnyTypeIconHelper.AddIcon(typeIconPrefab, buttonObj.transform, nameLabel, bunny.TypeIcon);

            Button btn = buttonObj.GetComponent<Button>();
            btn.onClick.AddListener(() => SelectPatient(bunny, btn));
        }
    }

    private void AssignBunny(NPCBunny bunny)
    {
        RoomSpot spot = currentRoom.RequestBed(bunny);
        if (spot == null)
        {
            NotificationToast.Instance.Show("No free beds.");
            PopulateLists(); // refresh — someone else likely filled the last bed
            return;
        }

        AudioManager.EnsureInstance().PlayButtonClick();
        bunny.AssignToHospitalBed(spot, currentRoom);

        PopulateLists(); // panel stays open for further assignments
    }

    private void SelectPatient(NPCBunny bunny, Button btn)
    {
        // Clicking the already-selected patient again deselects it — same shape as AssignmentUI.
        if (selectedPatient == bunny)
        {
            ClearSelection();
            return;
        }

        ResetButtonColor(selectedPatientButton);

        selectedPatient = bunny;
        selectedPatientButton = btn;
        if (unassignButton != null) unassignButton.interactable = true;

        SetButtonColor(btn, selectedColor);
    }

    private void OnUnassignClicked()
    {
        if (selectedPatient == null) return;

        AudioManager.EnsureInstance().PlayButtonClick();
        selectedPatient.ReturnFromHospital(); // releases the bed and walks the bunny back to whatever it was doing before

        ClearSelection();
        PopulateLists(); // bunny moves from the assigned list back into the injured list (still under max HP)
    }

    private void ClearSelection()
    {
        ResetButtonColor(selectedPatientButton);
        selectedPatient = null;
        selectedPatientButton = null;
        if (unassignButton != null) unassignButton.interactable = false;
    }

    private void SetButtonColor(Button btn, Color color)
    {
        Image img = btn.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    private void ResetButtonColor(Button btn)
    {
        if (btn == null) return;
        SetButtonColor(btn, normalColor);
    }
}
